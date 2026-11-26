using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media;
using PurinPointer.Core;

namespace PurinPointer;

/// <summary>One row of the pointer list and the edit waiting to be applied to it.</summary>
public sealed class RoleItem(CursorRole role) : INotifyPropertyChanged
{
    public CursorRole Role { get; } = role;

    /// <summary>The cursor being edited for this role (kept after applying, so it can be tweaked again).</summary>
    public CursorProject? Project { get; set; }

    /// <summary>The user chose "Use Windows default" and has not applied it yet.</summary>
    public bool PendingDefault { get; set; }

    /// <summary>There is something to write on the next Apply.</summary>
    public bool IsDirty { get => _dirty; set { _dirty = value; Refresh(); } }
    bool _dirty;

    public bool IsCustom { get => _custom; set { _custom = value; Refresh(); } }
    bool _custom;

    public ImageSource? Thumbnail { get => _thumb; set { _thumb = value; OnChanged(); } }
    ImageSource? _thumb;

    public string Name => Loc.Instance[Role.NameKey];

    public string Status => IsDirty ? Loc.Instance["status.pending"]
                          : IsCustom ? Loc.Instance["status.custom"]
                          : Loc.Instance["status.default"];

    public bool Highlight => IsDirty || IsCustom;

    public void Refresh()
    {
        OnChanged(nameof(Name));
        OnChanged(nameof(Status));
        OnChanged(nameof(Highlight));
        OnChanged(nameof(IsDirty));
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    void OnChanged([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}
