namespace Soundboard.Core.Hotkeys;

/// <summary>Result of registering one global hotkey with Windows.</summary>
public enum HotkeyRegisterResult
{
    Ok,

    /// <summary>Another program has already registered this combination.</summary>
    TakenByOtherProgram,

    /// <summary>Windows refused for another reason.</summary>
    Failed,
}

/// <summary>Thin wrapper around RegisterHotKey/UnregisterHotKey, replaceable in tests.</summary>
public interface IHotkeyApi
{
    HotkeyRegisterResult Register(int id, HotkeyGesture gesture);

    void Unregister(int id);
}

/// <summary>State of one binding after the last apply.</summary>
public enum HotkeyState
{
    /// <summary>No key assigned.</summary>
    None,

    /// <summary>Registered with Windows and active.</summary>
    Active,

    /// <summary>Hotkeys are paused; nothing registered.</summary>
    Paused,

    /// <summary>The same key belongs to another tile (first one wins).</summary>
    Duplicate,

    /// <summary>Another program holds this key.</summary>
    TakenByOtherProgram,

    /// <summary>Windows refused.</summary>
    Failed,
}

/// <summary>One binding: an owner (tile ID or a fixed function) and its key.</summary>
public sealed record HotkeyBinding(string Owner, HotkeyGesture? Gesture, HotkeyState State, string? DuplicateOf);

/// <summary>
/// Keeps the global hotkeys of all tiles registered. The first binding of a key wins; later
/// bindings with the same key are reported as duplicates. Keys held by other programs are
/// reported, never forced.
/// </summary>
public sealed class HotkeyService : IDisposable
{
    private readonly IHotkeyApi _api;
    private readonly List<Entry> _entries = [];
    private readonly Dictionary<int, string> _registered = [];
    private int _nextId = 1;
    private bool _paused;

    public HotkeyService(IHotkeyApi api) => _api = api;

    /// <summary>Raised with the owner when a registered hotkey is pressed.</summary>
    public event EventHandler<string>? Triggered;

    public bool Paused
    {
        get => _paused;
        set
        {
            if (_paused == value) return;
            _paused = value;
            Apply();
        }
    }

    /// <summary>Current state of all bindings, in the order they were set.</summary>
    public IReadOnlyList<HotkeyBinding> Bindings => _entries.Select(e => new HotkeyBinding(e.Owner, e.Gesture, e.State, e.DuplicateOf)).ToList();

    public HotkeyBinding? Get(string owner)
    {
        var e = _entries.FirstOrDefault(x => x.Owner == owner);
        return e is null ? null : new HotkeyBinding(e.Owner, e.Gesture, e.State, e.DuplicateOf);
    }

    /// <summary>Owner currently using <paramref name="gesture"/> (first binding), except <paramref name="exceptOwner"/>.</summary>
    public string? FindOwner(HotkeyGesture gesture, string? exceptOwner = null) =>
        _entries.FirstOrDefault(e => e.Gesture == gesture && e.Owner != exceptOwner)?.Owner;

    /// <summary>Replaces all bindings at once (e.g. after loading the profile).</summary>
    public void SetAll(IEnumerable<(string Owner, HotkeyGesture? Gesture)> bindings)
    {
        _entries.Clear();
        foreach (var (owner, gesture) in bindings) _entries.Add(new Entry(owner, gesture));
        Apply();
    }

    /// <summary>Sets or clears (null) the key of one owner. Unknown owners are appended.</summary>
    public HotkeyBinding Set(string owner, HotkeyGesture? gesture)
    {
        var e = _entries.FirstOrDefault(x => x.Owner == owner);
        if (e is null)
        {
            e = new Entry(owner, gesture);
            _entries.Add(e);
        }
        else
        {
            e.Gesture = gesture;
        }
        Apply();
        return Get(owner)!;
    }

    public void Remove(string owner)
    {
        _entries.RemoveAll(x => x.Owner == owner);
        Apply();
    }

    /// <summary>Called by the message loop with the ID from WM_HOTKEY.</summary>
    public void HandleHotkey(int id)
    {
        if (_paused) return;
        if (_registered.TryGetValue(id, out var owner)) Triggered?.Invoke(this, owner);
    }

    /// <summary>Registers everything again from scratch (cheap; a few keys).</summary>
    public void Apply()
    {
        foreach (var id in _registered.Keys) _api.Unregister(id);
        _registered.Clear();

        var used = new Dictionary<HotkeyGesture, string>();
        foreach (var e in _entries)
        {
            e.DuplicateOf = null;
            if (e.Gesture is not { } g)
            {
                e.State = HotkeyState.None;
                continue;
            }
            if (used.TryGetValue(g, out var first))
            {
                e.State = HotkeyState.Duplicate;
                e.DuplicateOf = first;
                continue;
            }
            used[g] = e.Owner;
            if (_paused)
            {
                e.State = HotkeyState.Paused;
                continue;
            }
            int id = _nextId++;
            if (_nextId > 0xBFFF) _nextId = 1; // RegisterHotKey: application IDs 0x0000-0xBFFF
            var result = _api.Register(id, g);
            if (result == HotkeyRegisterResult.Ok)
            {
                _registered[id] = e.Owner;
                e.State = HotkeyState.Active;
            }
            else
            {
                e.State = result == HotkeyRegisterResult.TakenByOtherProgram ? HotkeyState.TakenByOtherProgram : HotkeyState.Failed;
            }
        }
    }

    public void Dispose()
    {
        foreach (var id in _registered.Keys) _api.Unregister(id);
        _registered.Clear();
    }

    private sealed class Entry(string owner, HotkeyGesture? gesture)
    {
        public string Owner { get; } = owner;
        public HotkeyGesture? Gesture { get; set; } = gesture;
        public HotkeyState State { get; set; }
        public string? DuplicateOf { get; set; }
    }
}
