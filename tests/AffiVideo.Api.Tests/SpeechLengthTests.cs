using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>How long narration text is estimated to take to say, before any of it is spoken, as a pure function.</summary>
public sealed class SpeechLengthTests
{
    // Vietnamese as it is paced unless configured otherwise: four words a second, each word being one syllable.
    private static readonly SpeakingPace Vietnamese = new(WordsPerSecond: 4, SentencePauseMs: 300, ClausePauseMs: 150);

    [Theory]
    // Six words at four a second. The full stop that ends the text is followed by nothing, so it adds no pause.
    [InlineData("Pin dùng liên tục 30 giờ.", 1500)]
    // Two sentences: ten words, and one pause between them.
    [InlineData("Lumo 500. Xem chi tiết sản phẩm ngay hôm nay.", 2800)]
    // A comma is a shorter pause than a full stop.
    [InlineData("Nhẹ, bền, dễ mang theo!", 1250 + 150 + 150)]
    // A question and its answer.
    [InlineData("Bạn vẫn dùng bình nhựa? Đây là Lumo 500", 2250 + 300)]
    [InlineData("", 0)]
    [InlineData("   ", 0)]
    public void Narration_text_takes_its_words_at_the_pace_of_its_language_and_a_pause_at_each_mark_inside_it(string text, int milliseconds)
    {
        Assert.Equal(milliseconds, SpeechLength.EstimateMs(text, Vietnamese));
    }

    [Theory]
    // Speech that ends before its Scene does is played as it is.
    [InlineData(1500, 7000, 1.0)]
    [InlineData(3000, 3000, 1.0)]
    // Speech a twentieth too long is played a twentieth faster, and a tenth too long a tenth faster.
    [InlineData(3150, 3000, 1.05)]
    [InlineData(3300, 3000, 1.1)]
    public void Speech_longer_than_its_Scene_is_sped_up_just_enough_to_end_with_it(int speechMs, int sceneMs, double tempo)
    {
        Assert.Equal(tempo, SpeechFit.TempoFor(speechMs, sceneMs)!.Value, precision: 6);
    }

    [Theory]
    [InlineData(3301, 3000)]
    [InlineData(5000, 3000)]
    public void Speech_that_would_have_to_be_sped_up_by_more_than_a_tenth_does_not_fit(int speechMs, int sceneMs)
    {
        Assert.Null(SpeechFit.TempoFor(speechMs, sceneMs));
    }

    [Fact]
    public void A_slower_language_takes_longer_to_say_the_same_number_of_words()
    {
        var english = new SpeakingPace(WordsPerSecond: 2.5, SentencePauseMs: 300, ClausePauseMs: 150);

        Assert.Equal(2000, SpeechLength.EstimateMs("Thirty hours on one charge", english));
    }
}
