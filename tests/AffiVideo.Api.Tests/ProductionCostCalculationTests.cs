using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>What a render attempt is estimated to have cost, and what several add up to, as pure functions.</summary>
public sealed class ProductionCostCalculationTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 10, 9, 0, 0, TimeSpan.Zero);

    [Theory]
    // Local rendering costs nothing until someone says what it costs.
    [InlineData("0", "0", 90_000, "0")]
    // An amount for each attempt, however long it took.
    [InlineData("0.01", "0", 0, "0.01")]
    [InlineData("0.01", "0", 90_000, "0.01")]
    // An amount for each minute the attempt took, and for the part of a minute.
    [InlineData("0", "0.06", 60_000, "0.06")]
    [InlineData("0", "0.06", 90_000, "0.09")]
    [InlineData("0", "0.06", 1_000, "0.001")]
    // Both together.
    [InlineData("0.01", "0.06", 90_000, "0.10")]
    // Kept to six decimals, the half going up.
    [InlineData("0", "0.001", 500, "0.000008")]
    [InlineData("0", "0.001", 30, "0.000001")]
    [InlineData("0", "0.001", 29, "0")]
    public void An_attempt_is_estimated_at_the_rate_for_an_attempt_and_the_rate_for_each_minute_it_took(
        string perAttempt, string perMinute, int tookMs, string expected)
    {
        var rates = new RenderRates("test", "USD", Amount(perAttempt), Amount(perMinute));

        Assert.Equal(Amount(expected), ProductionCosts.Estimate(rates, TimeSpan.FromMilliseconds(tookMs)));
    }

    [Fact]
    public void An_attempt_that_seems_to_have_ended_before_it_began_is_estimated_as_taking_no_time()
    {
        var rates = new RenderRates("test", "USD", 0.01m, 0.06m);

        Assert.Equal(0.01m, ProductionCosts.Estimate(rates, TimeSpan.FromMinutes(-5)));
    }

    [Theory]
    [InlineData("", "USD", "0", "0")]
    [InlineData("  ", "USD", "0", "0")]
    [InlineData("1", "usd", "0", "0")]
    [InlineData("1", "US", "0", "0")]
    [InlineData("1", "USDD", "0", "0")]
    [InlineData("1", "USD", "-0.01", "0")]
    [InlineData("1", "USD", "0", "-0.01")]
    // More than an estimate could be kept at.
    [InlineData("1", "USD", "1000000.01", "0")]
    [InlineData("1", "USD", "0", "1000000.01")]
    [InlineData("a-version-named-at-greater-length-than-fifty-characters", "USD", "0", "0")]
    public void Rates_without_a_version_a_currency_code_or_with_an_amount_below_nothing_or_above_the_limit_are_not_rates(
        string version, string currency, string perAttempt, string perMinute)
    {
        Assert.NotNull(new RenderRates(version, currency, Amount(perAttempt), Amount(perMinute)).Problem);
    }

    [Fact]
    public void The_rates_local_rendering_has_unless_it_is_configured_are_zero_and_are_rates()
    {
        Assert.Null(new RenderRates("1", "USD", 0, 0).Problem);
        Assert.Null(new RenderRates("2026-10", "VND", 250, 1200.5m).Problem);
        Assert.Null(new RenderRates("2026-10", "VND", 1_000_000, 1_000_000).Problem);
    }

    [Fact]
    public void A_record_keeps_what_it_was_estimated_with_and_what_the_attempt_rendered()
    {
        var rates = new RenderRates("2026-10", "VND", 250, 1200);
        Scene[] scenes =
        [
            NewScene(1, Technique.TextAnimation), NewScene(2, Technique.ImageMotion),
            NewScene(3, Technique.ImageMotion), NewScene(4, Technique.StaticImage),
        ];
        var product = Guid.NewGuid();
        var job = Guid.NewGuid();

        var record = new ProductionCostRecord(
            Guid.NewGuid(), Guid.NewGuid(), product, job, attempt: 2, RenderAttemptOutcome.Failed, RenderProvider.Local,
            ProductionCosts.CountTechniques(scenes), TimeSpan.FromSeconds(30), rates, Now);

        Assert.Equal((product, job, 2), (record.ProductId, record.RenderJobId, record.Attempt));
        Assert.Equal((RenderAttemptOutcome.Failed, RenderProvider.Local), (record.Outcome, record.Provider));
        Assert.Equal(30_000, record.DurationMs);
        Assert.Equal((850m, "VND", "2026-10"), (record.EstimatedAmount, record.Currency, record.RatesVersion));
        // In the order the Techniques are listed, each with the number of Scenes made by it.
        Assert.Equal(
            [new TechniqueCount(Technique.StaticImage, 1), new TechniqueCount(Technique.ImageMotion, 2), new TechniqueCount(Technique.TextAnimation, 1)],
            record.TechniqueCounts);
    }

    [Fact]
    public void Records_add_up_to_one_estimated_total_for_each_currency_they_are_in()
    {
        var dollars = new RenderRates("1", "USD", 0.01m, 0);
        var dong = new RenderRates("2", "VND", 250, 0);

        var totals = ProductionCosts.Totals([Record(dong), Record(dollars), Record(dollars), Record(dong), Record(dollars)]);

        // Amounts in different currencies are never added to each other.
        Assert.Equal([new EstimatedAmount(0.03m, "USD"), new EstimatedAmount(500m, "VND")], totals);
    }

    [Fact]
    public void No_records_add_up_to_no_total_rather_than_to_a_total_of_nothing()
    {
        Assert.Empty(ProductionCosts.Totals([]));
    }

    [Fact]
    public void Records_estimated_at_nothing_still_add_up_to_a_total_of_nothing_in_their_currency()
    {
        var free = new RenderRates("1", "USD", 0, 0);

        Assert.Equal([new EstimatedAmount(0m, "USD")], ProductionCosts.Totals([Record(free), Record(free)]));
    }

    private static ProductionCostRecord Record(RenderRates rates) => new(
        Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), attempt: 1, RenderAttemptOutcome.Completed, RenderProvider.Local,
        [], TimeSpan.FromSeconds(40), rates, Now);

    private static Scene NewScene(int position, Technique technique) => new(
        position, SceneLayout.Hook, technique, 3000, ["Chữ"], "", [], []);

    private static decimal Amount(string written) => decimal.Parse(written, System.Globalization.CultureInfo.InvariantCulture);
}
