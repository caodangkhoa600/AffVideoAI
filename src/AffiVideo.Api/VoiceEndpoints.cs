using AffiVideo.Application;
using AffiVideo.Application.Organizations;
using AffiVideo.Application.Providers;
using AffiVideo.Contracts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.Extensions.Options;

namespace AffiVideo.Api;

internal static class VoiceEndpoints
{
    // The voices narration text can be spoken in. The list is configuration, the same
    // for every Organization; which of them is an Organization's own is that Organization's.
    public static void MapVoices(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/voices").WithTags("Voices");

        group.MapGet("", async Task<Ok<VoicesResponse>> (
                IOptions<GenerationOptions> generation, IOrganizations organizations, Caller caller, CancellationToken cancellationToken) =>
            {
                var offered = generation.Value.VoicesOnOffer;
                var chosen = caller.OrganizationId is { } own ? (await organizations.FindAsync(own, cancellationToken))?.DefaultVoice : null;
                return TypedResults.Ok(new VoicesResponse(
                    offered.Select(voice => new VoiceResponse(voice.Id, voice.Name, SampleOf(voice) is not null)).ToList(),
                    // The voice an Owner chose, while it is still on offer. Otherwise the first.
                    offered.Any(voice => voice.Id == chosen) ? chosen! : offered[0].Id));
            })
            .WithName("ListVoices")
            .WithSummary(
                "The voices narration text can be spoken in, and which of them a Project of the caller's Organization is given when " +
                "its brief names none.");

        group.MapGet("/{voiceId}/sample", Results<PhysicalFileHttpResult, NotFound> (string voiceId, IOptions<GenerationOptions> generation) =>
                generation.Value.VoicesOnOffer.FirstOrDefault(voice => voice.Id == voiceId) is { } voice && SampleOf(voice) is { } file
                    ? TypedResults.PhysicalFile(file, file.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) ? "audio/wav" : "audio/mpeg")
                    : TypedResults.NotFound())
            .Produces(StatusCodes.Status200OK, contentType: "audio/mpeg")
            .WithName("GetVoiceSample")
            .WithSummary(
                "A recording of the voice, to play before choosing it. It is a file kept with the application: playing it asks no " +
                "provider for anything and costs nothing. Answers 404 for a voice that has none.");
    }

    /// <summary>Why this is not a voice a member may choose, in words for the member. Null when it is one, or when none was named.</summary>
    public static string? NotOnOffer(string? voice, GenerationOptions generation) =>
        voice is null || generation.VoicesOnOffer.Any(offered => offered.Id == voice)
            ? null
            : $"There is no such voice. Choose one of: {string.Join(", ", generation.VoicesOnOffer.Select(offered => offered.Id))}.";

    // Where the voice's recording is, when it has one that is there.
    private static string? SampleOf(VoiceOption voice)
    {
        if (string.IsNullOrWhiteSpace(voice.Sample)) return null;
        var file = Path.GetFullPath(voice.Sample, AppContext.BaseDirectory);
        return File.Exists(file) ? file : null;
    }
}
