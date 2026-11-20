namespace Tavrynexo.Models;

public sealed class JunkCategory : Bindable
{
    public string Key { get; set; } = "";
    public string Title { get; set; } = "";
    public List<string> Paths { get; set; } = new();
    public bool IsRecycleBin { get; set; }
    public int MinAgeMinutes { get; set; }

    private long _size;
    public long Size { get => _size; set { if (Set(ref _size, value)) Raise(nameof(SizeText)); } }
    public string SizeText => Size > 0 ? Core.Fmt.Size(Size) : "—";

    private bool _selected = true;
    public bool IsSelected { get => _selected; set => Set(ref _selected, value); }
}
