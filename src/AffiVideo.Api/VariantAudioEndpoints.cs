using System.Globalization;
using AffiVideo.Application.Projects;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;

namespace AffiVideo.Api;

internal static class VariantAudioEndpoints
{
    // Room for the form around the file, so that a file a little over the limit
    // is refused with the reason and not cut off by the server.
    private const long LargestRequest = VariantAudio.MaxUploadBytes + 1024 * 1024;

    // The narration and music of one of the caller's Organization's Variants. As with
    // the Variant itself, another Organization's is answered like one that does not exist: 404.
    public static void MapVariantAudio(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/projects/{projectId:guid}/variants/{variantId:guid}/audio").WithTags("Variant audio");

        group.MapGet("", async Task<Results<Ok<VariantAudioResponse[]>, NotFound>> (
                Guid projectId, Guid variantId, IVariantAudio audio, CancellationToken cancellationToken) =>
                await audio.ListAsync(projectId, variantId, cancellationToken) is { } found
                    ? TypedResults.Ok(found.Select(ToResponse).ToArray())
                    : TypedResults.NotFound())
            .WithName("ListVariantAudio")
            .WithSummary("The Variant's narration and music: at most one of each, narration first.");

        group.MapPost("", async Task<Results<Created<VariantAudioResponse>, ValidationProblem, NotFound>> (
                Guid projectId, Guid variantId, [FromForm] VariantAudioKind kind, IFormFile? file,
                [FromForm] string? rightsConfirmed, [FromForm] string? volumePercent,
                IVariantAudio audio, CancellationToken cancellationToken) =>
            {
                // A number is read as a kind too, and "7" is no kind at all.
                if (!Enum.IsDefined(kind)) return Refused("kind", "Choose Narration or Music.");
                if (file is null) return Refused(AudioChange.File, "Choose a file to upload.");
                // Anything but a plain yes is a no, and anything but a whole number is no volume.
                var confirmed = bool.TryParse(rightsConfirmed, out var said) && said;
                int? volume = null;
                // Only Music has a volume: whatever comes with Narration is not read.
                if (kind == VariantAudioKind.Music && !string.IsNullOrWhiteSpace(volumePercent))
                {
                    if (!int.TryParse(volumePercent, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var asked))
                    {
                        return Refused(AudioChange.VolumePercent, AudioChange.NoSuchVolume);
                    }
                    volume = asked;
                }

                // Only the bytes are passed on. The file's name and the type the browser declared are not.
                await using var upload = file.OpenReadStream();
                return await audio.AddAsync(projectId, variantId, kind, upload, confirmed, volume, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Record: { } added } => TypedResults.Created(
                        $"/api/v1/projects/{projectId}/variants/{variantId}/audio/{added.Audio.Id}", ToResponse(added)),
                    var refused => Refused(refused.Field!, refused.Refused!),
                };
            })
            // Reading a form asks for the framework's anti-forgery middleware. Every
            // state-changing request has already been through RequireAntiforgeryToken.
            .DisableAntiforgery()
            .WithMetadata(new RequestSizeLimitAttribute(LargestRequest))
            .WithName("UploadVariantAudio")
            .WithSummary(
                "Sets the Variant's narration or music from an MP3 or WAV file, in place of what it had. The member must " +
                "say rightsConfirmed=true: that they hold the rights to it, which is recorded in their name. The file is " +
                "decoded and kept as a WAV. Music takes a volumePercent from 0 to 100.");

        group.MapGet("/{audioId:guid}/content", async Task<Results<FileStreamHttpResult, NotFound>> (
                Guid projectId, Guid variantId, Guid audioId, IVariantAudio audio, HttpContext context, CancellationToken cancellationToken) =>
            {
                var content = await audio.OpenAsync(projectId, variantId, audioId, cancellationToken);
                if (content is null) return TypedResults.NotFound();

                // A browser may keep the sound, but asks before playing it again, so
                // every playing is authorised. Audio's content never changes.
                context.Response.Headers.CacheControl = "private, no-cache";
                context.Response.Headers.XContentTypeOptions = "nosniff";
                return TypedResults.Stream(content, VariantAudio.ContentType, entityTag: new EntityTagHeaderValue($"\"{audioId:N}\""));
            })
            .Produces(StatusCodes.Status200OK, contentType: VariantAudio.ContentType)
            .WithName("GetVariantAudioContent")
            .WithSummary("The sound, as a WAV, to listen to.");

        group.MapPut("/{audioId:guid}/volume", async Task<Results<Ok<VariantAudioResponse>, ValidationProblem, NotFound>> (
                Guid projectId, Guid variantId, Guid audioId, AudioVolumeRequest request, IVariantAudio audio,
                CancellationToken cancellationToken) =>
                await audio.SetVolumeAsync(projectId, variantId, audioId, request.VolumePercent, cancellationToken) switch
                {
                    null => TypedResults.NotFound(),
                    { Record: { } changed } => TypedResults.Ok(ToResponse(changed)),
                    var refused => Refused(refused.Field!, refused.Refused!),
                })
            .WithName("SetVariantAudioVolume")
            .WithSummary(
                "Sets how loud the music is beside narration, from 0 to 100. A video rendered afterwards is mixed at it. " +
                "Narration has no volume to set.");

        group.MapDelete("/{audioId:guid}", async Task<Results<NoContent, NotFound>> (
                Guid projectId, Guid variantId, Guid audioId, IVariantAudio audio, CancellationToken cancellationToken) =>
                await audio.RemoveAsync(projectId, variantId, audioId, cancellationToken)
                    ? TypedResults.NoContent()
                    : TypedResults.NotFound())
            .WithName("RemoveVariantAudio")
            .WithSummary("Removes the narration or the music, and its file. Videos already rendered keep their sound.");
    }

    private static ValidationProblem Refused(string field, string reason) =>
        TypedResults.ValidationProblem(new Dictionary<string, string[]> { [field] = [reason] });

    private static VariantAudioResponse ToResponse(VariantAudioRecord record) => new(
        record.Audio.Id,
        record.Audio.VariantId,
        record.Audio.Kind,
        record.Audio.DurationMs,
        record.Audio.SampleRate,
        record.Audio.Channels,
        record.Audio.SizeInBytes,
        record.Audio.VolumePercent,
        record.Audio.RightsConfirmedByMemberId,
        record.RightsConfirmedByEmail,
        record.Audio.RightsConfirmedAt,
        record.Audio.CreatedAt);
}
