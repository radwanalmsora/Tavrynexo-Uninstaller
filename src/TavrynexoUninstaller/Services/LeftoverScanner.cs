using System.IO;
using System.Text.RegularExpressions;
using Microsoft.Win32;
using Tavrynexo.Core;
using Tavrynexo.Models;

namespace Tavrynexo.Services;

/// <summary>
/// Finds what a program left behind. Design rules:
///  - Only exact / very close name matches, or things that provably point into the program's install folder.
///  - Exact matches and install-folder references are "High" (pre-selected); fuzzy matches are "Medium" (never pre-selected).
///  - Anything shared with another installed program, or inside a protected location, is skipped.
/// </summary>
public static class LeftoverScanner
{
    public static List<LeftoverItem> Scan(InstalledProgram p, IReadOnlyCollection<InstalledProgram> allPrograms, CancellationToken ct = default)
    {
        var items = new List<LeftoverItem>();
        var pubKey = PublisherKey(p.Publisher);

        // ---- Context about OTHER installed programs (to avoid deleting shared stuff) ----
        var others = allPrograms.Where(o => !ReferenceEquals(o, p) && !o.IsStore).ToList();
        var otherLocations = others
            .Select(o => o.InstallLocation).Where(l => !string.IsNullOrWhiteSpace(l))
            .Select(l => l.TrimEnd('\\')).ToList();
        var ownKeys = NameKeys(p, pubKey);
        var otherPrimaryKeys = new HashSet<string>(others.Select(o => Fmt.Norm(Fmt.CleanProductName(o.Name))));
        // Another installed program has the same cleaned name (e.g. two versions installed side by side):
        // name-based matching would hit shared folders, so we fall back to install-folder evidence only.
        bool sharedName = ownKeys.Count > 0 && otherPrimaryKeys.Contains(ownKeys[0]);
        var keys = sharedName ? new List<string>() : ownKeys;

        bool ConflictsWithOthers(string folder)
        {
            var f = folder.TrimEnd('\\');
            foreach (var o in otherLocations)
            {
                if (o.Equals(f, StringComparison.OrdinalIgnoreCase)) return true;
                if (o.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase)) return true; // would delete another program
            }
            return false;
        }

        void Add(LeftoverKind kind, string path, Confidence c, string? valueName = null)
        {
            items.Add(new LeftoverItem
            {
                Kind = kind, Path = path, ValueName = valueName, Confidence = c,
                PublisherKey = pubKey, IsSelected = c == Confidence.High
            });
        }

        void AddFolder(string path, Confidence c)
        {
            if (Safety.IsProtectedPath(path) || ConflictsWithOthers(path)) return;
            Add(LeftoverKind.Folder, path, c);
        }

        // ---- 1. Install folder ----
        string? loc = null;
        if (!string.IsNullOrWhiteSpace(p.InstallLocation))
        {
            var l = p.InstallLocation.Trim().Trim('"').TrimEnd('\\');
            if (Directory.Exists(l) && !Safety.IsProtectedPath(l) && !ConflictsWithOthers(l))
            {
                var parent = Path.GetDirectoryName(l);
                var root = Path.GetPathRoot(l)?.TrimEnd('\\');
                bool driveRootChild = parent != null && root != null && parent.TrimEnd('\\').Equals(root, StringComparison.OrdinalIgnoreCase);
                if (!driveRootChild || Match(Fmt.Norm(Path.GetFileName(l)), ownKeys) != null)
                    loc = l;
            }
        }
        if (loc != null) Add(LeftoverKind.Folder, loc, Confidence.High);

        ct.ThrowIfCancellationRequested();

        // ---- 2. Well-known folders (exact / close name matches) ----
        if (keys.Count > 0)
        {
            var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var roots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                local,
                Path.Combine(local, "Programs"),
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                Path.Combine(profile, "AppData", "LocalLow"),
            };
            foreach (var r in roots)
            {
                ct.ThrowIfCancellationRequested();
                ScanFolderRoot(r, keys, pubKey, AddFolder);
            }

            // ---- 3. Shortcuts ----
            ScanShortcuts(keys, pubKey, AddFolder, (path, c) =>
            {
                if (!Safety.IsProtectedPath(path)) Add(LeftoverKind.File, path, c);
            });
        }

        ct.ThrowIfCancellationRequested();

        // ---- 4. Registry ----
        // The program's own uninstall entry (only still present for force-removal or broken uninstallers).
        var uninstallPath = p.RegistryFullPath64;
        if (!string.IsNullOrEmpty(uninstallPath) && RegistryHelper.KeyExists(uninstallPath) && !Safety.IsProtectedRegistryPath(uninstallPath))
            Add(LeftoverKind.RegKey, uninstallPath, Confidence.High);

        if (keys.Count > 0)
        {
            foreach (var rootPath in new[]
            {
                RegistryHelper.HKCU + @"\SOFTWARE",
                RegistryHelper.HKLM + @"\SOFTWARE",
                RegistryHelper.HKLM + @"\SOFTWARE\WOW6432Node",
            })
            {
                ct.ThrowIfCancellationRequested();
                ScanRegistryRoot(rootPath, keys, pubKey, (path, c) =>
                {
                    if (!Safety.IsProtectedRegistryPath(path)) Add(LeftoverKind.RegKey, path, c);
                });
            }
        }

        // Things that point into the install folder (very reliable evidence).
        if (loc != null && loc.Length >= 8)
        {
            ScanAppPaths(loc, path => { if (!Safety.IsProtectedRegistryPath(path)) Add(LeftoverKind.RegKey, path, Confidence.High); });
            ScanRunKeys(loc, (key, name) => Add(LeftoverKind.RegValue, key, Confidence.High, name));
            ScanServices(loc, name => Add(LeftoverKind.Service, name, Confidence.High));
        }

        ct.ThrowIfCancellationRequested();

        // ---- 5. Scheduled tasks ----
        ScanTasks(keys, loc, (taskPath, c) => Add(LeftoverKind.Task, taskPath, c));

        // ---- Finalize: dedupe, drop children of listed folders, compute sizes ----
        var distinct = items
            .GroupBy(i => $"{i.Kind}|{i.Path}|{i.ValueName}", StringComparer.OrdinalIgnoreCase)
            .Select(g => g.OrderBy(i => i.Confidence).First())
            .ToList();

        var folders = distinct.Where(i => i.Kind == LeftoverKind.Folder).Select(i => i.Path.TrimEnd('\\')).ToList();
        var final = distinct.Where(i =>
        {
            if (i.Kind is not (LeftoverKind.Folder or LeftoverKind.File)) return true;
            var path = i.Path.TrimEnd('\\');
            return !folders.Any(f => !f.Equals(path, StringComparison.OrdinalIgnoreCase) &&
                                     path.StartsWith(f + "\\", StringComparison.OrdinalIgnoreCase));
        }).ToList();

        foreach (var it in final)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                if (it.Kind == LeftoverKind.Folder) it.Size = Fmt.DirSize(it.Path, ct);
                else if (it.Kind == LeftoverKind.File && File.Exists(it.Path)) it.Size = new FileInfo(it.Path).Length;
            }
            catch { }
        }

        return final
            .OrderBy(i => i.Confidence)
            .ThenBy(i => i.Kind)
            .ThenBy(i => i.Path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    // ======================= Name matching =======================

    public static string PublisherKey(string publisher)
    {
        if (string.IsNullOrWhiteSpace(publisher)) return "";
        var drop = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "inc", "llc", "ltd", "limited", "corp", "corporation", "gmbh", "co", "company", "software", "the", "sa", "ab", "ag", "bv", "oy", "pty", "plc", "sarl", "srl" };
        var words = Regex.Split(publisher, @"[\s,\.\-_]+").Where(w => w.Length > 0 && !drop.Contains(w));
        var key = Fmt.Norm(string.Concat(words));
        return key.Length >= 3 ? key : "";
    }

    private static List<string> NameKeys(InstalledProgram p, string pubKey)
    {
        var keys = new List<string>();
        void AddKey(string k)
        {
            if (k.Length >= 4 && !keys.Contains(k) && !Safety.IsBlockedName(k)) keys.Add(k);
        }

        var full = Fmt.Norm(Fmt.CleanProductName(p.Name));
        AddKey(full);
        if (pubKey.Length >= 3 && full.StartsWith(pubKey, StringComparison.Ordinal) && full.Length - pubKey.Length >= 5)
            AddKey(full[pubKey.Length..]);
        AddKey(Fmt.Norm(p.Name));
        return keys;
    }

    private static Confidence? Match(string nk, List<string> keys)
    {
        if (string.IsNullOrEmpty(nk) || keys.Count == 0) return null;
        foreach (var k in keys) if (nk == k) return Confidence.High;
        foreach (var k in keys)
            if (k.Length >= 6 && nk.StartsWith(k, StringComparison.Ordinal) && nk.Length - k.Length <= 8)
                return Confidence.Medium;
        return null;
    }

    // ======================= File system =======================

    private static IEnumerable<string> SafeDirs(string path)
    {
        try { return Directory.EnumerateDirectories(path).ToList(); } catch { return Array.Empty<string>(); }
    }

    private static IEnumerable<string> SafeFiles(string path)
    {
        try { return Directory.EnumerateFiles(path).ToList(); } catch { return Array.Empty<string>(); }
    }

    private static void ScanFolderRoot(string root, List<string> keys, string pubKey, Action<string, Confidence> addFolder)
    {
        if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return;

        foreach (var dir in SafeDirs(root))
        {
            var name = Path.GetFileName(dir);
            var nk = Fmt.Norm(name);

            if (!Safety.IsBlockedName(nk))
            {
                var c = Match(nk, keys);
                if (c != null) { addFolder(dir, c.Value); continue; }
            }

            // Vendor\Product structure (e.g. AppData\Roaming\Vendor\Product)
            if (pubKey.Length >= 3 && nk == pubKey && !Safety.SystemVendors.Contains(nk))
            {
                foreach (var sub in SafeDirs(dir))
                {
                    var sk = Fmt.Norm(Path.GetFileName(sub));
                    if (Safety.IsBlockedName(sk)) continue;
                    var c = Match(sk, keys);
                    if (c != null) addFolder(sub, c.Value);
                }
            }
        }
    }

    private static void ScanShortcuts(List<string> keys, string pubKey, Action<string, Confidence> addFolder, Action<string, Confidence> addFile)
    {
        var startMenus = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.Programs),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        };
        var desktops = new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory),
        };

        static bool IsShortcut(string f) =>
            f.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".url", StringComparison.OrdinalIgnoreCase);

        static string ShortcutKey(string file)
        {
            var k = Fmt.Norm(Path.GetFileNameWithoutExtension(file));
            return k.StartsWith("uninstall") && k.Length > 9 ? k[9..] : k;
        }

        foreach (var root in desktops)
        {
            if (string.IsNullOrEmpty(root)) continue;
            foreach (var f in SafeFiles(root))
            {
                if (!IsShortcut(f)) continue;
                var c = Match(ShortcutKey(f), keys);
                if (c != null) addFile(f, c.Value);
            }
        }

        foreach (var root in startMenus)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;

            foreach (var f in SafeFiles(root))
            {
                if (!IsShortcut(f)) continue;
                var c = Match(ShortcutKey(f), keys);
                if (c != null) addFile(f, c.Value);
            }

            foreach (var dir in SafeDirs(root))
            {
                var nk = Fmt.Norm(Path.GetFileName(dir));
                if (!Safety.IsBlockedName(nk))
                {
                    var c = Match(nk, keys);
                    if (c != null) { addFolder(dir, c.Value); continue; }
                }
                foreach (var f in SafeFiles(dir))
                {
                    if (!IsShortcut(f)) continue;
                    var c = Match(ShortcutKey(f), keys);
                    if (c != null) addFile(f, c.Value);
                }
            }
        }
    }

    // ======================= Registry =======================

    private static void ScanRegistryRoot(string rootPath, List<string> keys, string pubKey, Action<string, Confidence> add)
    {
        try
        {
            using var root = RegistryHelper.OpenKey(rootPath);
            if (root == null) return;

            foreach (var name in root.GetSubKeyNames())
            {
                var nk = Fmt.Norm(name);
                if (Safety.SystemVendors.Contains(nk)) continue;

                if (!Safety.IsBlockedName(nk))
                {
                    var c = Match(nk, keys);
                    if (c != null) { add(rootPath + "\\" + name, c.Value); continue; }
                }

                if (pubKey.Length >= 3 && nk == pubKey)
                {
                    try
                    {
                        using var vendor = root.OpenSubKey(name);
                        if (vendor == null) continue;
                        foreach (var sub in vendor.GetSubKeyNames())
                        {
                            var sk = Fmt.Norm(sub);
                            if (Safety.IsBlockedName(sk)) continue;
                            var c = Match(sk, keys);
                            if (c != null) add(rootPath + "\\" + name + "\\" + sub, c.Value);
                        }
                    }
                    catch { }
                }
            }
        }
        catch (Exception ex) { Logger.Error("Registry scan " + rootPath, ex); }
    }

    private static void ScanAppPaths(string loc, Action<string> add)
    {
        foreach (var root in new[]
        {
            RegistryHelper.HKLM + @"\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
            RegistryHelper.HKLM + @"\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths",
            RegistryHelper.HKCU + @"\SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths",
        })
        {
            try
            {
                using var k = RegistryHelper.OpenKey(root);
                if (k == null) continue;
                foreach (var sub in k.GetSubKeyNames())
                {
                    using var s = k.OpenSubKey(sub);
                    if (s == null) continue;
                    var def = s.GetValue("") as string ?? "";
                    var path = s.GetValue("Path") as string ?? "";
                    if (def.Contains(loc, StringComparison.OrdinalIgnoreCase) || path.Contains(loc, StringComparison.OrdinalIgnoreCase))
                        add(root + "\\" + sub);
                }
            }
            catch { }
        }
    }

    private static void ScanRunKeys(string loc, Action<string, string> add)
    {
        foreach (var key in new[]
        {
            RegistryHelper.HKCU + @"\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            RegistryHelper.HKLM + @"\SOFTWARE\Microsoft\Windows\CurrentVersion\Run",
            RegistryHelper.HKLM + @"\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run",
        })
        {
            try
            {
                using var k = RegistryHelper.OpenKey(key);
                if (k == null) continue;
                foreach (var v in k.GetValueNames())
                {
                    var data = k.GetValue(v) as string ?? "";
                    if (data.Contains(loc, StringComparison.OrdinalIgnoreCase)) add(key, v);
                }
            }
            catch { }
        }
    }

    private static void ScanServices(string loc, Action<string> add)
    {
        try
        {
            using var services = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services");
            if (services == null) return;
            foreach (var name in services.GetSubKeyNames())
            {
                using var s = services.OpenSubKey(name);
                var image = s?.GetValue("ImagePath") as string;
                if (image != null && Environment.ExpandEnvironmentVariables(image).Contains(loc, StringComparison.OrdinalIgnoreCase))
                    add(name);
            }
        }
        catch (Exception ex) { Logger.Error("Service scan", ex); }
    }

    // ======================= Scheduled tasks =======================

    private static void ScanTasks(List<string> keys, string? loc, Action<string, Confidence> add)
    {
        try
        {
            var tasksDir = Path.Combine(Environment.SystemDirectory, "Tasks");
            if (!Directory.Exists(tasksDir)) return;

            var opt = new EnumerationOptions { RecurseSubdirectories = true, IgnoreInaccessible = true, MaxRecursionDepth = 3 };
            foreach (var file in Directory.EnumerateFiles(tasksDir, "*", opt))
            {
                var rel = Path.GetRelativePath(tasksDir, file);
                if (rel.StartsWith("Microsoft\\", StringComparison.OrdinalIgnoreCase)) continue;

                Confidence? c = Match(Fmt.Norm(Path.GetFileName(file)), keys);

                if (loc != null && loc.Length >= 8)
                {
                    try
                    {
                        var fi = new FileInfo(file);
                        if (fi.Length < 262_144 && File.ReadAllText(file).Contains(loc, StringComparison.OrdinalIgnoreCase))
                            c = Confidence.High;
                    }
                    catch { }
                }

                if (c != null) add("\\" + rel, c.Value);
            }
        }
        catch (Exception ex) { Logger.Error("Task scan", ex); }
    }
}
