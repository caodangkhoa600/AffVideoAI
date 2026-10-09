using AffiVideo.Domain;

namespace AffiVideo.Application.Projects;

/// <summary>
/// The narration and music of the caller's Organization's Variants. A Project, a
/// Variant or audio of another Organization is answered exactly as one that does not exist.
/// </summary>
public interface IVariantAudio
{
    /// <summary>
    /// Decodes what was sent, and keeps it as a WAV if it is sound within the
    /// limits and the member confirms they hold the rights to it. The confirmation
    /// is recorded in the member's name, on the audio and in the audit log. It
    /// takes the place of the Variant's audio of the same kind.
    /// </summary>
    /// <param name="upload">The bytes a member sent. Nothing else about the file is trusted or asked for.</param>
    /// <param name="rightsConfirmed">Whether the member said they hold the rights to this audio.</param>
    /// <param name="volumePercent">For music. Left out, it is the volume of the music replaced, or the default.</param>
    /// <returns>Null when the Project has no such Variant.</returns>
    Task<AudioChange?> AddAsync(
        Guid projectId, Guid variantId, VariantAudioKind kind, Stream upload, bool rightsConfirmed, int? volumePercent,
        CancellationToken cancellationToken);

    /// <summary>Narration before music. Null when the Project has no such Variant.</summary>
    Task<IReadOnlyList<VariantAudioRecord>?> ListAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken);

    /// <summary>The stored WAV, for the caller to dispose. Null when the Variant has no such audio.</summary>
    Task<Stream?> OpenAsync(Guid projectId, Guid variantId, Guid audioId, CancellationToken cancellationToken);

    /// <summary>Sets how loud the music is beside narration.</summary>
    /// <returns>Null when the Variant has no such audio.</returns>
    Task<AudioChange?> SetVolumeAsync(Guid projectId, Guid variantId, Guid audioId, int volumePercent, CancellationToken cancellationToken);

    /// <returns>False when the Variant has no such audio.</returns>
    Task<bool> RemoveAsync(Guid projectId, Guid variantId, Guid audioId, CancellationToken cancellationToken);
}

/// <param name="RightsConfirmedByEmail">Of the member who confirmed they hold the rights.</param>
public sealed record VariantAudioRecord(VariantAudio Audio, string? RightsConfirmedByEmail);

/// <summary>
/// Either the audio as it now is, or the reason it was refused, in words for the
/// member, and the part of the request the reason is about.
/// </summary>
public sealed record AudioChange(VariantAudioRecord? Record, string? Field, string? Refused)
{
    public const string File = "file";
    public const string RightsConfirmed = "rightsConfirmed";
    public const string VolumePercent = "volumePercent";

    /// <summary>What a member is told about a volume that is none.</summary>
    public static readonly string NoSuchVolume = $"Choose a volume from 0 to {VariantAudio.MaxVolumePercent}.";

    public static AudioChange Made(VariantAudioRecord record) => new(record, null, null);

    public static AudioChange Refuse(string field, string reason) => new(null, field, reason);
}
