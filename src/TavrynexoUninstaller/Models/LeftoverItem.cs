using Tavrynexo.Core;

namespace Tavrynexo.Models;

public enum LeftoverKind { File, Folder, RegKey, RegValue, Service, Task }
public enum Confidence { High, Medium }

public sealed class LeftoverItem : Bindable
{
    public LeftoverKind Kind { get; set; }

    /// <summary>File/folder path, full registry key path, service name or task path.</summary>
    public string Path { get; set; } = "";

    /// <summary>Only for RegValue.</summary>
    public string? ValueName { get; set; }

    public Confidence Confidence { get; set; }
    public long Size { get; set; }

    /// <summary>Normalized publisher name of the owning program (used to prune empty vendor folders after deletion).</summary>
    public string PublisherKey { get; set; } = "";

    private bool _selected;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }

    public string KindText => L.Get("Kind" + Kind);
    public string ConfidenceText => L.Get(Confidence == Confidence.High ? "ConfHigh" : "ConfMedium");
    public bool IsHigh => Confidence == Confidence.High;
    public string SizeText => Size > 0 ? Fmt.Size(Size) : "";
    public string DisplayPath => Kind == LeftoverKind.RegValue ? $"{Path}  →  {ValueName}" : Path;
}
