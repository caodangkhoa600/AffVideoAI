using System.ComponentModel.DataAnnotations;
using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <summary>
/// The Product a Project is for and its brief. The brief is fixed once the Project is created.
/// </summary>
/// <param name="Audience">Who the videos are for.</param>
/// <param name="Language">The language of the videos: <c>vi</c>.</param>
/// <param name="TargetDurationSeconds">How long each video is to be, from 15 to 30 seconds.</param>
/// <param name="Objective">What the videos are meant to get the viewer to do.</param>
public sealed record ProjectRequest(
    Guid ProductId,
    [Required, StringLength(Project.AudienceMaxLength)] string Audience,
    [Required, VideoLanguage] string Language,
    [Range(Project.MinTargetDurationSeconds, Project.MaxTargetDurationSeconds)] int TargetDurationSeconds,
    [Required, StringLength(Project.ObjectiveMaxLength)] string Objective);

/// <param name="ProductName">As the Product is named now.</param>
/// <param name="VariantCount">How many Variants the Project has.</param>
public sealed record ProjectResponse(
    Guid Id,
    Guid ProductId,
    string ProductName,
    string Audience,
    string Language,
    int TargetDurationSeconds,
    string Objective,
    int VariantCount,
    DateTimeOffset CreatedAt);

/// <param name="Hook">The opening line, meant to stop the viewer scrolling.</param>
public sealed record VariantRequest(
    CreativeTemplate CreativeTemplate,
    [Required, StringLength(Variant.HookMaxLength)] string Hook);

/// <param name="Hook">The Hook of the new Variant, which must not be the one the duplicated Variant has.</param>
public sealed record DuplicateVariantRequest(
    [Required, StringLength(Variant.HookMaxLength)] string Hook);

/// <summary>A Variant. Nothing about it ever changes, its identifier least of all.</summary>
public sealed record VariantResponse(
    Guid Id,
    Guid ProjectId,
    CreativeTemplate CreativeTemplate,
    string Hook,
    DateTimeOffset CreatedAt);

/// <summary>A language a video can be made in. A blank one is for [Required] to refuse.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class VideoLanguageAttribute() : ValidationAttribute("Videos are made in Vietnamese (vi).")
{
    public override bool IsValid(object? value) =>
        value is null
        || (value is string code && (string.IsNullOrWhiteSpace(code) || ContentLanguages.VideoCodes.Contains(code)));
}

/// <summary>
/// The narration or the music a member uploaded for a Variant. What is kept is a
/// WAV of the sound the upload decodes to.
/// </summary>
/// <param name="DurationMs">Of the stored sound, in milliseconds.</param>
/// <param name="SampleRate">Samples a second.</param>
/// <param name="Channels">One for mono, two for stereo.</param>
/// <param name="SizeInBytes">Of the stored WAV, not of the file that was sent.</param>
/// <param name="VolumePercent">How loud it is in a video, from 0 to 100. Narration is always at 100; music is as loud as narration at 100.</param>
/// <param name="RightsConfirmedByMemberId">The member who confirmed, on uploading it, that they hold the rights to it.</param>
public sealed record VariantAudioResponse(
    Guid Id,
    Guid VariantId,
    VariantAudioKind Kind,
    int DurationMs,
    int SampleRate,
    int Channels,
    long SizeInBytes,
    int VolumePercent,
    Guid RightsConfirmedByMemberId,
    string? RightsConfirmedByEmail,
    DateTimeOffset RightsConfirmedAt,
    DateTimeOffset CreatedAt);

/// <param name="VolumePercent">How loud the music is beside narration, from 0, not heard, to 100, as loud.</param>
public sealed record AudioVolumeRequest([Range(0, VariantAudio.MaxVolumePercent)] int VolumePercent);
