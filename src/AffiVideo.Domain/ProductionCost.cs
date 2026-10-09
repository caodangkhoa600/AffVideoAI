namespace AffiVideo.Domain;

/// <summary>
/// What one attempt at a render job is estimated to have cost. One is written for
/// every attempt that ends, whether it made the Rendered Video or failed, and
/// nothing about it changes afterwards. It is an estimate from the rates in
/// force at the time, never an amount anyone was billed.
/// </summary>
public sealed class ProductionCostRecord : IOwnedByOrganization
{
    // For the data-access layer, which fills the properties itself.
    private ProductionCostRecord()
    {
    }

    /// <param name="duration">How long the attempt took, from a worker taking the job to the attempt ending.</param>
    /// <param name="rates">The rates in force, which the amount is estimated with.</param>
    public ProductionCostRecord(
        Guid id, Guid organizationId, Guid productId, Guid renderJobId, int attempt, RenderAttemptOutcome outcome,
        RenderProvider provider, IEnumerable<TechniqueCount> techniqueCounts, TimeSpan duration, RenderRates rates,
        DateTimeOffset recordedAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ProductId = productId;
        RenderJobId = renderJobId;
        Attempt = attempt;
        Outcome = outcome;
        Provider = provider;
        TechniqueCounts = [.. techniqueCounts];
        DurationMs = Math.Max(0, (long)duration.TotalMilliseconds);
        // From the duration as it is kept, so that the record's amount follows from the record.
        EstimatedAmount = ProductionCosts.Estimate(rates, TimeSpan.FromMilliseconds(DurationMs));
        Currency = rates.Currency;
        RatesVersion = rates.Version;
        RecordedAt = recordedAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The Product the video was being rendered for. The record stays with it whatever becomes of the video.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>The job the attempt was at. Absent once the job has been deleted with its Project.</summary>
    public Guid? RenderJobId { get; private set; }

    /// <summary>Which taking of the job this was, counted from one.</summary>
    public int Attempt { get; private set; }

    public RenderAttemptOutcome Outcome { get; private set; }

    /// <summary>What rendered.</summary>
    public RenderProvider Provider { get; private set; }

    /// <summary>How many Scenes of the Storyboard version the attempt was rendering are made by each Technique.</summary>
    public IReadOnlyList<TechniqueCount> TechniqueCounts { get; private set; } = [];

    /// <summary>How long the attempt took, in milliseconds.</summary>
    public long DurationMs { get; private set; }

    /// <summary>What the attempt is estimated to have cost. Nobody was billed this.</summary>
    public decimal EstimatedAmount { get; private set; }

    public string Currency { get; private set; } = "";

    /// <summary>The <see cref="RenderRates.Version"/> of the rates the amount was estimated with.</summary>
    public string RatesVersion { get; private set; } = "";

    public DateTimeOffset RecordedAt { get; private set; }
}

/// <summary>How an attempt at a render job ended.</summary>
public enum RenderAttemptOutcome
{
    /// <summary>It made the Rendered Video.</summary>
    Completed,

    /// <summary>It made nothing: the render went wrong, or the worker stopped while it had the job.</summary>
    Failed,
}

/// <summary>What renders a video.</summary>
public enum RenderProvider
{
    /// <summary>The worker itself, with Remotion and FFmpeg, on the Organization's own machine.</summary>
    Local,
}

/// <param name="Scenes">How many Scenes are made by the Technique.</param>
public sealed record TechniqueCount(Technique Technique, int Scenes);

/// <summary>An amount of money that was estimated, not billed.</summary>
public sealed record EstimatedAmount(decimal Amount, string Currency);

/// <summary>
/// What a provider's rendering is taken to cost. The rates are configuration:
/// whoever changes an amount changes the version too, so that every record says
/// which rates it was estimated with.
/// </summary>
/// <param name="Version">Names this set of rates.</param>
/// <param name="Currency">The three-letter code of the currency both amounts are in.</param>
/// <param name="PerAttempt">For each attempt, however long it takes.</param>
/// <param name="PerMinute">For each minute an attempt takes, and in proportion for part of one.</param>
public sealed record RenderRates(string Version, string Currency, decimal PerAttempt, decimal PerMinute)
{
    public const int VersionMaxLength = 50;

    /// <summary>
    /// The most a rate may be. An estimate is kept with twelve digits before the point, and
    /// at this rate for a minute an attempt would have to last over a year to need more.
    /// </summary>
    public const decimal MaxAmount = 1_000_000m;

    /// <summary>Why these cannot be used as rates, in words for whoever configured them. Null when they can.</summary>
    public string? Problem =>
        string.IsNullOrWhiteSpace(Version) || Version.Length > VersionMaxLength
            ? $"The version of the rates is a name of at most {VersionMaxLength} characters."
        : Currency.Length != Product.CurrencyLength || !Currency.All(letter => letter is >= 'A' and <= 'Z')
            ? "The currency of the rates is a three-letter code in capitals, such as USD or VND."
        : PerAttempt is < 0 or > MaxAmount || PerMinute is < 0 or > MaxAmount
            ? $"A rate is an amount from zero to {MaxAmount:0}."
        : null;
}

public static class ProductionCosts
{
    /// <summary>How many decimals an estimated amount is kept to: a rate for a minute may be a small part of a cent.</summary>
    public const int AmountDecimals = 6;

    public const int AmountPrecision = 18;

    /// <summary>
    /// What one attempt is estimated to have cost: the rate for an attempt, and the
    /// rate for a minute for as long as it took.
    /// </summary>
    public static decimal Estimate(RenderRates rates, TimeSpan duration)
    {
        var minutes = Math.Max(0, duration.Ticks) / (decimal)TimeSpan.TicksPerMinute;
        return Math.Round(rates.PerAttempt + rates.PerMinute * minutes, AmountDecimals, MidpointRounding.AwayFromZero);
    }

    /// <summary>
    /// The records added up, one total for each currency they are in, by currency
    /// code. Amounts in different currencies are never added to each other, and no
    /// records make no total.
    /// </summary>
    public static IReadOnlyList<EstimatedAmount> Totals(IEnumerable<ProductionCostRecord> records) =>
        [.. records
            .GroupBy(record => record.Currency)
            .OrderBy(currency => currency.Key, StringComparer.Ordinal)
            .Select(currency => new EstimatedAmount(currency.Sum(record => record.EstimatedAmount), currency.Key))];

    /// <summary>How many of the Scenes are made by each Technique, in the order the Techniques are listed.</summary>
    public static IReadOnlyList<TechniqueCount> CountTechniques(IEnumerable<Scene> scenes) =>
        [.. scenes
            .GroupBy(scene => scene.Technique)
            .OrderBy(technique => technique.Key)
            .Select(technique => new TechniqueCount(technique.Key, technique.Count()))];
}
