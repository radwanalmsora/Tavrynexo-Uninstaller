using System.IO;
using Tavrynexo.Core;
using Tavrynexo.Models;

namespace Tavrynexo.Services;

public static class JunkService
{
    public static List<JunkCategory> BuildCategories()
    {
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);

        var firefox = new List<string>();
        try
        {
            var profiles = Path.Combine(local, "Mozilla", "Firefox", "Profiles");
            if (Directory.Exists(profiles))
                foreach (var d in Directory.EnumerateDirectories(profiles))
                    firefox.Add(Path.Combine(d, "cache2"));
        }
        catch { }

        static List<string> Browser(string root) => new()
        {
            Path.Combine(root, "User Data", "Default", "Cache"),
            Path.Combine(root, "User Data", "Default", "Code Cache"),
            Path.Combine(root, "User Data", "Default", "GPUCache"),
        };

        return new List<JunkCategory>
        {
            new() { Key = "JunkUserTemp", Title = L.Get("JunkUserTemp"), MinAgeMinutes = 60,
                    Paths = { Path.GetTempPath().TrimEnd('\\') } },
            new() { Key = "JunkWinTemp", Title = L.Get("JunkWinTemp"), MinAgeMinutes = 60,
                    Paths = { Path.Combine(win, "Temp") } },
            new() { Key = "JunkWer", Title = L.Get("JunkWer"),
                    Paths = { Path.Combine(common, "Microsoft", "Windows", "WER", "ReportArchive"),
                              Path.Combine(common, "Microsoft", "Windows", "WER", "ReportQueue"),
                              Path.Combine(local, "Microsoft", "Windows", "WER") } },
            new() { Key = "JunkChrome", Title = L.Get("JunkChrome"), Paths = Browser(Path.Combine(local, "Google", "Chrome")) },
            new() { Key = "JunkEdge", Title = L.Get("JunkEdge"), Paths = Browser(Path.Combine(local, "Microsoft", "Edge")) },
            new() { Key = "JunkFirefox", Title = L.Get("JunkFirefox"), Paths = firefox },
            new() { Key = "JunkD3D", Title = L.Get("JunkD3D"), Paths = { Path.Combine(local, "D3DSCache") } },
            new() { Key = "JunkRecycle", Title = L.Get("JunkRecycle"), IsRecycleBin = true },
        };
    }

    private static EnumerationOptions Opts => new()
    {
        RecurseSubdirectories = true,
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.ReparsePoint,
    };

    public static void Analyze(JunkCategory c, CancellationToken ct = default)
    {
        long total = 0;
        if (c.IsRecycleBin) { c.Size = Native.RecycleBinSize(); return; }

        var cutoff = DateTime.Now.AddMinutes(-c.MinAgeMinutes);
        foreach (var path in c.Paths)
        {
            try
            {
                if (!Directory.Exists(path)) continue;
                foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", Opts))
                {
                    if (ct.IsCancellationRequested) break;
                    try { if (f.LastWriteTime <= cutoff) total += f.Length; } catch { }
                }
            }
            catch { }
        }
        c.Size = total;
    }

    /// <summary>Deletes the contents (never the root folder itself). Files that are in use are skipped. Returns bytes freed.</summary>
    public static long Clean(JunkCategory c)
    {
        long freed = 0;
        if (c.IsRecycleBin)
        {
            var size = Native.RecycleBinSize();
            if (Native.EmptyRecycleBin()) freed += size;
            return freed;
        }

        var cutoff = DateTime.Now.AddMinutes(-c.MinAgeMinutes);
        foreach (var path in c.Paths)
        {
            try
            {
                // Paths come only from the hard-coded list in BuildCategories(); just guard against anything absurd.
                if (path.Length < 12 || !Directory.Exists(path)) continue;

                foreach (var f in new DirectoryInfo(path).EnumerateFiles("*", Opts).ToList())
                {
                    try
                    {
                        if (f.LastWriteTime > cutoff) continue;
                        var len = f.Length;
                        f.Attributes = FileAttributes.Normal;
                        f.Delete();
                        freed += len;
                    }
                    catch { }
                }

                foreach (var d in Directory.EnumerateDirectories(path, "*", Opts).OrderByDescending(x => x.Length).ToList())
                {
                    try { if (!Directory.EnumerateFileSystemEntries(d).Any()) Directory.Delete(d, false); } catch { }
                }
            }
            catch (Exception ex) { Logger.Error("Junk clean " + path, ex); }
        }

        Logger.Info($"Junk '{c.Key}' freed {freed}");
        return freed;
    }
}
