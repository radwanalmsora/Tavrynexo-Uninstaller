using System.Windows.Media;
using Microsoft.Win32;
using Tavrynexo.Core;

namespace Tavrynexo.Models;

public sealed class InstalledProgram : Bindable
{
    public string Name { get; set; } = "";
    public string Publisher { get; set; } = "";
    public string Version { get; set; } = "";
    public string InstallLocation { get; set; } = "";
    public string UninstallString { get; set; } = "";
    public string QuietUninstallString { get; set; } = "";
    public string DisplayIcon { get; set; } = "";
    public DateTime? InstallDate { get; set; }

    // Registry origin (needed to verify removal and to delete the uninstall entry on force remove)
    public RegistryHive Hive { get; set; }
    public RegistryView View { get; set; }
    public string SubKeyPath { get; set; } = "";
    public string RegistryFullPath64 =>
        string.IsNullOrEmpty(SubKeyPath) ? "" : RegistryHelper.To64ViewPath(Hive, View, SubKeyPath);

    // Microsoft Store apps
    public bool IsStore { get; set; }
    public string PackageFullName { get; set; } = "";

    private long _sizeBytes;
    public long SizeBytes { get => _sizeBytes; set { if (Set(ref _sizeBytes, value)) Raise(nameof(SizeText)); } }

    private ImageSource? _icon;
    public ImageSource? Icon { get => _icon; set => Set(ref _icon, value); }

    public string SizeText => Fmt.Size(SizeBytes);
    public string DateText => InstallDate?.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture) ?? "";
    public string Initial => string.IsNullOrWhiteSpace(Name) ? "?" : Name.Trim()[..1].ToUpperInvariant();
    public bool HasUninstaller => !string.IsNullOrWhiteSpace(UninstallString) || !string.IsNullOrWhiteSpace(QuietUninstallString);
}
