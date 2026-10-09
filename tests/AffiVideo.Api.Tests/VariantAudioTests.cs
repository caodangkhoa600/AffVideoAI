using System.Buffers.Binary;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AffiVideo.Contracts;
using AffiVideo.Domain;
using Microsoft.AspNetCore.Http;

namespace AffiVideo.Api.Tests;

/// <summary>
/// The narration and music a member uploads for a Variant, as the member sees
/// them over HTTP. How they sound in a video is in <see cref="RenderAudioTests"/>.
/// </summary>
public sealed class VariantAudioTests(AffiVideoApp app)
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_member_uploads_narration_and_music_and_sees_them_on_the_Variant()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;

        var narration = await UploadedAsync(member, variant, VariantAudioKind.Narration, Tone(seconds: 3));
        var music = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 2, channels: 2));

        var audio = await member.GetAsync<VariantAudioResponse[]>(Audio(variant));
        Assert.Equal([narration.Id, music.Id], audio.Select(a => a.Id));
        Assert.Equal([VariantAudioKind.Narration, VariantAudioKind.Music], audio.Select(a => a.Kind));
        Assert.Equal([3000, 2000], audio.Select(a => a.DurationMs));
        Assert.Equal([1, 2], audio.Select(a => a.Channels));
        Assert.All(audio, a => Assert.Equal(variant.Id, a.VariantId));
        Assert.All(audio, a => Assert.Equal(44100, a.SampleRate));
    }

    [Fact]
    public async Task An_upload_answers_with_the_audio_and_where_it_is()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;

        var response = await UploadAsync(member, variant, VariantAudioKind.Narration, Tone(seconds: 2));

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var audio = await ReadAsync<VariantAudioResponse>(response);
        Assert.Equal($"{Audio(variant)}/{audio.Id}", response.Headers.Location?.OriginalString);
        Assert.Equal(VariantAudioKind.Narration, audio.Kind);
        Assert.Equal(2000, audio.DurationMs);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("false")]
    [InlineData("maybe")]
    public async Task An_upload_is_refused_unless_the_member_confirms_they_hold_the_rights(string? rightsConfirmed)
    {
        var (member, organization, variant) = await MemberWithVariantAsync();
        using var _ = member;

        var response = await UploadAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 2), rightsConfirmed: rightsConfirmed);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("hold the rights", await ReasonAsync(response, "rightsConfirmed"));
        Assert.Empty(await member.GetAsync<VariantAudioResponse[]>(Audio(variant)));
        Assert.Empty(await app.StoredKeysAsync($"organizations/{organization.Id}/"));
    }

    [Fact]
    public async Task The_confirmation_of_rights_is_recorded_with_who_made_it_and_when_and_kept_in_the_audit_log()
    {
        var organization = await app.CreateOrganizationAsync();
        using var owner = await app.SignedInAsync(organization.Owner);
        var editorCredentials = await app.AddEditorAsync(owner, organization);
        using var editor = await app.SignedInAsync(editorCredentials);
        var variant = await NewVariantAsync(owner);
        var before = DateTimeOffset.UtcNow.AddSeconds(-1);

        var audio = await UploadedAsync(editor, variant, VariantAudioKind.Narration, Tone(seconds: 2));

        var session = await editor.GetAsync<SessionResponse>("/api/v1/session");
        Assert.Equal(session.Member.Id, audio.RightsConfirmedByMemberId);
        Assert.Equal(editorCredentials.Email, audio.RightsConfirmedByEmail);
        Assert.InRange(audio.RightsConfirmedAt, before, DateTimeOffset.UtcNow.AddSeconds(1));
        // The audit log keeps it when the audio itself has been removed.
        (await editor.DeleteAsync($"{Audio(variant)}/{audio.Id}")).EnsureSuccessStatusCode();
        var log = await owner.GetAsync<PagedResponse<AuditLogEntryResponse>>($"/api/v1/organizations/{organization.Id}/audit-log");
        var entry = Assert.Single(log.Items, e => e.Action == AuditActions.AudioRightsConfirmed);
        Assert.Equal(audio.Id, entry.SubjectId);
        Assert.Equal(session.Member.Id, entry.ActorMemberId);
    }

    [Theory]
    [InlineData("wav")]
    [InlineData("mp3")]
    public async Task Whatever_kind_of_file_is_uploaded_what_is_served_is_a_WAV_of_the_sound_it_decodes_to(string format)
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;
        var file = format == "mp3" ? Mp3() : Tone(seconds: 2, sampleRate: 44100, channels: 2, bits: 24);

        var audio = await UploadedAsync(member, variant, VariantAudioKind.Music, file);

        var served = await member.GetAsync(Content(variant, audio.Id));
        Assert.Equal(HttpStatusCode.OK, served.StatusCode);
        Assert.Equal("audio/wav", served.Content.Headers.ContentType?.MediaType);
        Assert.True(served.Headers.CacheControl is { Private: true, NoCache: true });
        var bytes = await served.Content.ReadAsByteArrayAsync(Cancellation);
        Assert.Equal(audio.SizeInBytes, bytes.Length);
        var wav = ReadWav(bytes);
        Assert.Equal((1, 2, 44100, 16), (wav.Format, wav.Channels, wav.SampleRate, wav.Bits));
        // An MP3 decodes to a little more than was encoded: the encoder pads its first and last frames.
        Assert.InRange(audio.DurationMs, 2000, 2100);
        Assert.InRange(wav.Samples.Length / 2 * 1000.0 / 44100, audio.DurationMs - 1, audio.DurationMs + 1);
        // And it is the tone that went in: as loud, give or take what MP3 loses.
        Assert.InRange(Rms(wav.Samples), 0.5 / Math.Sqrt(2) * 0.9, 0.5 / Math.Sqrt(2) * 1.1);
    }

    [Fact]
    public async Task What_is_stored_is_the_sound_and_nothing_else_that_was_in_the_file()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;
        var tagged = Mp3();
        Assert.Contains("Someone", System.Text.Encoding.Latin1.GetString(tagged));

        var audio = await UploadedAsync(member, variant, VariantAudioKind.Music, tagged);

        var stored = await member.Http.GetByteArrayAsync(Content(variant, audio.Id), Cancellation);
        Assert.DoesNotContain("Someone", System.Text.Encoding.Latin1.GetString(stored));
        Assert.Equal(44 + ReadWav(stored).Samples.Length * 2, stored.Length);
    }

    [Theory]
    [InlineData(8, 1, 8000)]
    [InlineData(16, 2, 22050)]
    [InlineData(24, 1, 48000)]
    [InlineData(32, 2, 44100)]
    public async Task A_WAV_of_any_usual_sample_size_is_kept_as_16_bit_at_its_own_sample_rate(int bits, int channels, int sampleRate)
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;

        var audio = await UploadedAsync(
            member, variant, VariantAudioKind.Narration, Tone(seconds: 1.5, sampleRate: sampleRate, channels: channels, bits: bits));

        var wav = ReadWav(await member.Http.GetByteArrayAsync(Content(variant, audio.Id), Cancellation));
        Assert.Equal((1500, sampleRate, channels), (audio.DurationMs, audio.SampleRate, audio.Channels));
        Assert.Equal((channels, sampleRate, 16), (wav.Channels, wav.SampleRate, wav.Bits));
        Assert.InRange(Rms(wav.Samples), 0.5 / Math.Sqrt(2) * 0.97, 0.5 / Math.Sqrt(2) * 1.03);
    }

    [Fact]
    public async Task A_WAV_written_to_a_pipe_which_could_not_say_how_long_it_is_is_read_to_its_end()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;
        var piped = Tone(seconds: 2);
        BinaryPrimitives.WriteUInt32LittleEndian(piped.AsSpan(4), uint.MaxValue);
        BinaryPrimitives.WriteUInt32LittleEndian(piped.AsSpan(40), uint.MaxValue);

        var audio = await UploadedAsync(member, variant, VariantAudioKind.Narration, piped);

        Assert.Equal(2000, audio.DurationMs);
    }

    [Fact]
    public async Task An_upload_is_judged_by_what_is_in_it_not_by_its_name_or_the_type_it_claims()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;

        var soundCalledText = await UploadAsync(member, variant, VariantAudioKind.Narration, Tone(seconds: 2), "notes.txt", "text/plain");
        var textCalledSound = await UploadAsync(
            member, variant, VariantAudioKind.Music, "This is not a song."u8.ToArray(), "song.mp3", "audio/mpeg");

        Assert.Equal(HttpStatusCode.Created, soundCalledText.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, textCalledSound.StatusCode);
        Assert.Contains("not an MP3 or WAV", await ReasonAsync(textCalledSound));
        Assert.Single(await member.GetAsync<VariantAudioResponse[]>(Audio(variant)));
    }

    public static TheoryData<string, string> Refused => new()
    {
        { "empty", "empty" },
        { "too large", "larger than 20 MB" },
        { "an image", "not an MP3 or WAV" },
        { "an AAC stream", "not an MP3 or WAV" },
        { "a WAV cut short", "damaged" },
        { "a WAV with no sound in it", "damaged" },
        { "an MP3 that is all header", "damaged" },
        { "too short", "at least 1 second" },
        { "too long", "5 minutes" },
        { "surround sound", "mono or stereo" },
        { "too many samples a second", "48000" },
        { "a compressed WAV", "not one this can read" },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public async Task An_upload_that_cannot_be_used_is_refused_with_the_reason_and_nothing_is_kept(string what, string reason)
    {
        var (member, organization, variant) = await MemberWithVariantAsync();
        using var _ = member;
        var file = what switch
        {
            "empty" => [],
            "too large" => new byte[VariantAudio.MaxUploadBytes + 1],
            "an image" => ProductAssetTests.Image(SkiaSharp.SKEncodedImageFormat.Png, 40, 40),
            // As an ADTS stream opens: the twelve bits an MP3 frame also sets, and a layer of nought.
            "an AAC stream" => [0xFF, 0xF1, 0x50, 0x80, 0x02, 0x1F, 0xFC, .. new byte[400]],
            "a WAV cut short" => Tone(seconds: 3)[..^4000],
            "a WAV with no sound in it" => Tone(seconds: 2)[..36],
            "an MP3 that is all header" => Mp3()[..120],
            "too short" => Tone(seconds: 0.9),
            // Small samples, and few of them a second, so that five minutes is a small file.
            "too long" => Tone(seconds: 301, sampleRate: 8000, bits: 8),
            "surround sound" => Tone(seconds: 2, channels: 6),
            "too many samples a second" => Tone(seconds: 2, sampleRate: 96000),
            "a compressed WAV" => Tone(seconds: 2, formatTag: 0x11),
            _ => throw new ArgumentOutOfRangeException(nameof(what)),
        };

        var response = await UploadAsync(member, variant, VariantAudioKind.Narration, file);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains(reason, await ReasonAsync(response));
        Assert.Empty(await member.GetAsync<VariantAudioResponse[]>(Audio(variant)));
        Assert.Empty(await app.StoredKeysAsync($"organizations/{organization.Id}/"));
    }

    [Fact]
    public async Task Audio_exactly_at_the_limits_of_length_is_accepted()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;

        var shortest = await UploadedAsync(member, variant, VariantAudioKind.Narration, Tone(seconds: 1));
        var longest = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 300, sampleRate: 8000, bits: 8));

        Assert.Equal(VariantAudio.MinDurationMs, shortest.DurationMs);
        Assert.Equal(VariantAudio.MaxDurationMs, longest.DurationMs);
    }

    [Fact]
    public async Task An_upload_with_no_file_or_no_such_kind_is_refused()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;
        using var noFile = new MultipartFormDataContent { { new StringContent("Music"), "kind" }, { new StringContent("true"), "rightsConfirmed" } };
        using var noSuchKind = Form("Jingle", Tone(seconds: 2));
        using var kindByNumber = Form("7", Tone(seconds: 2));

        var withoutFile = await member.PostFormAsync(Audio(variant), noFile);
        var withoutKind = await member.PostFormAsync(Audio(variant), noSuchKind);
        var withNumber = await member.PostFormAsync(Audio(variant), kindByNumber);

        Assert.Equal(HttpStatusCode.BadRequest, withoutFile.StatusCode);
        Assert.Contains("Choose a file", await ReasonAsync(withoutFile));
        Assert.Equal(HttpStatusCode.BadRequest, withoutKind.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, withNumber.StatusCode);
        Assert.Empty(await member.GetAsync<VariantAudioResponse[]>(Audio(variant)));
    }

    [Fact]
    public async Task New_narration_takes_the_place_of_the_old_and_leaves_the_music_alone()
    {
        var (member, organization, variant) = await MemberWithVariantAsync();
        using var _ = member;
        var first = await UploadedAsync(member, variant, VariantAudioKind.Narration, Tone(seconds: 2));
        var music = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 2));

        var second = await UploadedAsync(member, variant, VariantAudioKind.Narration, Tone(seconds: 3));

        var audio = await member.GetAsync<VariantAudioResponse[]>(Audio(variant));
        Assert.Equal([second.Id, music.Id], audio.Select(a => a.Id));
        Assert.Equal(HttpStatusCode.NotFound, (await member.GetAsync(Content(variant, first.Id))).StatusCode);
        var stored = await app.StoredKeysAsync($"organizations/{organization.Id}/");
        Assert.Equal(2, stored.Length);
        Assert.DoesNotContain(stored, key => key.Contains(first.Id.ToString()));
    }

    [Fact]
    public async Task A_member_removes_audio_and_its_file_goes_with_it()
    {
        var (member, organization, variant) = await MemberWithVariantAsync();
        using var _ = member;
        var narration = await UploadedAsync(member, variant, VariantAudioKind.Narration, Tone(seconds: 2));
        var music = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 2));

        var removed = await member.DeleteAsync($"{Audio(variant)}/{narration.Id}");
        var again = await member.DeleteAsync($"{Audio(variant)}/{narration.Id}");

        Assert.Equal(HttpStatusCode.NoContent, removed.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, again.StatusCode);
        Assert.Equal([music.Id], (await member.GetAsync<VariantAudioResponse[]>(Audio(variant))).Select(a => a.Id));
        var stored = Assert.Single(await app.StoredKeysAsync($"organizations/{organization.Id}/"));
        Assert.Equal($"organizations/{organization.Id}/variants/{variant.Id}/audio/{music.Id}.wav", stored);
    }

    [Fact]
    public async Task Music_starts_at_a_volume_under_narration_unless_the_member_chooses_one_and_the_volume_can_be_changed()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;

        var music = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 2));
        var changed = await member.PutAsync($"{Audio(variant)}/{music.Id}/volume", new AudioVolumeRequest(65));
        // Replacing the file keeps the volume the member had set, unless a new one comes with it.
        var replaced = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 3));
        var chosen = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 3), volumePercent: "0");

        Assert.Equal(VariantAudio.DefaultMusicVolumePercent, music.VolumePercent);
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(65, (await ReadAsync<VariantAudioResponse>(changed)).VolumePercent);
        Assert.Equal(65, replaced.VolumePercent);
        Assert.Equal(0, chosen.VolumePercent);
        Assert.Equal(0, Assert.Single(await member.GetAsync<VariantAudioResponse[]>(Audio(variant))).VolumePercent);
    }

    [Theory]
    [InlineData("-1")]
    [InlineData("101")]
    [InlineData("loud")]
    public async Task A_volume_that_is_not_from_0_to_100_is_refused(string volume)
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;
        var music = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 2), volumePercent: "40");

        var upload = await UploadAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 3), volumePercent: volume);
        var change = int.TryParse(volume, out var number)
            ? await member.PutAsync($"{Audio(variant)}/{music.Id}/volume", new AudioVolumeRequest(number))
            : await member.PutAsync($"{Audio(variant)}/{music.Id}/volume", new { volumePercent = volume });

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        var kept = Assert.Single(await member.GetAsync<VariantAudioResponse[]>(Audio(variant)));
        Assert.Equal((music.Id, 40), (kept.Id, kept.VolumePercent));
    }

    [Fact]
    public async Task Narration_is_always_at_full_volume_and_has_none_to_set()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;

        var narration = await UploadedAsync(member, variant, VariantAudioKind.Narration, Tone(seconds: 2), volumePercent: "20");
        var change = await member.PutAsync($"{Audio(variant)}/{narration.Id}/volume", new AudioVolumeRequest(20));

        Assert.Equal(100, narration.VolumePercent);
        Assert.Equal(HttpStatusCode.BadRequest, change.StatusCode);
        Assert.Contains("Only music", await ReasonAsync(change, "volumePercent"));
        Assert.Equal(100, Assert.Single(await member.GetAsync<VariantAudioResponse[]>(Audio(variant))).VolumePercent);
    }

    [Fact]
    public async Task A_file_is_stored_under_its_Organization_and_cannot_be_fetched_from_the_storage_directly()
    {
        var (member, organization, variant) = await MemberWithVariantAsync();
        using var _ = member;
        var audio = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 2));

        var key = Assert.Single(await app.StoredKeysAsync($"organizations/{organization.Id}/"));
        using var anyone = new HttpClient();
        var direct = await anyone.GetAsync(app.StorageAddress(key), Cancellation);

        Assert.Equal($"organizations/{organization.Id}/variants/{variant.Id}/audio/{audio.Id}.wav", key);
        Assert.Equal(HttpStatusCode.Forbidden, direct.StatusCode);
    }

    [Fact]
    public async Task Audio_is_only_found_under_the_Variant_it_belongs_to()
    {
        var organization = await app.CreateOrganizationAsync();
        using var member = await app.SignedInAsync(organization.Owner);
        var variant = await NewVariantAsync(member);
        var other = await NewVariantAsync(member);
        var audio = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 2));
        var underAnotherProject = $"{VariantTests.Variants(other.ProjectId)}/{variant.Id}/audio";

        HttpResponseMessage[] responses =
        [
            await member.GetAsync(Content(other, audio.Id)),
            await member.DeleteAsync($"{Audio(other)}/{audio.Id}"),
            await member.PutAsync($"{Audio(other)}/{audio.Id}/volume", new AudioVolumeRequest(10)),
            await member.GetAsync(underAnotherProject),
            await member.GetAsync($"{underAnotherProject}/{audio.Id}/content"),
            await member.GetAsync($"{VariantTests.Variants(variant.ProjectId)}/{Guid.NewGuid()}/audio"),
            await member.GetAsync(Content(variant, Guid.NewGuid())),
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.NotFound, response.StatusCode));
        Assert.Empty(await member.GetAsync<VariantAudioResponse[]>(Audio(other)));
        Assert.Equal(VariantAudio.DefaultMusicVolumePercent, Assert.Single(await member.GetAsync<VariantAudioResponse[]>(Audio(variant))).VolumePercent);
    }

    [Fact]
    public async Task Deleting_a_Project_deletes_the_audio_of_its_Variants_and_their_files()
    {
        var (member, organization, variant) = await MemberWithVariantAsync();
        using var _ = member;
        await UploadedAsync(member, variant, VariantAudioKind.Narration, Tone(seconds: 2));
        await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 2));

        var deleted = await member.DeleteAsync($"{ProjectTests.Projects}/{variant.ProjectId}");

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await app.StoredKeysAsync($"organizations/{organization.Id}/"));
    }

    [Fact]
    public async Task Audio_is_refused_to_someone_who_is_not_signed_in()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;
        var audio = await UploadedAsync(member, variant, VariantAudioKind.Music, Tone(seconds: 2));
        using var stranger = app.NewBrowser();

        HttpResponseMessage[] responses =
        [
            await stranger.GetAsync(Audio(variant)),
            await stranger.GetAsync(Content(variant, audio.Id)),
            await UploadAsync(stranger, variant, VariantAudioKind.Music, Tone(seconds: 2)),
            await stranger.PutAsync($"{Audio(variant)}/{audio.Id}/volume", new AudioVolumeRequest(10)),
            await stranger.DeleteAsync($"{Audio(variant)}/{audio.Id}"),
        ];

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode));
        Assert.Single(await member.GetAsync<VariantAudioResponse[]>(Audio(variant)));
    }

    [Fact]
    public async Task An_upload_without_an_anti_forgery_token_is_refused()
    {
        var (member, _, variant) = await MemberWithVariantAsync();
        using var _ = member;
        using var form = Form("Music", Tone(seconds: 2));

        var response = await member.Http.PostAsync(Audio(variant), form, Cancellation);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await member.GetAsync<VariantAudioResponse[]>(Audio(variant)));
    }

    internal static string Audio(VariantResponse variant) => $"{VariantTests.Variants(variant.ProjectId)}/{variant.Id}/audio";

    internal static string Content(VariantResponse variant, Guid audioId) => $"{Audio(variant)}/{audioId}/content";

    /// <summary>A two-second 440 Hz tone at half of full scale, as a stereo MP3 that names a title and an artist.</summary>
    internal static byte[] Mp3() => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tone.mp3"));

    /// <summary>A steady tone as a WAV file: every channel the same sine wave.</summary>
    /// <param name="amplitude">Of the wave's peak, where 1 is as loud as a file can be.</param>
    /// <param name="formatTag">How the samples are said to be stored: 1 is plain PCM.</param>
    internal static byte[] Tone(
        double seconds, double amplitude = 0.5, int frequency = 440, int sampleRate = 44100, int channels = 1, int bits = 16,
        int formatTag = 1)
    {
        var frames = (int)Math.Round(seconds * sampleRate);
        var bytesPerSample = bits / 8;
        var data = new byte[frames * channels * bytesPerSample];
        for (var frame = 0; frame < frames; frame++)
        {
            var value = amplitude * Math.Sin(2 * Math.PI * frequency * frame / sampleRate);
            for (var channel = 0; channel < channels; channel++)
            {
                var at = data.AsSpan((frame * channels + channel) * bytesPerSample, bytesPerSample);
                switch (bits)
                {
                    // Eight-bit samples are the only ones without a sign: silence is 128.
                    case 8: at[0] = (byte)Math.Round(128 + value * 127); break;
                    case 16: BinaryPrimitives.WriteInt16LittleEndian(at, (short)Math.Round(value * short.MaxValue)); break;
                    case 24:
                        var sample = (int)Math.Round(value * 8388607);
                        at[0] = (byte)sample;
                        at[1] = (byte)(sample >> 8);
                        at[2] = (byte)(sample >> 16);
                        break;
                    case 32: BinaryPrimitives.WriteInt32LittleEndian(at, (int)Math.Round(value * int.MaxValue)); break;
                    default: throw new ArgumentOutOfRangeException(nameof(bits));
                }
            }
        }

        var wav = new byte[44 + data.Length];
        "RIFF"u8.CopyTo(wav);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(4), 36 + data.Length);
        "WAVEfmt "u8.CopyTo(wav.AsSpan(8));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(16), 16);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(20), (short)formatTag);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(22), (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(24), sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(28), sampleRate * channels * bytesPerSample);
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(32), (short)(channels * bytesPerSample));
        BinaryPrimitives.WriteInt16LittleEndian(wav.AsSpan(34), (short)bits);
        "data"u8.CopyTo(wav.AsSpan(36));
        BinaryPrimitives.WriteInt32LittleEndian(wav.AsSpan(40), data.Length);
        data.CopyTo(wav.AsSpan(44));
        return wav;
    }

    /// <param name="rightsConfirmed">What the form says about the rights. Null leaves the field out.</param>
    /// <param name="volumePercent">What the form says about the volume. Null leaves the field out.</param>
    internal static async Task<HttpResponseMessage> UploadAsync(
        Browser member, VariantResponse variant, VariantAudioKind kind, byte[] file,
        string fileName = "sound.wav", string contentType = "audio/wav", string? rightsConfirmed = "true", string? volumePercent = null)
    {
        using var form = Form(kind.ToString(), file, fileName, contentType, rightsConfirmed, volumePercent);
        return await member.PostFormAsync(Audio(variant), form);
    }

    internal static async Task<VariantAudioResponse> UploadedAsync(
        Browser member, VariantResponse variant, VariantAudioKind kind, byte[] file, string? volumePercent = null)
    {
        var response = await UploadAsync(member, variant, kind, file, volumePercent: volumePercent);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync(Cancellation));
        return await ReadAsync<VariantAudioResponse>(response);
    }

    private static MultipartFormDataContent Form(
        string kind, byte[] file, string fileName = "sound.wav", string contentType = "audio/wav",
        string? rightsConfirmed = "true", string? volumePercent = null)
    {
        var content = new ByteArrayContent(file);
        content.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        var form = new MultipartFormDataContent { { new StringContent(kind), "kind" }, { content, "file", fileName } };
        if (rightsConfirmed is not null) form.Add(new StringContent(rightsConfirmed), "rightsConfirmed");
        if (volumePercent is not null) form.Add(new StringContent(volumePercent), "volumePercent");
        return form;
    }

    // A 16-bit PCM WAV as this system writes one: a 44-byte header and the samples, from -1 to 1.
    private static (int Format, int Channels, int SampleRate, int Bits, double[] Samples) ReadWav(byte[] wav)
    {
        Assert.Equal("RIFF"u8.ToArray(), wav[..4]);
        Assert.Equal("WAVEfmt "u8.ToArray(), wav[8..16]);
        Assert.Equal("data"u8.ToArray(), wav[36..40]);
        Assert.Equal(wav.Length - 8, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(4)));
        Assert.Equal(wav.Length - 44, BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(40)));
        var samples = new double[(wav.Length - 44) / 2];
        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(44 + index * 2)) / 32768.0;
        }
        return (
            BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(20)), BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(22)),
            BinaryPrimitives.ReadInt32LittleEndian(wav.AsSpan(24)), BinaryPrimitives.ReadInt16LittleEndian(wav.AsSpan(34)), samples);
    }

    private static double Rms(double[] samples) => Math.Sqrt(samples.Sum(sample => sample * sample) / samples.Length);

    // What the member is told is wrong, beside the field it is wrong with.
    private static async Task<string> ReasonAsync(HttpResponseMessage response, string field = "file")
    {
        var problem = await ReadAsync<HttpValidationProblemDetails>(response);
        return problem.Errors.TryGetValue(field, out var reasons) ? string.Join(" ", reasons) : "";
    }

    private async Task<(Browser Member, TestOrganization Organization, VariantResponse Variant)> MemberWithVariantAsync()
    {
        var organization = await app.CreateOrganizationAsync();
        var member = await app.SignedInAsync(organization.Owner);
        return (member, organization, await NewVariantAsync(member));
    }

    // A Variant needs nothing of its Product to be given audio: no photo and no Fact, so nothing else is stored.
    private static async Task<VariantResponse> NewVariantAsync(Browser member) =>
        await StoryboardTests.NewVariantAsync(member, await StoryboardTests.NewProductAsync(member));

    private static async Task<T> ReadAsync<T>(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
}
