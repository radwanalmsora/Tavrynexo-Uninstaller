using System.IO;

namespace Tavrynexo.Core;

/// <summary>
/// Central safety rules. Nothing that touches the disk or registry for deletion is allowed
/// to bypass these checks (they are enforced both when scanning and again when deleting).
/// </summary>
public static class Safety
{
    /// <summary>Generic or shared folder / key names that must never be matched or deleted as "leftovers".</summary>
    public static readonly HashSet<string> BlockedNames = new()
    {
        "microsoft","windows","windowsapps","windowsnt","common","commonfiles","internetexplorer","windowsdefender",
        "windowspowershell","windowsmail","windowsmediaplayer","windowsphotoviewer","windowssidebar","windowsportabledevices",
        "microsoftedge","microsoftedgecore","microsoftcorporation","modifiablewindowsapps","referenceassemblies","msbuild","dotnet",
        "google","intel","nvidia","nvidiacorporation","amd","apple","adobe","realtek","oracle","java","mozilla",
        "packages","programs","temp","tmp","system32","syswow64","users","default","public","all users","allusers",
        "systemvolumeinformation","recycle.bin","recyclebin","packagecache","installer","uninstall","uninstallinformation",
        "programdata","appdata","local","locallow","roaming","startmenu","desktop","documents","downloads","pictures","music","videos",
        "classes","policies","wow6432node","registeredapplications","clients","odbc","drivers","services","tavrynexo",
    };

    /// <summary>Vendors we never descend into when looking for "Vendor\Product" structures.</summary>
    public static readonly HashSet<string> SystemVendors = new()
    {
        "microsoft","windows","windowsnt","classes","policies","wow6432node","clients","registeredapplications","odbc","windowsapps"
    };

    private static readonly HashSet<string> ProtectedExact = BuildExact();
    private static readonly List<string> ProtectedTrees = BuildTrees();

    public static bool IsBlockedName(string normalizedName) =>
        string.IsNullOrEmpty(normalizedName) || BlockedNames.Contains(normalizedName);

    public static bool IsProtectedPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return true;
        string full;
        try { full = Path.GetFullPath(path).TrimEnd('\\'); } catch { return true; }

        if (full.Length <= 2) return true;                                  // "C:"
        var root = (Path.GetPathRoot(full) ?? "").TrimEnd('\\');
        if (string.Equals(full, root, StringComparison.OrdinalIgnoreCase)) return true;

        if (ProtectedExact.Contains(full)) return true;

        foreach (var t in ProtectedTrees)
            if (full.Equals(t, StringComparison.OrdinalIgnoreCase) ||
                full.StartsWith(t + "\\", StringComparison.OrdinalIgnoreCase))
                return true;

        // Never delete our own folder or any parent of it.
        var self = AppContext.BaseDirectory.TrimEnd('\\');
        if (self.Equals(full, StringComparison.OrdinalIgnoreCase) ||
            self.StartsWith(full + "\\", StringComparison.OrdinalIgnoreCase))
            return true;

        if (IsBlockedName(Fmt.Norm(Path.GetFileName(full)))) return true;
        return false;
    }

    public static bool IsProtectedRegistryPath(string? fullPath)
    {
        if (string.IsNullOrWhiteSpace(fullPath)) return true;
        var parts = fullPath.Trim('\\').Split('\\', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3) return true;
        if (!parts[1].Equals("SOFTWARE", StringComparison.OrdinalIgnoreCase)) return true;
        if (parts[2].Equals("WOW6432Node", StringComparison.OrdinalIgnoreCase) && parts.Length < 4) return true;

        // A concrete product key under ...\CurrentVersion\Uninstall\<product> may be removed (force remove).
        if (parts.Length >= 6 &&
            fullPath.Contains(@"\CurrentVersion\Uninstall\", StringComparison.OrdinalIgnoreCase))
            return false;

        var last = Fmt.Norm(parts[^1]);
        if (IsBlockedName(last)) return true;
        if (last is "apppaths" or "run" or "currentversion" or "runonce") return true;
        return false;
    }

    private static HashSet<string> BuildExact()
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try { set.Add(Path.GetFullPath(p).TrimEnd('\\')); } catch { }
        }

        foreach (var f in new[]
        {
            Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86,
            Environment.SpecialFolder.CommonProgramFiles, Environment.SpecialFolder.CommonProgramFilesX86,
            Environment.SpecialFolder.Windows, Environment.SpecialFolder.System, Environment.SpecialFolder.SystemX86,
            Environment.SpecialFolder.UserProfile, Environment.SpecialFolder.LocalApplicationData,
            Environment.SpecialFolder.ApplicationData, Environment.SpecialFolder.CommonApplicationData,
            Environment.SpecialFolder.Desktop, Environment.SpecialFolder.DesktopDirectory, Environment.SpecialFolder.MyDocuments,
            Environment.SpecialFolder.MyPictures, Environment.SpecialFolder.MyMusic, Environment.SpecialFolder.MyVideos,
            Environment.SpecialFolder.StartMenu, Environment.SpecialFolder.Programs,
            Environment.SpecialFolder.CommonStartMenu, Environment.SpecialFolder.CommonPrograms,
            Environment.SpecialFolder.Startup, Environment.SpecialFolder.CommonStartup,
        })
            Add(Environment.GetFolderPath(f));

        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var sysDrive = Path.GetPathRoot(Environment.GetFolderPath(Environment.SpecialFolder.Windows)) ?? @"C:\";

        Add(Path.Combine(profile, "Downloads"));
        Add(Path.Combine(profile, "OneDrive"));
        Add(Path.Combine(sysDrive, "Users"));
        Add(Path.Combine(local, "Programs"));
        Add(Path.Combine(local, "Temp"));
        Add(Path.Combine(local, "Packages"));
        Add(Path.Combine(local, "Microsoft"));
        Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Microsoft"));
        return set;
    }

    private static List<string> BuildTrees()
    {
        var list = new List<string>();
        void Add(string? p)
        {
            if (string.IsNullOrWhiteSpace(p)) return;
            try { list.Add(Path.GetFullPath(p).TrimEnd('\\')); } catch { }
        }

        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var pf = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        var pf86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        var pd = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        var local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var roam = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var sysDrive = Path.GetPathRoot(win) ?? @"C:\";

        Add(win);
        foreach (var root in new[] { pf, pf86 })
        {
            Add(Path.Combine(root, "WindowsApps"));
            Add(Path.Combine(root, "Common Files"));
            Add(Path.Combine(root, "Windows Defender"));
            Add(Path.Combine(root, "Windows NT"));
            Add(Path.Combine(root, "Windows Mail"));
            Add(Path.Combine(root, "Windows Media Player"));
            Add(Path.Combine(root, "Windows Photo Viewer"));
            Add(Path.Combine(root, "Internet Explorer"));
            Add(Path.Combine(root, "Reference Assemblies"));
            Add(Path.Combine(root, "dotnet"));
            Add(Path.Combine(root, "Microsoft"));
        }
        Add(Path.Combine(pd, "Microsoft"));
        Add(Path.Combine(pd, "Package Cache"));
        Add(Path.Combine(local, "Microsoft"));
        Add(Path.Combine(local, "Packages"));
        Add(Path.Combine(roam, "Microsoft"));
        Add(Path.Combine(sysDrive, "System Volume Information"));
        Add(Path.Combine(sysDrive, "$Recycle.Bin"));
        Add(Path.Combine(sysDrive, "Recovery"));
        return list;
    }
}
