using System.Diagnostics;
using System.IO;
using Tavrynexo.Core;
using Tavrynexo.Models;

namespace Tavrynexo.Services;

public sealed class DeleteResult
{
    public int Deleted;
    public int Failed;
    public int ScheduledForReboot;
    public List<string> FailedItems { get; } = new();
    public string BackupFolder { get; set; } = "";
}

public static class CleanupService
{
    public static DeleteResult Delete(IEnumerable<LeftoverItem> items, string programName, bool backupRegistry, IProgress<string>? progress = null)
    {
        var res = new DeleteResult();
        var backupDir = Path.Combine(AppPaths.BackupDir, $"{DateTime.Now:yyyyMMdd_HHmmss}_{Sanitize(programName)}");
        res.BackupFolder = backupDir;
        var backedUp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int backupCounter = 0;

        var ordered = items
            .OrderBy(i => Order(i.Kind))
            .ThenByDescending(i => i.Path.Length)
            .ToList();

        foreach (var it in ordered)
        {
            progress?.Report(it.DisplayPath);
            try
            {
                switch (it.Kind)
                {
                    case LeftoverKind.Service: DeleteService(it, res); break;
                    case LeftoverKind.Task: DeleteTask(it, res); break;
                    case LeftoverKind.Folder: DeleteFolder(it, res); break;
                    case LeftoverKind.File: DeleteFile(it, res); break;
                    case LeftoverKind.RegKey:
                        DeleteRegKey(it, res, backupRegistry, backupDir, backedUp, ref backupCounter); break;
                    case LeftoverKind.RegValue:
                        DeleteRegValue(it, res, backupRegistry, backupDir, backedUp, ref backupCounter); break;
                }
            }
            catch (Exception ex)
            {
                Fail(res, it, ex.Message);
            }
        }

        Logger.Info($"Cleanup [{programName}] deleted={res.Deleted} failed={res.Failed} reboot={res.ScheduledForReboot}");
        return res;
    }

    private static int Order(LeftoverKind k) => k switch
    {
        LeftoverKind.Service => 0,
        LeftoverKind.Task => 1,
        LeftoverKind.Folder => 2,
        LeftoverKind.File => 3,
        LeftoverKind.RegValue => 4,
        _ => 5,
    };

    private static void Fail(DeleteResult res, LeftoverItem it, string reason)
    {
        res.Failed++;
        res.FailedItems.Add($"{it.DisplayPath}  ({reason})");
        Logger.Error($"Delete failed {it.Kind} {it.DisplayPath}: {reason}");
    }

    // ---------------- services / tasks ----------------

    private static void DeleteService(LeftoverItem it, DeleteResult res)
    {
        ProcessRunner.Run("sc.exe", $"stop \"{it.Path}\"", 30_000);
        var (code, _, err) = ProcessRunner.Run("sc.exe", $"delete \"{it.Path}\"", 30_000);
        if (code == 0 || code == 1060) res.Deleted++;          // 1060 = does not exist
        else if (code == 1072) res.ScheduledForReboot++;       // marked for deletion
        else Fail(res, it, $"sc exit {code} {err}".Trim());
    }

    private static void DeleteTask(LeftoverItem it, DeleteResult res)
    {
        var (code, _, err) = ProcessRunner.Run("schtasks.exe", $"/Delete /TN \"{it.Path}\" /F", 30_000);
        if (code == 0) res.Deleted++;
        else Fail(res, it, $"schtasks exit {code} {err}".Trim());
    }

    // ---------------- files / folders ----------------

    private static void DeleteFile(LeftoverItem it, DeleteResult res)
    {
        if (Safety.IsProtectedPath(it.Path)) { Fail(res, it, "protected path"); return; }
        if (!File.Exists(it.Path)) { res.Deleted++; return; }

        try
        {
            File.SetAttributes(it.Path, FileAttributes.Normal);
            File.Delete(it.Path);
            res.Deleted++;
        }
        catch
        {
            if (Native.DeleteOnReboot(it.Path)) res.ScheduledForReboot++;
            else Fail(res, it, "in use");
        }
    }

    private static void DeleteFolder(LeftoverItem it, DeleteResult res)
    {
        if (Safety.IsProtectedPath(it.Path)) { Fail(res, it, "protected path"); return; }
        if (!Directory.Exists(it.Path)) { res.Deleted++; PruneEmptyParent(it); return; }

        KillProcessesIn(it.Path);

        var before = res.ScheduledForReboot;
        var gone = DeleteDirectoryRobust(it.Path, res);
        if (gone) { res.Deleted++; PruneEmptyParent(it); }
        else if (res.ScheduledForReboot > before) { /* counted as scheduled */ }
        else Fail(res, it, "in use or access denied");
    }

    private static bool DeleteDirectoryRobust(string path, DeleteResult res)
    {
        var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, AttributesToSkip = 0 };

        // Remove read-only flags first (a common reason Directory.Delete fails).
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", opt))
            {
                try { File.SetAttributes(f, FileAttributes.Normal); } catch { }
            }
        }
        catch { }

        try { Directory.Delete(path, true); return true; } catch { }

        // File by file; whatever is locked is scheduled for deletion at the next reboot.
        try
        {
            foreach (var f in Directory.EnumerateFiles(path, "*", opt).ToList())
            {
                try { File.Delete(f); }
                catch { if (Native.DeleteOnReboot(f)) res.ScheduledForReboot++; }
            }

            foreach (var d in Directory.EnumerateDirectories(path, "*", opt).OrderByDescending(d => d.Length).ToList())
            {
                try { Directory.Delete(d, false); }
                catch { Native.DeleteOnReboot(d); }
            }
        }
        catch { }

        try { Directory.Delete(path, false); } catch { Native.DeleteOnReboot(path); }
        return !Directory.Exists(path);
    }

    private static void KillProcessesIn(string folder)
    {
        var prefix = folder.TrimEnd('\\') + "\\";
        foreach (var pr in Process.GetProcesses())
        {
            try
            {
                if (pr.Id == Environment.ProcessId) continue;
                var file = pr.MainModule?.FileName;
                if (file != null && file.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    pr.Kill(true);
                    pr.WaitForExit(3000);
                }
            }
            catch { }
            finally { pr.Dispose(); }
        }
    }

    private static void PruneEmptyParent(LeftoverItem it)
    {
        try
        {
            if (string.IsNullOrEmpty(it.PublisherKey)) return;
            var parent = Path.GetDirectoryName(it.Path.TrimEnd('\\'));
            if (string.IsNullOrEmpty(parent) || !Directory.Exists(parent)) return;
            if (Fmt.Norm(Path.GetFileName(parent)) != it.PublisherKey) return;
            if (Safety.IsProtectedPath(parent)) return;
            if (Directory.EnumerateFileSystemEntries(parent).Any()) return;
            Directory.Delete(parent, false);
        }
        catch { }
    }

    // ---------------- registry ----------------

    private static bool Backup(string keyPath, string backupDir, HashSet<string> done, ref int counter)
    {
        if (!done.Add(keyPath)) return true;
        counter++;
        var last = keyPath[(keyPath.LastIndexOf('\\') + 1)..];
        var file = Path.Combine(backupDir, $"{counter:000}_{Sanitize(last)}.reg");
        return RegistryHelper.Export(keyPath, file);
    }

    private static void DeleteRegKey(LeftoverItem it, DeleteResult res, bool backup, string backupDir,
                                     HashSet<string> done, ref int counter)
    {
        if (Safety.IsProtectedRegistryPath(it.Path)) { Fail(res, it, "protected key"); return; }
        if (!RegistryHelper.KeyExists(it.Path)) { res.Deleted++; return; }

        if (backup && !Backup(it.Path, backupDir, done, ref counter)) { Fail(res, it, "registry backup failed - not deleted"); return; }

        if (RegistryHelper.DeleteKeyTree(it.Path))
        {
            res.Deleted++;
            PruneEmptyParentKey(it);
        }
        else Fail(res, it, "access denied");
    }

    private static void DeleteRegValue(LeftoverItem it, DeleteResult res, bool backup, string backupDir,
                                       HashSet<string> done, ref int counter)
    {
        if (string.IsNullOrEmpty(it.ValueName)) { Fail(res, it, "no value name"); return; }
        if (!RegistryHelper.KeyExists(it.Path)) { res.Deleted++; return; }

        if (backup && !Backup(it.Path, backupDir, done, ref counter)) { Fail(res, it, "registry backup failed - not deleted"); return; }

        RegistryHelper.DeleteValue(it.Path, it.ValueName!);
        res.Deleted++;
    }

    private static void PruneEmptyParentKey(LeftoverItem it)
    {
        try
        {
            if (string.IsNullOrEmpty(it.PublisherKey)) return;
            var idx = it.Path.LastIndexOf('\\');
            if (idx < 0) return;
            var parent = it.Path[..idx];
            var parentName = parent[(parent.LastIndexOf('\\') + 1)..];
            if (Fmt.Norm(parentName) != it.PublisherKey) return;
            if (Safety.IsProtectedRegistryPath(parent) || Safety.SystemVendors.Contains(it.PublisherKey)) return;
            if (RegistryHelper.IsEmptyKey(parent)) RegistryHelper.DeleteKeyTree(parent);
        }
        catch { }
    }

    private static string Sanitize(string s)
    {
        var bad = Path.GetInvalidFileNameChars();
        var clean = new string(s.Select(c => bad.Contains(c) || c == ' ' ? '_' : c).ToArray());
        return clean.Length > 40 ? clean[..40] : clean;
    }
}
