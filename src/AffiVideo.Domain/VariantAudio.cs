namespace AffiVideo.Domain;

/// <summary>
/// The narration or the music a member uploaded for a Variant, after confirming
/// they hold the rights to it. What is kept is never the file that was sent but a
/// WAV of the sound it decodes to, so every one is the same kind of file. A Variant
/// has at most one of each kind, and a video rendered for it has them mixed in.
/// </summary>
public sealed class VariantAudio : IOwnedByOrganization
{
    /// <summary>The largest file a member may send.</summary>
    public const long MaxUploadBytes = 20 * 1024 * 1024;

    /// <summary>The shortest sound that is kept. Less is a click, and its loudness cannot be measured.</summary>
    public const int MinDurationMs = 1000;

    /// <summary>The longest sound that is kept: longer than a song, and many times a video.</summary>
    public const int MaxDurationMs = 5 * 60 * 1000;

    /// <summary>The fewest and the most samples a second a WAV file may have.</summary>
    public const int MinSampleRate = 8000;

    public const int MaxSampleRate = 48000;

    /// <summary>Music is as loud as narration at 100, and is not heard at 0.</summary>
    public const int MaxVolumePercent = 100;

    /// <summary>Where music sits under narration unless the member says otherwise.</summary>
    public const int DefaultMusicVolumePercent = 30;

    /// <summary>What every stored one is: 16-bit PCM in a WAV file.</summary>
    public const string ContentType = "audio/wav";

    // For the data-access layer, which fills the properties itself.
    private VariantAudio()
    {
    }

    /// <param name="volumePercent">For music, how loud it is beside narration. Narration is always at 100.</param>
    /// <param name="rightsConfirmedByMemberId">The member who confirmed, on uploading it, that they hold the rights.</param>
    public VariantAudio(
        Guid id, Guid organizationId, Guid variantId, VariantAudioKind kind,
        int durationMs, int sampleRate, int channels, long sizeInBytes, int volumePercent,
        Guid rightsConfirmedByMemberId, DateTimeOffset now)
    {
        Id = id;
        OrganizationId = organizationId;
        VariantId = variantId;
        Kind = kind;
        DurationMs = durationMs;
        SampleRate = sampleRate;
        Channels = channels;
        SizeInBytes = sizeInBytes;
        VolumePercent = kind == VariantAudioKind.Music ? volumePercent : MaxVolumePercent;
        RightsConfirmedByMemberId = rightsConfirmedByMemberId;
        RightsConfirmedAt = now;
        CreatedAt = now;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    public Guid VariantId { get; private set; }

    public VariantAudioKind Kind { get; private set; }

    /// <summary>Of the stored sound, in milliseconds.</summary>
    public int DurationMs { get; private set; }

    /// <summary>Samples a second, as the file that was sent had them.</summary>
    public int SampleRate { get; private set; }

    /// <summary>One for mono, two for stereo.</summary>
    public int Channels { get; private set; }

    /// <summary>Of the stored WAV, not of the file that was sent.</summary>
    public long SizeInBytes { get; private set; }

    /// <summary>How loud it is in the mix, from 0 to 100. Only music is ever below 100.</summary>
    public int VolumePercent { get; private set; }

    /// <summary>The member who confirmed, on uploading it, that they hold the rights to it.</summary>
    public Guid RightsConfirmedByMemberId { get; private set; }

    public DateTimeOffset RightsConfirmedAt { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>Whether this is a volume music can be set to.</summary>
    public static bool IsVolume(int volumePercent) => volumePercent is >= 0 and <= MaxVolumePercent;

    /// <summary>Sets how loud the music is beside narration.</summary>
    /// <returns>False, with nothing changed, for narration or for a volume that is none.</returns>
    public bool SetVolume(int volumePercent)
    {
        if (Kind != VariantAudioKind.Music || !IsVolume(volumePercent)) return false;

        VolumePercent = volumePercent;
        return true;
    }

    /// <summary>
    /// Where the file is in object storage. It starts with the Organization, so
    /// everything one Organization has stored is under one prefix of its own.
    /// </summary>
    public string StorageKey => $"organizations/{OrganizationId}/variants/{VariantId}/audio/{Id}.wav";
}

public enum VariantAudioKind
{
    /// <summary>A voice speaking over the video.</summary>
    Narration,

    /// <summary>Music under the video, at a volume the member chooses.</summary>
    Music,
}
