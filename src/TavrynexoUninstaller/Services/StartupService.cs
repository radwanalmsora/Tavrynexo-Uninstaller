using System.IO;
using Microsoft.Win32;
using Tavrynexo.Core;
using Tavrynexo.Models;

namespace Tavrynexo.Services;

public static class StartupService
{
    private const string RunPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedBase = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\";

    public static List<StartupEntry> Scan()
    {
        var list = new List<StartupEntry>();

        ReadRun(list, RegistryHive.CurrentUser, RegistryView.Registry64, StartupSource.RunCurrentUser);
        ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry64, StartupSource.RunLocalMachine);
        ReadRun(list, RegistryHive.LocalMachine, RegistryView.Registry32, StartupSource.RunLocalMachine32);

        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.Startup), StartupSource.FolderUser);
        ReadFolder(list, Environment.GetFolderPath(Environment.SpecialFolder.CommonStartup), StartupSource.FolderCommon);

        return list.OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }

    private static void ReadRun(List<StartupEntry> list, RegistryHive hive, RegistryView view, StartupSource src)
    {
        try
        {
            using var b = RegistryKey.OpenBaseKey(hive, view);
            using var k = b.OpenSubKey(RunPath);
            if (k == null) return;
            foreach (var name in k.GetValueNames())
            {
                var cmd = k.GetValue(name) as string ?? "";
                list.Add(new StartupEntry
                {
                    Name = string.IsNullOrEmpty(name) ? "(Default)" : name,
                    ItemName = name,
                    Command = cmd,
                    Source = src,
                    Enabled = IsEnabled(src, name),
                });
            }
        }
        catch (Exception ex) { Logger.Error("Startup read " + src, ex); }
    }

    private static void ReadFolder(List<StartupEntry> list, string folder, StartupSource src)
    {
        try
        {
            if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return;
            foreach (var f in Directory.EnumerateFiles(folder))
            {
                var file = Path.GetFileName(f);
                if (file.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase)) continue;
                list.Add(new StartupEntry
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    ItemName = file,
                    FilePath = f,
                    Command = f,
                    Source = src,
                    Enabled = IsEnabled(src, file),
                });
            }
        }
        catch (Exception ex) { Logger.Error("Startup folder " + src, ex); }
    }

    private static (RegistryHive hive, string sub) Approved(StartupSource s) => s switch
    {
        StartupSource.RunCurrentUser => (RegistryHive.CurrentUser, ApprovedBase + "Run"),
        StartupSource.RunLocalMachine => (RegistryHive.LocalMachine, ApprovedBase + "Run"),
        StartupSource.RunLocalMachine32 => (RegistryHive.LocalMachine, ApprovedBase + "Run32"),
        StartupSource.FolderUser => (RegistryHive.CurrentUser, ApprovedBase + "StartupFolder"),
        _ => (RegistryHive.LocalMachine, ApprovedBase + "StartupFolder"),
    };

    private static bool IsEnabled(StartupSource src, string itemName)
    {
        try
        {
            var (hive, sub) = Approved(src);
            using var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
            using var k = b.OpenSubKey(sub);
            if (k?.GetValue(itemName) is byte[] data && data.Length > 0)
                return (data[0] & 1) == 0;       // 02/06 = enabled, 03 = disabled
        }
        catch { }
        return true;
    }

    public static void SetEnabled(StartupEntry e, bool enabled)
    {
        var (hive, sub) = Approved(e.Source);
        using var b = RegistryKey.OpenBaseKey(hive, RegistryView.Registry64);
        using var k = b.CreateSubKey(sub, true);
        var data = new byte[12];
        data[0] = enabled ? (byte)2 : (byte)3;
        if (!enabled) BitConverter.GetBytes(DateTime.UtcNow.ToFileTimeUtc()).CopyTo(data, 4);
        k.SetValue(e.ItemName, data, RegistryValueKind.Binary);
        e.Enabled = enabled;
        Logger.Info($"Startup '{e.Name}' => {(enabled ? "enabled" : "disabled")}");
    }

    public static void Delete(StartupEntry e)
    {
        switch (e.Source)
        {
            case StartupSource.FolderUser:
            case StartupSource.FolderCommon:
                if (File.Exists(e.FilePath)) File.Delete(e.FilePath);
                break;
            default:
            {
                var hive = e.Source == StartupSource.RunCurrentUser ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;
                var view = e.Source == StartupSource.RunLocalMachine32 ? RegistryView.Registry32 : RegistryView.Registry64;
                using var b = RegistryKey.OpenBaseKey(hive, view);
                using var k = b.OpenSubKey(RunPath, true);
                k?.DeleteValue(e.ItemName, false);
                break;
            }
        }

        try
        {
            var (ah, asub) = Approved(e.Source);
            using var ab = RegistryKey.OpenBaseKey(ah, RegistryView.Registry64);
            using var ak = ab.OpenSubKey(asub, true);
            ak?.DeleteValue(e.ItemName, false);
        }
        catch { }
        Logger.Info($"Startup entry deleted: {e.Name}");
    }
}
