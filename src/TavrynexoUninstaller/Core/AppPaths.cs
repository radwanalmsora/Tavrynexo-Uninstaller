using System.IO;

namespace Tavrynexo.Core;

public static class AppPaths
{
    public static string DataDir { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Tavrynexo");

    public static string BackupDir => Path.Combine(DataDir, "Backups");
    public static string LogDir => Path.Combine(DataDir, "Logs");
}
