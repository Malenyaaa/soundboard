using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Config;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>Order of the pads (drag, Ctrl+arrow) and the undo stack for changes to the pads.</summary>
public sealed partial class MainViewModel
{
    private const int UndoLimit = 50;

    /// <summary>Steps that can be taken back, newest last (Ctrl+Z).</summary>
    private readonly List<(Func<string> Text, Action Undo)> _undo = [];

    /// <summary>
    /// Remembers a step for Ctrl+Z. No notice: the result (pad gone, moved, quieter) is seen on the
    /// pads themselves, and a banner after every step was in the way.
    /// </summary>
    private void PushUndo(Func<string> text, Action undo)
    {
        _undo.Add((text, undo));
        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
    }

    /// <summary>Takes back the newest step (Ctrl+Z); with nothing left it does nothing.</summary>
    [RelayCommand]
    private void UndoLast()
    {
        if (_undo.Count == 0) return;
        var (_, undo) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        undo();
    }

    /// <summary>Moves a pad to <paramref name="index"/> (in the order without it) and remembers the step.</summary>
    public void MoveTile(TileViewModel tile, int index)
    {
        int from = Tiles.IndexOf(tile);
        if (from < 0) return;
        index = ClampToGroup(tile, Math.Clamp(index, 0, Tiles.Count - 1)); // favourites stay in front
        if (index == from) return;
        MoveTileRaw(tile, index);
        // Found again by its settings: removing and restoring or an import makes new pad objects.
        var config = tile.Config;
        var name = tile.Name;
        PushUndo(() => L.F("Notice.PadMoved", name), () =>
        {
            if (Tiles.FirstOrDefault(t => ReferenceEquals(t.Config, config)) is { } now) MoveTileRaw(now, from);
        });
    }

    /// <summary>Drop after dragging: the pad goes in front of <paramref name="before"/> (null = at the end).</summary>
    public void MoveTileBefore(TileViewModel tile, TileViewModel? before)
    {
        int from = Tiles.IndexOf(tile);
        if (from < 0 || ReferenceEquals(tile, before)) return;
        int insertBefore = before is null ? Tiles.Count : Tiles.IndexOf(before);
        if (insertBefore < 0) return;
        MoveTile(tile, PadOrder.TargetIndex(from, insertBefore));
    }

    private void MoveTileRaw(TileViewModel tile, int index)
    {
        int from = Tiles.IndexOf(tile);
        Tiles.Move(from, Math.Clamp(index, 0, Tiles.Count - 1));
        // The profile follows the order on screen.
        int cfgFrom = _profile.Tiles.IndexOf(tile.Config);
        if (cfgFrom >= 0) PadOrder.Move(_profile.Tiles, cfgFrom, Tiles.IndexOf(tile));
        RecolorAutomatic();
        ScheduleSave();
    }
}
