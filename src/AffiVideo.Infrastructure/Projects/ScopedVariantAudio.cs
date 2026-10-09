using AffiVideo.Application;
using AffiVideo.Application.Projects;
using AffiVideo.Application.Storage;
using AffiVideo.Domain;
using AffiVideo.Infrastructure.Audio;
using AffiVideo.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace AffiVideo.Infrastructure.Projects;

// No method names an Organization: the context's filter leaves only the caller's
// Variants and audio, and a file is only ever looked up by the key of audio found that way.
internal sealed class ScopedVariantAudio(
    AffiVideoDbContext database,
    IObjectStorage storage,
    Caller caller,
    TimeProvider clock,
    ILogger<ScopedVariantAudio> logger) : IVariantAudio
{
    public async Task<AudioChange?> AddAsync(
        Guid projectId, Guid variantId, VariantAudioKind kind, Stream upload, bool rightsConfirmed, int? volumePercent,
        CancellationToken cancellationToken)
    {
        if (!await VariantExistsAsync(projectId, variantId, cancellationToken)) return null;
        var organizationId = caller.OrganizationId
            ?? throw new InvalidOperationException("Audio is stored for the caller's Organization, and there is no caller.");
        var member = caller.MemberId ?? throw new InvalidOperationException("Only a member can confirm they hold the rights to audio.");

        // Before anything of the file is looked at: without the rights, what it holds does not matter.
        if (!rightsConfirmed)
        {
            return AudioChange.Refuse(AudioChange.RightsConfirmed, "Confirm that you hold the rights to this audio before uploading it.");
        }
        if (kind == VariantAudioKind.Music && volumePercent is { } asked && !VariantAudio.IsVolume(asked))
        {
            return AudioChange.Refuse(AudioChange.VolumePercent, AudioChange.NoSuchVolume);
        }

        var sound = await AudioNormaliser.NormaliseAsync(upload, cancellationToken);
        if (sound.Wav is null) return AudioChange.Refuse(AudioChange.File, sound.Refused!);

        var replaced = await database.VariantAudio
            .Where(a => a.VariantId == variantId && a.Kind == kind)
            .ToListAsync(cancellationToken);
        database.VariantAudio.RemoveRange(replaced);

        var now = clock.GetUtcNow();
        var audio = new VariantAudio(
            Guid.CreateVersion7(), organizationId, variantId, kind, sound.DurationMs, sound.SampleRate, sound.Channels, sound.Wav.Length,
            volumePercent ?? replaced.FirstOrDefault()?.VolumePercent ?? VariantAudio.DefaultMusicVolumePercent,
            member, now);
        database.VariantAudio.Add(audio);
        // The audit log keeps the confirmation when the audio itself has been replaced or removed.
        database.AuditLog.Add(new AuditLogEntry(Guid.CreateVersion7(), organizationId, member, AuditActions.AudioRightsConfirmed, audio.Id, now));

        // The file first: a record never points at a file that is not there.
        using (var wav = new MemoryStream(sound.Wav, writable: false))
        {
            await storage.PutAsync(audio.StorageKey, wav, VariantAudio.ContentType, cancellationToken);
        }
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception)
        {
            await DeleteFileAsync(audio.StorageKey);
            switch (exception)
            {
                case DbUpdateConcurrencyException:
                case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.UniqueViolation } }:
                    return AudioChange.Refuse(
                        AudioChange.File, $"Someone else changed this Variant's {Named(kind)} at the same moment. Try again.");
                // The Project was deleted, and the Variant with it, after it was read.
                case DbUpdateException { InnerException: PostgresException { SqlState: PostgresErrorCodes.ForeignKeyViolation } }:
                    return null;
                default:
                    throw;
            }
        }

        foreach (var old in replaced) await DeleteFileAsync(old.StorageKey);
        return AudioChange.Made((await FindRecordAsync(audio.Id, cancellationToken))!);
    }

    public async Task<IReadOnlyList<VariantAudioRecord>?> ListAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken)
    {
        if (!await VariantExistsAsync(projectId, variantId, cancellationToken)) return null;

        var found = await WithMember(database.VariantAudio.AsNoTracking().Where(a => a.VariantId == variantId)).ToListAsync(cancellationToken);
        return [.. found.OrderBy(record => record.Audio.Kind)];
    }

    public async Task<Stream?> OpenAsync(Guid projectId, Guid variantId, Guid audioId, CancellationToken cancellationToken)
    {
        var audio = await FindAsync(projectId, variantId, audioId, cancellationToken);
        if (audio is null) return null;

        var content = await storage.OpenAsync(audio.StorageKey, cancellationToken);
        if (content is null)
        {
            logger.LogError("The file of audio {AudioId} is missing from storage at {StorageKey}", audio.Id, audio.StorageKey);
        }
        return content;
    }

    public async Task<AudioChange?> SetVolumeAsync(
        Guid projectId, Guid variantId, Guid audioId, int volumePercent, CancellationToken cancellationToken)
    {
        var audio = await FindAsync(projectId, variantId, audioId, cancellationToken);
        if (audio is null) return null;
        if (audio.Kind != VariantAudioKind.Music)
        {
            return AudioChange.Refuse(AudioChange.VolumePercent, "Only music has a volume to set. Narration is always at full volume.");
        }
        if (!audio.SetVolume(volumePercent)) return AudioChange.Refuse(AudioChange.VolumePercent, AudioChange.NoSuchVolume);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Replaced or removed after it was read.
            return null;
        }
        return await FindRecordAsync(audio.Id, cancellationToken) is { } changed ? AudioChange.Made(changed) : null;
    }

    public async Task<bool> RemoveAsync(Guid projectId, Guid variantId, Guid audioId, CancellationToken cancellationToken)
    {
        var audio = await FindAsync(projectId, variantId, audioId, cancellationToken);
        if (audio is null) return false;

        database.VariantAudio.Remove(audio);
        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException)
        {
            // Replaced or removed by someone else after it was read, who deleted the file too.
            return false;
        }
        await DeleteFileAsync(audio.StorageKey);
        return true;
    }

    // The Variant has to be this Project's: an identifier from one Project does not work under another.
    private Task<bool> VariantExistsAsync(Guid projectId, Guid variantId, CancellationToken cancellationToken) =>
        database.Variants.AnyAsync(v => v.Id == variantId && v.ProjectId == projectId, cancellationToken);

    // And the audio this Variant's.
    private async Task<VariantAudio?> FindAsync(Guid projectId, Guid variantId, Guid audioId, CancellationToken cancellationToken) =>
        await VariantExistsAsync(projectId, variantId, cancellationToken)
            ? await database.VariantAudio.SingleOrDefaultAsync(a => a.Id == audioId && a.VariantId == variantId, cancellationToken)
            : null;

    private Task<VariantAudioRecord?> FindRecordAsync(Guid audioId, CancellationToken cancellationToken) =>
        WithMember(database.VariantAudio.AsNoTracking().Where(a => a.Id == audioId)).SingleOrDefaultAsync(cancellationToken);

    private IQueryable<VariantAudioRecord> WithMember(IQueryable<VariantAudio> audio) =>
        from one in audio
        join member in database.Members on one.RightsConfirmedByMemberId equals member.Id
        select new VariantAudioRecord(one, member.Email);

    private static string Named(VariantAudioKind kind) => kind == VariantAudioKind.Narration ? "narration" : "music";

    // The record is what makes a file reachable, and it is already gone or was never
    // saved. A file left behind is wasted space, not a reason to fail the request, and
    // it is deleted even if the member has stopped waiting.
    private async Task DeleteFileAsync(string key)
    {
        try
        {
            await storage.DeleteAsync(key, CancellationToken.None);
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "The file at {StorageKey} could not be deleted and is left behind", key);
        }
    }
}
