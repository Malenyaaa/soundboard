using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Config;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>Search above the pads, pad size, and showing newly added pads.</summary>
public sealed partial class MainViewModel
{
    /// <summary>Search text in the header; empty shows every pad.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SearchActive))]
    private string _searchText = "";

    public bool SearchActive => PadFilter.IsActive(SearchText);

    /// <summary>A search is typed, there are pads, but none matches.</summary>
    [ObservableProperty]
    private bool _searchHasNoResults;

    [ObservableProperty]
    private string _searchNoResultsText = "";

    partial void OnSearchTextChanged(string value)
    {
        // The pads are what is searched: the settings make room for them.
        if (PadFilter.IsActive(value) && SettingsOpen) CloseSettings();
        ApplySearch();
    }

    /// <summary>Hides the pads that do not match; called again when names, keys or the language change.</summary>
    private void ApplySearch()
    {
        foreach (var t in Tiles) t.IsFilteredOut = !PadFilter.Matches(t.Name, t.HotkeyText, SearchText);
        SearchHasNoResults = SearchActive && Tiles.Count > 0 && Tiles.All(t => t.IsFilteredOut);
        SearchNoResultsText = SearchHasNoResults ? L.F("Search.NoResults", SearchText.Trim()) : "";
        RefreshGroups(); // collapsed groups open for their matches, heads without a match go
    }

    [RelayCommand]
    private void ClearSearch() => SearchText = "";

    /// <summary>A name changed: the search looks again.</summary>
    public void RefreshSearch() => ApplySearch();

    /// <summary>"Large" or "Compact".</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CompactPads))]
    private string _padSize = PadSizes.Large;

    public bool CompactPads => PadSize == PadSizes.Compact;

    partial void OnPadSizeChanged(string value)
    {
        foreach (var t in Tiles) t.Compact = CompactPads;
        if (value is not (PadSizes.Large or PadSizes.Compact) || value == _profile.PadSize) return;
        _profile.PadSize = value;
        ScheduleSave();
    }

    /// <summary>New pads arrived (sounds added or dropped): the window scrolls to the first one.</summary>
    public event Action<TileViewModel>? PadsAdded;

    /// <summary>Lights the new pads up briefly and asks the window to bring them into view.</summary>
    private void ShowAdded(IReadOnlyList<TileViewModel> added)
    {
        if (added.Count == 0) return;
        // New pads must be seen: a search that would hide them is cleared, a collapsed group opens.
        if (SearchActive && added.Any(t => t.IsFilteredOut)) SearchText = "";
        foreach (var t in added.Where(t => t.IsCollapsedOut).ToList()) Head(GroupOf(t)).IsCollapsed = false;
        foreach (var t in added) t.IsJustAdded = true;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1600) };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            foreach (var t in added) t.IsJustAdded = false;
        };
        timer.Start();
        PadsAdded?.Invoke(added[0]);
    }
}
