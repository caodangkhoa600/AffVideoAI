using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>How many Facts Product Showcase shows and how long each may be, as a pure function.</summary>
public sealed class FactPacingTests
{
    private static readonly CreativeTemplateDefinition ProductShowcase = CreativeTemplates.Find(CreativeTemplate.ProductShowcase)!;

    [Theory]
    // 20 seconds: the Facts Scene lasts 7, less 0.7 for the panel to arrive, so three Facts get 2.1 seconds
    // each. 1.2 of that is not words arriving, and 0.9 seconds is fifteen steps of 0.06 after the first word.
    [InlineData(20, 3, 16)]
    // 15 seconds: 5.2 less 0.7 is 1.5 seconds each, leaving 0.3: five steps after the first word.
    [InlineData(15, 3, 6)]
    [InlineData(15, 2, 18)]
    [InlineData(15, 1, 56)]
    // 30 seconds: 10.5 less 0.7 is 3.266 seconds each, leaving 2.066: thirty-four steps.
    [InlineData(30, 3, 35)]
    public void A_Fact_may_have_as_many_words_as_arrive_and_are_held_before_it_leaves(int seconds, int facts, int words)
    {
        Assert.Equal(words, ProductShowcase.MaxFactWords(seconds, facts));
    }

    [Fact]
    public void Facts_given_too_little_time_for_any_word_may_have_none()
    {
        var crowded = ProductShowcase with { MaxFacts = 10 };

        Assert.Equal(0, crowded.MaxFactWords(targetDurationSeconds: 15, factCount: 10));
    }

    [Theory]
    // Three Facts of six words fit a 15-second video; of seven words, only two of them do.
    [InlineData(15, 6, 3)]
    [InlineData(15, 7, 2)]
    [InlineData(15, 18, 2)]
    [InlineData(15, 19, 1)]
    [InlineData(15, 56, 1)]
    [InlineData(15, 57, 0)]
    [InlineData(20, 16, 3)]
    [InlineData(20, 17, 2)]
    public void Fewer_Facts_are_shown_when_that_is_what_it_takes_for_each_to_be_read(int seconds, int wordsInEachFact, int shown)
    {
        var fact = string.Join(" ", Enumerable.Repeat("pin", wordsInEachFact));

        Assert.Equal(shown, ProductShowcase.FactsShown([fact, fact, fact, fact], seconds));
    }

    [Fact]
    public void Facts_are_taken_oldest_first_and_never_more_than_there_are_or_than_the_template_shows()
    {
        Assert.Equal(3, ProductShowcase.FactsShown(["một", "hai", "ba", "bốn"], targetDurationSeconds: 20));
        Assert.Equal(2, ProductShowcase.FactsShown(["một", "hai"], targetDurationSeconds: 20));
        Assert.Equal(0, ProductShowcase.FactsShown([], targetDurationSeconds: 20));
        // The long Fact is the third: it is left out. Were it the first, it would be the one shown.
        var longer = string.Join(" ", Enumerable.Repeat("pin", 17));
        Assert.Equal(2, ProductShowcase.FactsShown(["một", "hai", longer], targetDurationSeconds: 20));
        Assert.Equal(2, ProductShowcase.FactsShown([longer, "hai", "ba"], targetDurationSeconds: 20));
    }
}
