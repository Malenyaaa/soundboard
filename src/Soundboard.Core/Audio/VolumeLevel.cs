namespace Soundboard.Core.Audio;

/// <summary>
/// The step a volume shows on the speaker symbol: 0 = muted (struck through), 1–3 = number of
/// sound arcs. The percent is rounded the same way the number beside the slider is, so the
/// symbol is struck through exactly when the number reads "0 %".
/// </summary>
public static class VolumeLevel
{
    public const int Muted = 0;
    public const int Max = 3;

    /// <summary>0 at 0 %, 1 arc up to 33 %, 2 up to 66 %, 3 above: three equal thirds of the slider.</summary>
    public static int Of(double percent)
    {
        if (double.IsNaN(percent)) return Muted;
        double p = Math.Round(percent);
        if (p <= 0) return Muted;
        if (p <= 33) return 1;
        if (p <= 66) return 2;
        return Max;
    }
}
