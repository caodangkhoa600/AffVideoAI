using System.Text.Json;
using System.Text.Json.Serialization;
using AffiVideo.Domain;

namespace AffiVideo.Infrastructure.Persistence;

/// <summary>
/// How a production cost record's Technique counts are kept in their column:
/// <c>[{"technique":"ImageMotion","scenes":3}]</c>, each Technique by name.
/// </summary>
internal static class TechniqueCountsJson
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(allowIntegerValues: false) },
    };

    public static string Write(IReadOnlyList<TechniqueCount> counts) => JsonSerializer.Serialize(counts, Options);

    public static IReadOnlyList<TechniqueCount> Read(string json) =>
        JsonSerializer.Deserialize<List<TechniqueCount>>(json, Options) ?? [];
}
