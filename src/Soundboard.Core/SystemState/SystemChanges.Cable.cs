using Soundboard.Core.Audio;
using Soundboard.Core.Text;

namespace Soundboard.Core.SystemState;

/// <summary>What happened at one end of the cable.</summary>
public sealed record CableFormatStep(DeviceFlow Flow, string? Name, ApplyOutcome Outcome, int? Rate, string? Error);

public sealed class CableFormatResult
{
    public required IReadOnlyList<CableFormatStep> Steps { get; init; }

    public bool Changed => Steps.Any(s => s.Outcome == ApplyOutcome.Changed);

    /// <summary>Both ends run at the wanted rate.</summary>
    public bool AllAtRate(int rate) => Steps.Count == 2 && Steps.All(s => s.Rate == rate);
}

public sealed partial class SystemChanges
{
    /// <summary>The rate Discord, most microphones and the Windows default playback use: no resampling on the way.</summary>
    public const int CableRate = 48000;

    public static string CableFormatKey(string deviceId) => "cable-format:" + deviceId;

    /// <summary>
    /// Sets both ends of the virtual cable ("CABLE Input" for playback, "CABLE Output" for
    /// recording) to 48 kHz, keeping channels and bit depth. Only the cable: each end is checked
    /// by its name and direction, as Windows reports them right now, before anything is written.
    /// The format stays until removal (install scope): the cable belongs to the soundboard, and
    /// switching it at every start and exit would cut Discord's input twice per session.
    /// </summary>
    public CableFormatResult EnsureCableFormat(CableInfo cable)
    {
        return new CableFormatResult
        {
            Steps =
            [
                EnsureCableEnd(cable.Input, DeviceFlow.Render),
                EnsureCableEnd(cable.Output, DeviceFlow.Capture),
            ],
        };
    }

    /// <summary>The current rate of each end, without changing anything.</summary>
    public CableFormatResult ReadCableFormat(CableInfo cable) => new()
    {
        Steps =
        [
            ReadEnd(cable.Input, DeviceFlow.Render),
            ReadEnd(cable.Output, DeviceFlow.Capture),
        ],
    };

    private CableFormatStep ReadEnd(AudioDeviceInfo? dev, DeviceFlow flow)
    {
        if (_formats is null || dev is null) return new CableFormatStep(flow, dev?.Name, ApplyOutcome.Skipped, null, null);
        try
        {
            return new CableFormatStep(flow, dev.Name, ApplyOutcome.AlreadySet, _formats.Read(dev.Id)?.SampleRate, null);
        }
        catch (Exception ex) when (IsSettingError(ex))
        {
            return new CableFormatStep(flow, dev.Name, ApplyOutcome.Failed, null, ex.Message);
        }
    }

    /// <summary>True only for VB-CABLE's own two endpoints, by name and direction.</summary>
    public static bool IsCableEndpoint(string? name, DeviceFlow flow) =>
        name is not null && (flow == DeviceFlow.Render
            ? name.StartsWith(CableDetector.InputName, StringComparison.OrdinalIgnoreCase)
            : name.StartsWith(CableDetector.OutputName, StringComparison.OrdinalIgnoreCase));

    private CableFormatStep EnsureCableEnd(AudioDeviceInfo? dev, DeviceFlow flow)
    {
        if (_formats is null || dev is null) return new CableFormatStep(flow, dev?.Name, ApplyOutcome.Skipped, null, null);
        // Never a real device: the report must say cable, and so must Windows right now.
        var who = _formats.Find(dev.Id);
        if (dev.Flow != flow || !IsCableEndpoint(dev.Name, flow) || who is null || who.Flow != flow || !IsCableEndpoint(who.Name, flow))
            return new CableFormatStep(flow, dev.Name, ApplyOutcome.Skipped, null, null);
        if (who.Status != DeviceStatus.Active)
            return new CableFormatStep(flow, dev.Name, ApplyOutcome.Skipped, null, null);

        string key = CableFormatKey(dev.Id);
        try
        {
            return _journal.Update(list =>
            {
                var now = _formats.Read(dev.Id);
                if (now is null) return new CableFormatStep(flow, dev.Name, ApplyOutcome.Failed, null, L.Get("Changes.FormatUnreadable"));
                var existing = list.FirstOrDefault(e => e.Key == key);
                if (now.SampleRate == CableRate)
                {
                    if (existing is null) return new CableFormatStep(flow, dev.Name, ApplyOutcome.AlreadySet, CableRate, null);
                    existing.Applied = true;
                    return new CableFormatStep(flow, dev.Name, ApplyOutcome.AlreadyOurs, CableRate, null);
                }
                // An old entry whose rate is no longer in place (never applied, or changed since): what Windows has now is "before".
                if (existing is not null) list.Remove(existing);

                list.Add(new ChangeEntry
                {
                    Key = key,
                    Kind = ChangeKind.CableFormat,
                    Scope = ChangeScope.Install,
                    At = DateTime.Now,
                    DeviceId = dev.Id,
                    DeviceName = dev.Name,
                    Flow = flow,
                    OldDeviceFormat = Convert.ToBase64String(now.Device),
                    OldMixFormat = Convert.ToBase64String(now.Mix),
                    OldRate = now.SampleRate,
                    NewRate = CableRate,
                });
                _journal.SaveLocked(list); // write-ahead
                _formats.Write(dev.Id, WaveFormatBytes.WithRate(now.Device, CableRate), WaveFormatBytes.WithRate(now.Mix, CableRate));
                var after = _formats.Read(dev.Id)?.SampleRate;
                if (after != CableRate)
                    throw new InvalidOperationException(L.F("Changes.FormatNotTaken", RateText(after)));
                list.Single(e => e.Key == key).Applied = true;
                return new CableFormatStep(flow, dev.Name, ApplyOutcome.Changed, CableRate, null);
            });
        }
        catch (Exception ex) when (IsSettingError(ex))
        {
            // Back to before (only if the new rate did get in), entry gone; the app resamples as before.
            UndoKey(key);
            int? rate = null;
            try { rate = _formats.Read(dev.Id)?.SampleRate; }
            catch (Exception ex2) when (IsSettingError(ex2)) { }
            return new CableFormatStep(flow, dev.Name, ApplyOutcome.Failed, rate, ex.Message);
        }
    }

    private UndoOutcome UndoCableFormat(ChangeEntry e)
    {
        if (_formats is null) throw new InvalidOperationException(L.Get("Changes.FormatNoAccess"));
        if (e.DeviceId is null) return UndoOutcome.LeftAsIs;
        var who = _formats.Find(e.DeviceId);
        if (who is null || who.Status == DeviceStatus.NotPresent) return UndoOutcome.Gone; // driver removed: nothing left to put back
        // The ID was checked as the cable when the entry was written, and endpoint IDs are unique and stable:
        // a renamed cable is still found. A different direction means it is not that endpoint: keep the entry.
        if (e.Flow is { } flow ? who.Flow != flow : !IsCableEndpoint(who.Name, who.Flow))
            throw new InvalidOperationException(L.Get("Changes.FormatOtherDevice"));
        if (who.Status != DeviceStatus.Active) throw new InvalidOperationException(L.Get("Changes.FormatNotActive"));
        var now = _formats.Read(e.DeviceId) ?? throw new InvalidOperationException(L.Get("Changes.FormatUnreadable"));
        byte[] oldDevice = Convert.FromBase64String(e.OldDeviceFormat ?? "");
        byte[] oldMix = Convert.FromBase64String(e.OldMixFormat ?? "");
        if (!WaveFormatBytes.IsValid(oldDevice) || !WaveFormatBytes.IsValid(oldMix)) return UndoOutcome.LeftAsIs;
        // Only while the format is still exactly what the app set (rate, channels, bit depth): a format
        // someone chose since stays.
        if (now.SampleRate != e.NewRate || WaveFormatBytes.Channels(now.Device) != WaveFormatBytes.Channels(oldDevice)
            || WaveFormatBytes.Bits(now.Device) != WaveFormatBytes.Bits(oldDevice) || WaveFormatBytes.BlockAlign(now.Device) != WaveFormatBytes.BlockAlign(oldDevice))
            return UndoOutcome.LeftAsIs;
        _formats.Write(e.DeviceId, oldDevice, oldMix);
        return UndoOutcome.Restored;
    }
}
