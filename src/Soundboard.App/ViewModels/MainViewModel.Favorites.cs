using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>
/// Favourites (right-click → "Favourite"): pinned to the front in a row of their own. The order on
/// screen and in the profile always has all favourites first; moving a pad stays inside its group.
/// </summary>
public sealed partial class MainViewModel
{
    /// <summary>The favourite group changed (the pad panel breaks the row after the last favourite).</summary>
    public event Action? FavoritesChanged;

    public void SetFavorite(TileViewModel tile, bool favorite) => SetFavorite(tile, favorite, remember: true);

    private void SetFavorite(TileViewModel tile, bool favorite, bool remember)
    {
        if (tile.IsFavorite == favorite) return;
        int oldIndex = Tiles.IndexOf(tile);
        tile.Config.Favorite = favorite;
        tile.IsFavorite = favorite;
        // A new favourite goes to the end of the favourites; a former one to the front of the rest.
        int target = Tiles.Count(t => t.IsFavorite && !ReferenceEquals(t, tile));
        if (Tiles.IndexOf(tile) != target) MoveTileRaw(tile, target);
        else ScheduleSave();
        FavoritesChanged?.Invoke();
        if (!remember) return;
        var config = tile.Config;
        PushUndo(TF(favorite ? "Notice.Favorited" : "Notice.Unfavorited", tile.Name), () =>
        {
            if (Tiles.FirstOrDefault(t => ReferenceEquals(t.Config, config)) is not { } now) return;
            SetFavorite(now, !favorite, remember: false);
            if (Tiles.IndexOf(now) != oldIndex) MoveTileRaw(now, Math.Clamp(oldIndex, 0, Tiles.Count - 1));
            KeepFavoritesFirst();
        });
    }

    /// <summary>Allowed place for <paramref name="tile"/>: favourites stay in front, the others behind them.</summary>
    private int ClampToGroup(TileViewModel tile, int index)
    {
        int favorites = Tiles.Count(t => t.IsFavorite && !ReferenceEquals(t, tile));
        return tile.IsFavorite ? Math.Min(index, favorites) : Math.Max(index, favorites);
    }

    /// <summary>Puts all favourites in front, keeping the order within each group (after load, import, restore).</summary>
    private void KeepFavoritesFirst()
    {
        int slot = 0;
        bool moved = false;
        for (int i = 0; i < Tiles.Count; i++)
        {
            if (!Tiles[i].IsFavorite) continue;
            if (i != slot)
            {
                MoveTileRaw(Tiles[i], slot);
                moved = true;
            }
            slot++;
        }
        if (moved) FavoritesChanged?.Invoke();
    }
}
