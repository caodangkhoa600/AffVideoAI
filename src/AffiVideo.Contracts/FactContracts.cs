using System.ComponentModel.DataAnnotations;
using AffiVideo.Domain;

namespace AffiVideo.Contracts;

/// <summary>
/// What a member types in about a Fact, when adding one and when replacing one.
/// </summary>
/// <param name="Language">The language the text is written in: <c>vi</c> or <c>en</c>.</param>
/// <param name="Source">Where the Fact came from, so that it can be checked again.</param>
public sealed record FactRequest(
    [Required, StringLength(Fact.TextMaxLength)] string Text,
    [Required, ContentLanguage] string Language,
    [StringLength(Fact.SourceMaxLength)] string? Source = null);

/// <summary>A Fact in one of its three states. Its text, language and source never change.</summary>
/// <param name="ConfirmedByMemberId">The member who Confirmed it. Kept when the Fact is later Withdrawn.</param>
public sealed record FactResponse(
    Guid Id,
    Guid ProductId,
    string Text,
    string Language,
    string? Source,
    FactState State,
    DateTimeOffset CreatedAt,
    Guid? ConfirmedByMemberId,
    string? ConfirmedByEmail,
    DateTimeOffset? ConfirmedAt,
    DateTimeOffset? WithdrawnAt);

/// <summary>One of the languages content can be written in. A blank one is for [Required] to refuse.</summary>
[AttributeUsage(AttributeTargets.Parameter | AttributeTargets.Property)]
public sealed class ContentLanguageAttribute() : ValidationAttribute("Choose Vietnamese (vi) or English (en).")
{
    public override bool IsValid(object? value) =>
        value is null
        || (value is string code && (string.IsNullOrWhiteSpace(code) || ContentLanguages.Codes.Contains(code)));
}
