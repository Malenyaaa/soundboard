using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Config;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>
/// The head of a group of pads: the favourites, an own category, or the pads in none ("Other pads").
/// The arrow collapses it; a click on the name renames it (not "Other pads").
/// </summary>
public sealed partial class CategoryViewModel : ObservableObject, Views.IInlineRenamable
{
    private readonly MainViewModel _owner;

    public CategoryViewModel(MainViewModel owner, GroupKey group, CategoryConfig? config)
    {
        _owner = owner;
        Group = group;
        Config = config;
        _name = DisplayName();
        _isCollapsed = owner.IsGroupCollapsed(group);
    }

    public GroupKey Group { get; }

    /// <summary>The stored category; null for the favourites and "Other pads".</summary>
    public CategoryConfig? Config { get; }

    public bool IsOwn => Group.Kind == GroupKind.Category;

    /// <summary>Only "Other pads" keeps its name.</summary>
    public bool CanRename => Group.Kind != GroupKind.Others;

    /// <summary>Own categories can be deleted (their pads go to "Other pads") and moved up and down.</summary>
    public bool CanDelete => IsOwn;

    [ObservableProperty]
    private string _name;

    [ObservableProperty]
    private bool _isRenaming;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ToggleName), nameof(CountText))]
    private bool _isCollapsed;

    /// <summary>Pads in the group (all, also those the search hides).</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CountText), nameof(IsEmpty))]
    private int _count;

    public bool IsEmpty => Count == 0;

    /// <summary>Hidden: the favourites while there are none; any head while a search finds nothing in its group.</summary>
    [ObservableProperty]
    private bool _isHidden;

    /// <summary>A pad is dragged over this head: it would go into this group.</summary>
    [ObservableProperty]
    private bool _isDropTarget;

    /// <summary>After the name: the number of pads while collapsed, a hint while empty; else nothing.</summary>
    public string CountText =>
        IsCollapsed ? L.F("Category.CountCollapsed", Count)
        : Count == 0 && IsOwn ? L.Get("Category.EmptyHint")
        : "";

    /// <summary>Name of the arrow for screen readers: what a click does.</summary>
    public string ToggleName => L.F(IsCollapsed ? "Category.Expand" : "Category.Collapse", Name);

    public bool CanMoveUp => IsOwn && _owner.CategoryIndex(this) > 0;

    public bool CanMoveDown => IsOwn && _owner.CategoryIndex(this) < _owner.OwnCategoryCount - 1;

    private string DisplayName() => Group.Kind switch
    {
        GroupKind.Favorites => _owner.FavoritesName ?? L.Get("Pads.Favorites"),
        GroupKind.Category => Config!.Name,
        _ => L.Get("Pads.Others"),
    };

    /// <summary>The language changed, or the stored name.</summary>
    public void RefreshTexts()
    {
        // Straight to the field: a name from the profile or the language is no rename by the user.
#pragma warning disable MVVMTK0034
        if (!IsRenaming) _name = DisplayName();
#pragma warning restore MVVMTK0034
        OnPropertyChanged(nameof(Name));
        OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(ToggleName));
        OnPropertyChanged(nameof(CanMoveUp));
        OnPropertyChanged(nameof(CanMoveDown));
    }

    /// <summary>A new name typed in the head (the rename box sets it; empty keeps the old one).</summary>
    partial void OnNameChanged(string value)
    {
        if (value != DisplayName()) _owner.RenameGroup(this, value);
        OnPropertyChanged(nameof(ToggleName));
    }

    partial void OnIsCollapsedChanged(bool value) => _owner.SetGroupCollapsed(this, value);

    [RelayCommand]
    private void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    /// <summary>Click on the name: renames in place; "Other pads" has no own name, there the click collapses.</summary>
    [RelayCommand]
    private void NameClick()
    {
        if (CanRename) IsRenaming = true;
        else IsCollapsed = !IsCollapsed;
    }

    [RelayCommand]
    private void Rename()
    {
        if (CanRename) IsRenaming = true;
    }

    [RelayCommand]
    private void Delete() => _owner.DeleteGroup(this);

    [RelayCommand]
    private void MoveUp() => _owner.MoveCategory(this, -1);

    [RelayCommand]
    private void MoveDown() => _owner.MoveCategory(this, +1);

    [RelayCommand]
    private void NewCategory() => _owner.AddCategory(null);
}
