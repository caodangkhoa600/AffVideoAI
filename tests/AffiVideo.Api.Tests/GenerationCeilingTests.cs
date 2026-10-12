using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>The ceiling on a Project's generation as a pure function: whether a generation may go ahead.</summary>
public sealed class GenerationCeilingTests
{
    [Theory]
    [InlineData(0, 0.04, 5)]
    // Reaching the ceiling is not passing it.
    [InlineData(4.96, 0.04, 5)]
    public void A_generation_that_stays_within_the_ceiling_goes_ahead(decimal spent, decimal asked, decimal ceiling)
    {
        Assert.Null(GenerationCeilings.Refusal(spent, asked, ceiling, "USD"));
    }

    [Fact]
    public void A_generation_that_would_pass_the_ceiling_is_refused_with_what_was_spent_what_was_asked_and_the_ceiling()
    {
        var reason = GenerationCeilings.Refusal(spent: 4.97m, asked: 0.04m, ceiling: 5m, "USD");

        Assert.NotNull(reason);
        Assert.Contains("5.01 USD", reason);
        Assert.Contains("ceiling of 5.00 USD", reason);
        Assert.Contains("4.97 USD", reason);
        Assert.Contains("0.04 USD", reason);
    }

    [Fact]
    public void With_a_ceiling_of_nothing_every_paid_generation_is_refused()
    {
        Assert.NotNull(GenerationCeilings.Refusal(spent: 0m, asked: 0.04m, ceiling: 0m, "USD"));
    }
}
