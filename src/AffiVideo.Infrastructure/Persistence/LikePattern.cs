namespace AffiVideo.Infrastructure.Persistence;

/// <summary>What a member searches for, as a pattern for ILIKE. It is text, never a pattern of their own.</summary>
internal static class LikePattern
{
    /// <summary>The escape character to name in the ILIKE the pattern is used in.</summary>
    public const string Escape = "\\";

    /// <summary>Matches whatever contains this text, without the spaces around it.</summary>
    public static string Containing(string text) => $"%{text.Trim()
        .Replace(Escape, Escape + Escape, StringComparison.Ordinal)
        .Replace("%", Escape + "%", StringComparison.Ordinal)
        .Replace("_", Escape + "_", StringComparison.Ordinal)}%";
}
