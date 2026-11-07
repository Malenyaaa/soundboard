using Soundboard.Core.Config;

namespace Soundboard.App.ViewModels;

/// <summary>
/// Favourites (right-click → "Favourite"): the first group of pads. A pad that stops being a favourite goes
/// back to its own category, if it has one, else to "Other pads".
/// </summary>
public sealed partial class MainViewModel
{
    public void SetFavorite(TileViewModel tile, bool favorite)
    {
        if (tile.IsFavorite == favorite) return;
        // A new favourite goes to the end of the favourites; a former one to the front of the group it returns to.
        var back = tile.Config.Category is { } id && _profile.Categories.Any(c => c.Id == id) ? GroupKey.Category(id) : GroupKey.Others;
        var target = favorite ? GroupKey.Favorites : back;
        var first = favorite ? null : Tiles.FirstOrDefault(t => !ReferenceEquals(t, tile) && GroupOf(t) == target);
        MoveTileTo(tile, target, first, TF(favorite ? "Notice.Favorited" : "Notice.Unfavorited", tile.Name));
    }
}
