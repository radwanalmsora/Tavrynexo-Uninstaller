namespace Tavrynexo.Models;

public enum StartupSource { RunCurrentUser, RunLocalMachine, RunLocalMachine32, FolderUser, FolderCommon }

public sealed class StartupEntry : Bindable
{
    public string Name { get; set; } = "";
    public string Command { get; set; } = "";
    public StartupSource Source { get; set; }

    /// <summary>For registry entries: the value name. For folder entries: the file name.</summary>
    public string ItemName { get; set; } = "";

    /// <summary>For folder entries: full file path.</summary>
    public string FilePath { get; set; } = "";

    private bool _enabled = true;
    public bool Enabled { get => _enabled; set => Set(ref _enabled, value); }

    public string SourceText => Source switch
    {
        StartupSource.RunCurrentUser => "HKCU\\Run",
        StartupSource.RunLocalMachine => "HKLM\\Run",
        StartupSource.RunLocalMachine32 => "HKLM\\Run (32-bit)",
        StartupSource.FolderUser => "Startup folder (user)",
        StartupSource.FolderCommon => "Startup folder (all users)",
        _ => ""
    };
}
