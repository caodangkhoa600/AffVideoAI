using System.Buffers.Binary;
using AffiVideo.Application.Providers;
using AffiVideo.Domain;

namespace AffiVideo.Infrastructure.Generation;

// The fake speech provider. It calls nobody and says nothing: what it makes is a
// steady tone that lasts as long as the text is estimated to take to say, so that
// everything that depends on how long speech is can be seen working. Nobody takes
// it for a voice, and what it made is labelled as fake wherever it is shown.
internal sealed class FakeTextToSpeech(GenerationOptions generation) : ITextToSpeech
{
    public const string Model = "fake-speech";

    private const int SampleRate = 48000;
    private const int WavHeaderLength = 44;
    private const double ToneHz = 220;

    // A third of full scale: loud enough to measure, and nowhere near clipping.
    private const double Amplitude = short.MaxValue / 3.0;

    // The tone comes and goes over this long, so that it does not click.
    private const int FadeMs = 20;

    private int _asked;

    public SpeechProvider Provider => SpeechProvider.Fake;

    // Every voice is the same tone: the fake has none of its own.
    public async Task<SpeechGeneration> SpeakAsync(string text, string language, string voice, CancellationToken cancellationToken)
    {
        var options = generation.FakeSpeech;
        if (options.DelaySeconds > 0) await Task.Delay(TimeSpan.FromSeconds(options.DelaySeconds), cancellationToken);

        // It misbehaves only as it is configured to, so that what a member is shown then can be seen.
        if (options.Refuses)
        {
            return new SpeechGeneration.Refused("The fake speech provider refuses every text, as it is configured to.", options.BillsFailures);
        }
        if (Interlocked.Increment(ref _asked) <= options.TechnicalFailures)
        {
            return new SpeechGeneration.Failed("The fake speech provider failed, as it is configured to.", options.BillsFailures);
        }

        var durationMs = SpeechLength.EstimateMs(text, generation.PaceOf(language));
        return new SpeechGeneration.Made(Tone(durationMs), durationMs, Model);
    }

    // Mono 16-bit PCM in a WAV file.
    private static byte[] Tone(int durationMs)
    {
        var frames = (int)((long)durationMs * SampleRate / 1000);
        var fade = FadeMs * SampleRate / 1000;
        var wav = new byte[WavHeaderLength + frames * 2];
        var header = wav.AsSpan();
        "RIFF"u8.CopyTo(header);
        BinaryPrimitives.WriteInt32LittleEndian(header[4..], wav.Length - 8);
        "WAVE"u8.CopyTo(header[8..]);
        "fmt "u8.CopyTo(header[12..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[16..], 16);
        BinaryPrimitives.WriteInt16LittleEndian(header[20..], 1);
        BinaryPrimitives.WriteInt16LittleEndian(header[22..], 1);
        BinaryPrimitives.WriteInt32LittleEndian(header[24..], SampleRate);
        BinaryPrimitives.WriteInt32LittleEndian(header[28..], SampleRate * 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[32..], 2);
        BinaryPrimitives.WriteInt16LittleEndian(header[34..], 16);
        "data"u8.CopyTo(header[36..]);
        BinaryPrimitives.WriteInt32LittleEndian(header[40..], frames * 2);
        for (var frame = 0; frame < frames; frame++)
        {
            var envelope = Math.Min(1.0, Math.Min(frame, frames - 1 - frame) / (double)Math.Max(1, fade));
            var sample = (short)(Amplitude * envelope * Math.Sin(2 * Math.PI * ToneHz * frame / SampleRate));
            BinaryPrimitives.WriteInt16LittleEndian(header[(WavHeaderLength + frame * 2)..], sample);
        }
        return wav;
    }
}

// What the worker has when no speech provider is configured. Speech that reaches it was
// confirmed by an API that has one: the two are configured differently, and the member is told.
internal sealed class NoTextToSpeech : ITextToSpeech
{
    public SpeechProvider Provider => SpeechProvider.None;

    public Task<SpeechGeneration> SpeakAsync(string text, string language, string voice, CancellationToken cancellationToken) =>
        Task.FromResult<SpeechGeneration>(new SpeechGeneration.Failed("The worker has no speech provider configured.", Billed: false));
}
