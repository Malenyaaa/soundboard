using CommunityToolkit.Mvvm.Input;
using Soundboard.Core.Config;
using Soundboard.Core.Text;

namespace Soundboard.App.ViewModels;

/// <summary>Order of the pads (drag, Ctrl+arrow) and the undo stack for changes to the pads.</summary>
public sealed partial class MainViewModel
{
    private const int UndoLimit = 50;

    /// <summary>Steps that can be taken back, newest last. The notice always offers the newest.</summary>
    private readonly List<(Func<string> Text, Action Undo)> _undo = [];

    /// <summary>Remembers a step; the notice names it and offers "Undo" (also Ctrl+Z).</summary>
    private void PushUndo(Func<string> text, Action undo)
    {
        _undo.Add((text, undo));
        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
        ShowUndoNotice();
    }

    private void ShowUndoNotice()
    {
        if (_undo.Count == 0)
        {
            ClearNotice("undo");
            return;
        }
        var text = _undo[^1].Text;
        int more = _undo.Count - 1;
        ShowNotice("undo", more == 0 ? text : () => L.Count("Notice.UndoMore", more, text()), T("Action.Undo"), UndoLast);
    }

    /// <summary>Takes back the newest step; the notice then offers the one before, if any.</summary>
    [RelayCommand]
    private void UndoLast()
    {
        if (_undo.Count == 0)
        {
            ShowNotice("undo", T("Notice.NothingToUndo"));
            HideNoticeAfter("undo", TimeSpan.FromSeconds(3));
            return;
        }
        var (_, undo) = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        undo();
        ShowUndoNotice();
    }

    /// <summary>Moves a pad to <paramref name="index"/> (in the order without it) and remembers the step.</summary>
    public void MoveTile(TileViewModel tile, int index)
    {
        int from = Tiles.IndexOf(tile);
        if (from < 0) return;
        index = Math.Clamp(index, 0, Tiles.Count - 1);
        if (index == from) return;
        MoveTileRaw(tile, index);
        PushUndo(() => L.F("Notice.PadMoved", tile.Name), () =>
        {
            if (Tiles.Contains(tile)) MoveTileRaw(tile, from);
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
