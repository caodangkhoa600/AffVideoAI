using System.Buffers.Binary;
using System.Globalization;
using AffiVideo.Domain;
using NLayer;

namespace AffiVideo.Infrastructure.Audio;

/// <summary>
/// Turns what a member sent into the WAV that is kept, or says why it cannot be.
/// Only the bytes are looked at, and the whole of the sound is decoded: a file is
/// audio because it decodes to some, not because it says so. The WAV holds the
/// samples the file decodes to and nothing else of it: its title, its artist and
/// its cover are left behind. It is decoded here, in managed code, so that the
/// only thing FFmpeg is ever handed is a file this system wrote.
/// </summary>
internal static class AudioNormaliser
{
    private const int WavHeaderLength = 44;
    private const int PcmFormat = 1;
    private const int FloatFormat = 3;
    private const int ExtensibleFormat = 0xFFFE;

    private const string NotAudio = "The file is not an MP3 or WAV audio file.";
    private const string Damaged = "The audio is damaged and cannot be read.";
    private const string UnreadableWav =
        "The WAV file keeps its sound in a format that is not one this can read. Save it as PCM, or as an MP3.";

    private static readonly string LargestFile = $"{VariantAudio.MaxUploadBytes / (1024 * 1024)} MB";
    private static readonly string TooLong = $"The audio lasts longer than {VariantAudio.MaxDurationMs / 60_000} minutes.";

    public static async Task<NormalisedAudio> NormaliseAsync(Stream upload, CancellationToken cancellationToken)
    {
        var bytes = await ReadAsync(upload, cancellationToken);
        if (bytes is null) return NormalisedAudio.Refuse($"The file is larger than {LargestFile}.");
        if (bytes.Length == 0) return NormalisedAudio.Refuse("The file is empty.");

        if (IsWav(bytes)) return FromWav(bytes);
        if (IsMp3(bytes)) return FromMp3(bytes, cancellationToken);
        return NormalisedAudio.Refuse(NotAudio);
    }

    // Null when there is more than may be sent. The count is of what arrives, not of what was declared.
    private static async Task<byte[]?> ReadAsync(Stream upload, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await upload.ReadAsync(chunk, cancellationToken)) > 0)
        {
            if (buffer.Length + read > VariantAudio.MaxUploadBytes) return null;
            buffer.Write(chunk, 0, read);
        }
        return buffer.ToArray();
    }

    private static bool IsWav(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 12 && bytes[..4].SequenceEqual("RIFF"u8) && bytes[8..12].SequenceEqual("WAVE"u8);

    // An MP3 opens with a tag that says ID3, or straight away with a frame: eleven bits all set, and then
    // a layer that is one. An AAC stream opens the same way but for the layer, which it leaves at nought.
    private static bool IsMp3(ReadOnlySpan<byte> bytes) =>
        bytes.Length >= 4
        && (bytes[..3].SequenceEqual("ID3"u8) || (bytes[0] == 0xFF && (bytes[1] & 0xE0) == 0xE0 && (bytes[1] & 0x06) != 0));

    private static NormalisedAudio FromWav(byte[] bytes)
    {
        // A WAV file is a list of chunks, each with a name and a length. Two matter:
        // "fmt ", which says how the samples are stored, and "data", which is the samples.
        (int Format, int Channels, int SampleRate, int Bits)? format = null;
        var position = 12;
        while (position + 8 <= bytes.Length)
        {
            var name = bytes.AsSpan(position, 4);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(position + 4));
            var start = position + 8;
            // A program that wrote the file to a pipe could not go back to say how long the
            // sound was, and left the length at its largest: the sound is then the rest of the file.
            if (length == uint.MaxValue && name.SequenceEqual("data"u8)) length = (uint)(bytes.Length - start);
            // A chunk that says it is longer than the rest of the file: the file was cut short.
            if (length > bytes.Length - start) return NormalisedAudio.Refuse(Damaged);

            var chunk = bytes.AsSpan(start, (int)length);
            if (name.SequenceEqual("fmt "u8))
            {
                if (chunk.Length < 16) return NormalisedAudio.Refuse(Damaged);
                var tag = (int)BinaryPrimitives.ReadUInt16LittleEndian(chunk);
                // The extensible format keeps the real one further in.
                if (tag == ExtensibleFormat && chunk.Length >= 26) tag = BinaryPrimitives.ReadUInt16LittleEndian(chunk[24..]);
                format = (
                    tag, BinaryPrimitives.ReadUInt16LittleEndian(chunk[2..]),
                    (int)Math.Min(BinaryPrimitives.ReadUInt32LittleEndian(chunk[4..]), int.MaxValue),
                    BinaryPrimitives.ReadUInt16LittleEndian(chunk[14..]));
            }
            else if (name.SequenceEqual("data"u8))
            {
                return format is { } stored ? FromPcm(chunk, stored.Format, stored.Channels, stored.SampleRate, stored.Bits) : NormalisedAudio.Refuse(Damaged);
            }
            // A chunk of odd length is followed by one byte that belongs to nothing.
            position = start + (int)length + ((int)length & 1);
        }
        return NormalisedAudio.Refuse(Damaged);
    }

    private static NormalisedAudio FromPcm(ReadOnlySpan<byte> data, int format, int channels, int sampleRate, int bits)
    {
        var readable = format switch
        {
            PcmFormat => bits is 8 or 16 or 24 or 32,
            FloatFormat => bits == 32,
            _ => false,
        };
        if (!readable) return NormalisedAudio.Refuse(UnreadableWav);
        if (Unusable(channels, sampleRate) is { } reason) return NormalisedAudio.Refuse(reason);

        var bytesPerSample = bits / 8;
        var frames = data.Length / (bytesPerSample * channels);
        if (WrongLength(frames, sampleRate) is { } wrongLength) return NormalisedAudio.Refuse(wrongLength);

        var wav = NewWav(frames, channels, sampleRate);
        var samples = wav.AsSpan(WavHeaderLength);
        for (var index = 0; index < frames * channels; index++)
        {
            var sample = data.Slice(index * bytesPerSample, bytesPerSample);
            var value = (format, bits) switch
            {
                (FloatFormat, _) => FromFloat(BinaryPrimitives.ReadSingleLittleEndian(sample)),
                // Eight-bit samples are the only ones without a sign: silence is 128.
                (_, 8) => (short)((sample[0] - 128) << 8),
                (_, 16) => BinaryPrimitives.ReadInt16LittleEndian(sample),
                // Of larger samples the most significant sixteen bits are kept.
                (_, 24) => BinaryPrimitives.ReadInt16LittleEndian(sample[1..]),
                _ => BinaryPrimitives.ReadInt16LittleEndian(sample[2..]),
            };
            BinaryPrimitives.WriteInt16LittleEndian(samples[(index * 2)..], value);
        }
        return new NormalisedAudio(wav, DurationMs(frames, sampleRate), sampleRate, channels, null);
    }

    private static NormalisedAudio FromMp3(byte[] bytes, CancellationToken cancellationToken)
    {
        try
        {
            using var source = new MemoryStream(bytes, writable: false);
            using var mpeg = new MpegFile(source);
            int channels = mpeg.Channels, sampleRate = mpeg.SampleRate;
            if (channels == 0 || sampleRate == 0) return NormalisedAudio.Refuse(Damaged);
            if (Unusable(channels, sampleRate) is { } reason) return NormalisedAudio.Refuse(reason);

            // No more is decoded than may be kept: a file that goes on past the limit is refused there.
            var most = (long)VariantAudio.MaxDurationMs * sampleRate / 1000 * channels;
            using var decoded = new MemoryStream();
            var buffer = new float[sampleRate * channels];
            var pair = new byte[2];
            long count = 0;
            int read;
            while ((read = mpeg.ReadSamples(buffer, 0, buffer.Length)) > 0)
            {
                // A second of sound at a time: a member who has stopped waiting is not decoded for.
                cancellationToken.ThrowIfCancellationRequested();
                count += read;
                if (count > most) return NormalisedAudio.Refuse(TooLong);
                for (var index = 0; index < read; index++)
                {
                    BinaryPrimitives.WriteInt16LittleEndian(pair, FromFloat(buffer[index]));
                    decoded.Write(pair);
                }
            }

            var frames = (int)(count / channels);
            if (frames == 0) return NormalisedAudio.Refuse(Damaged);
            if (WrongLength(frames, sampleRate) is { } wrongLength) return NormalisedAudio.Refuse(wrongLength);

            var wav = NewWav(frames, channels, sampleRate);
            decoded.GetBuffer().AsSpan(0, frames * channels * 2).CopyTo(wav.AsSpan(WavHeaderLength));
            return new NormalisedAudio(wav, DurationMs(frames, sampleRate), sampleRate, channels, null);
        }
        catch (Exception exception) when (exception is not (OutOfMemoryException or OperationCanceledException))
        {
            // The decoder gave up on it, in whichever way it does for this kind of damage.
            return NormalisedAudio.Refuse(Damaged);
        }
    }

    private static string? Unusable(int channels, int sampleRate)
    {
        if (channels is not (1 or 2)) return $"The audio has {channels} channels. Upload mono or stereo.";
        if (sampleRate < VariantAudio.MinSampleRate)
        {
            return $"The audio has {sampleRate} samples a second. The fewest allowed is {VariantAudio.MinSampleRate}.";
        }
        if (sampleRate > VariantAudio.MaxSampleRate)
        {
            return $"The audio has {sampleRate} samples a second. The most allowed is {VariantAudio.MaxSampleRate}.";
        }
        return null;
    }

    private static string? WrongLength(int frames, int sampleRate)
    {
        var durationMs = DurationMs(frames, sampleRate);
        if (durationMs > VariantAudio.MaxDurationMs) return TooLong;
        if (durationMs < VariantAudio.MinDurationMs)
        {
            var seconds = (durationMs / 1000.0).ToString("0.##", CultureInfo.InvariantCulture);
            return $"The audio lasts {seconds} seconds. It must last at least {VariantAudio.MinDurationMs / 1000} second.";
        }
        return null;
    }

    private static int DurationMs(int frames, int sampleRate) => (int)Math.Round(frames * 1000.0 / sampleRate);

    private static short FromFloat(float sample) =>
        float.IsNaN(sample) ? (short)0 : (short)Math.Round(Math.Clamp(sample, -1f, 1f) * short.MaxValue);

    // A WAV file with room for this many 16-bit samples after its header.
    private static byte[] NewWav(int frames, int channels, int sampleRate)
    {
        var dataLength = frames * channels * 2;
        var wav = new byte[WavHeaderLength + dataLength];
        var header = wav.AsSpan(0, WavHeaderLength);
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], 36 + dataLength);
        "WAVEfmt "u8.CopyTo(header[8..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], PcmFormat);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], (short)channels);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], sampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], sampleRate * channels * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], (short)(channels * 2));
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], dataLength);
        return wav;
    }
}

/// <summary>Either the WAV to keep, or the reason there is none, in words for the member.</summary>
internal sealed record NormalisedAudio(byte[]? Wav, int DurationMs, int SampleRate, int Channels, string? Refused)
{
    public static NormalisedAudio Refuse(string reason) => new(null, 0, 0, 0, reason);
}
