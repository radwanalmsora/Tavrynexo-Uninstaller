using System.IO;
using Microsoft.Win32;

namespace Tavrynexo.Core;

/// <summary>All registry paths are full paths in the 64-bit view, e.g. HKEY_LOCAL_MACHINE\SOFTWARE\WOW6432Node\Vendor.</summary>
public static class RegistryHelper
{
    public const string HKLM = "HKEY_LOCAL_MACHINE";
    public const string HKCU = "HKEY_CURRENT_USER";

    public static RegistryKey? OpenBase(string fullPath, out string sub)
    {
        sub = "";
        var idx = fullPath.IndexOf('\\');
        var hiveName = idx < 0 ? fullPath : fullPath[..idx];
        sub = idx < 0 ? "" : fullPath[(idx + 1)..];
        return hiveName.ToUpperInvariant() switch
        {
            HKLM => RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64),
            HKCU => RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Registry64),
            _ => null
        };
    }

    public static RegistryKey? OpenKey(string fullPath, bool writable = false)
    {
        using var b = OpenBase(fullPath, out var sub);
        return b?.OpenSubKey(sub, writable);
    }

    public static bool KeyExists(string fullPath)
    {
        try { using var k = OpenKey(fullPath); return k != null; } catch { return false; }
    }

    public static bool DeleteKeyTree(string fullPath)
    {
        using var b = OpenBase(fullPath, out var sub);
        if (b == null || string.IsNullOrEmpty(sub)) return false;
        b.DeleteSubKeyTree(sub, false);
        return !KeyExists(fullPath);
    }

    public static void DeleteValue(string fullPath, string valueName)
    {
        using var k = OpenKey(fullPath, true);
        k?.DeleteValue(valueName, false);
    }

    public static bool IsEmptyKey(string fullPath)
    {
        try
        {
            using var k = OpenKey(fullPath);
            return k != null && k.SubKeyCount == 0 && k.ValueCount == 0;
        }
        catch { return false; }
    }

    public static bool Export(string fullPath, string file)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            var (code, _, _) = ProcessRunner.Run("reg.exe", $"export \"{fullPath}\" \"{file}\" /y", 60_000);
            return code == 0;
        }
        catch { return false; }
    }

    /// <summary>Converts a path seen through the 32-bit view into its real 64-bit-view path.</summary>
    public static string To64ViewPath(RegistryHive hive, RegistryView view, string subPath)
    {
        var prefix = hive == RegistryHive.LocalMachine ? HKLM : HKCU;
        if (hive == RegistryHive.LocalMachine && view == RegistryView.Registry32 &&
            subPath.StartsWith(@"SOFTWARE\", StringComparison.OrdinalIgnoreCase) &&
            Environment.Is64BitOperatingSystem)
            subPath = @"SOFTWARE\WOW6432Node\" + subPath[9..];
        return prefix + "\\" + subPath;
    }
}
