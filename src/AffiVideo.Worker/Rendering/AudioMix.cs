namespace AffiVideo.Worker.Rendering;

/// <summary>
/// How loud each track is made before the narration and the music are mixed.
/// Every track is brought to one loudness by a single gain over its whole length,
/// so nothing in it is made louder or quieter than the rest of it, and a limiter
/// holds the peaks that gain sends over the ceiling. The music is then turned down
/// to the volume the member chose.
/// </summary>
public static class AudioMix
{
    /// <summary>The loudness every track is brought to, in LUFS: what short-video platforms play at, more or less.</summary>
    public const double TargetLufs = -16;

    /// <summary>The highest any peak may be, in dB below full scale.</summary>
    public const double CeilingDb = -1.5;

    /// <summary>The same ceiling as a share of full scale.</summary>
    public static readonly double Ceiling = Math.Pow(10, CeilingDb / 20);

    /// <summary>How long a track that is cut where the video ends takes to fade out.</summary>
    public const int FadeOutMs = 500;

    /// <summary>
    /// The most a track is turned up, in dB. A recording quieter than this allows for
    /// is mostly its own noise, and is left quieter than the target, not made of noise.
    /// </summary>
    public const double MaxGainDb = 30;

    /// <summary>Below this a track is silence, and no gain would make anything of it.</summary>
    private const double SilenceLufs = -70;

    /// <summary>What a track is multiplied by to bring it to the target loudness. Silence is left as it is.</summary>
    /// <param name="integratedLufs">How loud the track is over its whole length, as FFmpeg measured it. Negative infinity for silence.</param>
    public static double Gain(double integratedLufs)
    {
        if (!double.IsFinite(integratedLufs) || integratedLufs < SilenceLufs) return 1;

        return Math.Pow(10, Math.Min(TargetLufs - integratedLufs, MaxGainDb) / 20);
    }
}
