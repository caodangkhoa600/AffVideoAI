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
