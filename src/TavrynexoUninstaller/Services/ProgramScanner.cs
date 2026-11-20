using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Tavrynexo.Core;
using Tavrynexo.Models;

namespace Tavrynexo.Services;

public static class ProgramScanner
{
    private const string UninstallPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall";

    public static List<InstalledProgram> Scan()
    {
        var result = new List<InstalledProgram>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var sources = new (RegistryHive hive, RegistryView view)[]
        {
            (RegistryHive.LocalMachine, RegistryView.Registry64),
            (RegistryHive.LocalMachine, RegistryView.Registry32),
            (RegistryHive.CurrentUser, RegistryView.Registry64),
        };

        foreach (var (hive, view) in sources)
        {
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var un = baseKey.OpenSubKey(UninstallPath);
                if (un == null) continue;

                foreach (var sub in un.GetSubKeyNames())
                {
                    try
                    {
                        using var k = un.OpenSubKey(sub);
                        if (k == null) continue;
                        var p = Read(k, hive, view, UninstallPath + "\\" + sub);
                        if (p == null) continue;
                        if (!seen.Add($"{p.Name}|{p.Version}|{p.Publisher}")) continue;
                        result.Add(p);
                    }
                    catch (Exception ex) { Logger.Error($"Read uninstall key {sub}", ex); }
                }
            }
            catch (Exception ex) { Logger.Error($"Open uninstall root {hive}/{view}", ex); }
        }

        return result.OrderBy(p => p.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static InstalledProgram? Read(RegistryKey k, RegistryHive hive, RegistryView view, string subPath)
    {
        string Str(string n) => (k.GetValue(n) as string)?.Trim() ?? "";

        var name = Str("DisplayName");
        if (string.IsNullOrWhiteSpace(name)) return null;
        if (k.GetValue("SystemComponent") is int sc && sc == 1) return null;
        if (!string.IsNullOrEmpty(Str("ParentKeyName"))) return null;

        var release = Str("ReleaseType");
        if (release is "Update" or "Hotfix" or "Security Update" or "Update Rollup" or "ServicePack") return null;
        if (Regex.IsMatch(name, @"^(KB\d{5,}|Security Update for|Update for|Hotfix for)", RegexOptions.IgnoreCase)) return null;

        var p = new InstalledProgram
        {
            Name = name,
            Publisher = Str("Publisher"),
            Version = Str("DisplayVersion"),
            UninstallString = Str("UninstallString"),
            QuietUninstallString = Str("QuietUninstallString"),
            DisplayIcon = Str("DisplayIcon"),
            Hive = hive,
            View = view,
            SubKeyPath = subPath,
        };

        p.InstallLocation = DeriveLocation(Str("InstallLocation"), p.DisplayIcon, p.UninstallString);

        try { var v = k.GetValue("EstimatedSize"); if (v != null) p.SizeBytes = Convert.ToInt64(v, CultureInfo.InvariantCulture) * 1024; } catch { }

        var d = Str("InstallDate");
        if (DateTime.TryParseExact(d, "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var dt))
            p.InstallDate = dt;

        return p;
    }

    private static string DeriveLocation(string loc, string displayIcon, string uninstall)
    {
        if (!string.IsNullOrWhiteSpace(loc))
        {
            var l = Environment.ExpandEnvironmentVariables(loc.Trim().Trim('"')).TrimEnd('\\');
            if (Directory.Exists(l)) return l;
        }

        foreach (var cand in new[] { ExtractFilePath(displayIcon), ExtractFilePath(uninstall) })
        {
            if (cand == null) continue;
            var dir = Path.GetDirectoryName(cand);
            if (string.IsNullOrEmpty(dir)) continue;
            var top = TopLevelUnderKnownRoot(dir);
            if (top != null) return top;
        }
        return "";
    }

    private static string? TopLevelUnderKnownRoot(string dir)
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roots = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            Path.Combine(local, "Programs"),
            local,
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        };

        foreach (var root in roots)
        {
            if (string.IsNullOrEmpty(root)) continue;
            if (!dir.StartsWith(root + "\\", StringComparison.OrdinalIgnoreCase)) continue;
            var rest = dir[(root.Length + 1)..];
            var seg = rest.Split('\\')[0];
            var cand = Path.Combine(root, seg);
            if (Directory.Exists(cand) && !Safety.IsProtectedPath(cand)) return cand;
        }
        return null;
    }

    /// <summary>Extracts the executable/icon path from strings like "C:\a\b.exe",0 or C:\a\b.exe /S.</summary>
    public static string? ExtractFilePath(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        s = Environment.ExpandEnvironmentVariables(s.Trim());
        string path;
        if (s.StartsWith('"'))
        {
            var end = s.IndexOf('"', 1);
            if (end < 0) return null;
            path = s.Substring(1, end - 1);
        }
        else
        {
            var m = Regex.Match(s, @"^(.+?\.(exe|ico|dll|msi|cpl|bat|cmd))", RegexOptions.IgnoreCase);
            path = m.Success ? m.Groups[1].Value : s;
            var comma = path.LastIndexOf(',');
            if (!m.Success && comma > 2 && int.TryParse(path[(comma + 1)..].Trim(), out _)) path = path[..comma];
        }

        try
        {
            if (!Path.IsPathRooted(path)) return null;
            var file = Path.GetFileName(path).ToLowerInvariant();
            if (file is "msiexec.exe" or "cmd.exe" or "rundll32.exe" or "powershell.exe") return null;
            return path;
        }
        catch { return null; }
    }

    /// <summary>Loads icons and missing sizes on background threads so the list appears instantly.</summary>
    public static Task LoadIconsAndSizesAsync(IEnumerable<InstalledProgram> programs)
    {
        var list = programs.ToList();
        return Task.Run(() =>
        {
            Parallel.ForEach(list, new ParallelOptions { MaxDegreeOfParallelism = 4 }, p =>
            {
                try { p.Icon = LoadIcon(p); } catch { }
                try
                {
                    if (p.SizeBytes <= 0 && !string.IsNullOrEmpty(p.InstallLocation) && !Safety.IsProtectedPath(p.InstallLocation))
                        p.SizeBytes = Fmt.DirSize(p.InstallLocation);
                }
                catch { }
            });
        });
    }

    private static System.Windows.Media.ImageSource? LoadIcon(InstalledProgram p)
    {
        var candidates = new List<string>();
        var di = ExtractFilePath(p.DisplayIcon);
        if (di != null) candidates.Add(di);

        if (!string.IsNullOrEmpty(p.InstallLocation) && Directory.Exists(p.InstallLocation))
        {
            try
            {
                var exe = Directory.EnumerateFiles(p.InstallLocation, "*.exe", SearchOption.TopDirectoryOnly)
                    .FirstOrDefault(f =>
                    {
                        var n = Path.GetFileName(f).ToLowerInvariant();
                        return !n.StartsWith("unins") && !n.StartsWith("uninstall") && !n.StartsWith("setup") && !n.StartsWith("update");
                    });
                if (exe != null) candidates.Add(exe);
            }
            catch { }
        }

        var us = ExtractFilePath(p.UninstallString);
        if (us != null) candidates.Add(us);

        foreach (var c in candidates)
        {
            try
            {
                if (!File.Exists(c)) continue;
                var img = Native.GetFileIcon(c);
                if (img != null) return img;
            }
            catch { }
        }
        return null;
    }
}
