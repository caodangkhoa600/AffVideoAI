namespace AffiVideo.Domain;

/// <summary>
/// One Scene's narration text, spoken. It is asked for with the text and its
/// language, waits in the database until a worker has a speech provider say it,
/// and is then used as it is: nobody approves it, and a member who wants it said
/// otherwise changes the text. It belongs to the Project it was made for, and
/// every Scene of that Project with the same narration text has the same speech.
/// </summary>
public sealed class Speech : IOwnedByOrganization
{
    /// <summary>What every stored one is: 16-bit PCM in a WAV file.</summary>
    public const string ContentType = "audio/wav";

    // For the data-access layer, which fills the properties itself.
    private Speech()
    {
    }

    /// <summary>Speech is queued as soon as it is asked for, and may be taken at once.</summary>
    /// <param name="language">The language of the text, as an ISO 639-1 code.</param>
    /// <param name="amount">What saying it is estimated to cost, from the rates in force as it is asked for.</param>
    /// <param name="ordinal">Which of the pieces of speech asked for with this idempotency key it is, counted from one.</param>
    public Speech(
        Guid id, Guid organizationId, Guid projectId, Guid productId, string text, string language, string voice, SpeechProvider provider,
        EstimatedAmount amount, string ratesVersion, string idempotencyKey, int ordinal, Guid requestedByMemberId, DateTimeOffset createdAt)
    {
        Id = id;
        OrganizationId = organizationId;
        ProjectId = projectId;
        ProductId = productId;
        Text = text;
        Language = language;
        Voice = voice;
        Provider = provider;
        EstimatedAmount = amount.Amount;
        Currency = amount.Currency;
        RatesVersion = ratesVersion;
        IdempotencyKey = idempotencyKey;
        Ordinal = ordinal;
        RequestedByMemberId = requestedByMemberId;
        State = SpeechState.Queued;
        AvailableAt = createdAt;
        CreatedAt = createdAt;
        UpdatedAt = createdAt;
    }

    public Guid Id { get; private set; }

    public Guid OrganizationId { get; private set; }

    /// <summary>The Project it was made for. Absent once that Project has been deleted: the speech is kept.</summary>
    public Guid? ProjectId { get; private set; }

    /// <summary>The Product of that Project, which the cost of the speech is recorded against.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>What is said: a Scene's narration text, as the provider is told.</summary>
    public string Text { get; private set; } = "";

    /// <summary>The language of the text, as an ISO 639-1 code.</summary>
    public string Language { get; private set; } = "";

    /// <summary>The voice it is said in, by its id in the list of voices on offer.</summary>
    public string Voice { get; private set; } = "";

    /// <summary>What says it, fixed as it is asked for.</summary>
    public SpeechProvider Provider { get; private set; }

    /// <summary>The provider's model that said it. Absent until it is made.</summary>
    public string? Model { get; private set; }

    public SpeechState State { get; private set; }

    /// <summary>What saying it is estimated to cost. Nobody was billed this.</summary>
    public decimal EstimatedAmount { get; private set; }

    public string Currency { get; private set; } = "";

    /// <summary>The version of the rates the amount was estimated with.</summary>
    public string RatesVersion { get; private set; } = "";

    /// <summary>What the member's click was submitted with, so that a request sent twice asks for the speech once.</summary>
    public string IdempotencyKey { get; private set; } = "";

    public int Ordinal { get; private set; }

    public Guid RequestedByMemberId { get; private set; }

    /// <summary>How many times a worker has taken it.</summary>
    public int Attempt { get; private set; }

    public DateTimeOffset? AttemptStartedAt { get; private set; }

    /// <summary>The earliest a worker may take it while it is queued: later than now when it waits to be tried again.</summary>
    public DateTimeOffset AvailableAt { get; private set; }

    /// <summary>When speech that waits to be tried again may next be taken. Absent for any other.</summary>
    public DateTimeOffset? RetryAt => State == SpeechState.Queued && Attempt > 0 ? AvailableAt : null;

    /// <summary>Names the one taking that may still write to it, as a Render job's does.</summary>
    public Guid? LeaseId { get; private set; }

    public DateTimeOffset? LeaseExpiresAt { get; private set; }

    public SpeechFailureKind? FailureKind { get; private set; }

    /// <summary>Why it was not made, in words for the member.</summary>
    public string? FailureMessage { get; private set; }

    /// <summary>What went wrong as the provider or the program reported it.</summary>
    public string? FailureDetail { get; private set; }

    /// <summary>
    /// Whether the provider is taken to have charged for it. Speech that was made was charged for;
    /// speech that failed was only if the provider says so.
    /// </summary>
    public bool Billed { get; private set; }

    /// <summary>How long the stored sound lasts, in milliseconds. Zero until it is made.</summary>
    public int DurationMs { get; private set; }

    /// <summary>Of the stored WAV. Zero until it is made.</summary>
    public long SizeInBytes { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    /// <summary>When the state last changed.</summary>
    public DateTimeOffset UpdatedAt { get; private set; }

    /// <summary>Where the sound is kept, under its Organization.</summary>
    public string StorageKey => $"organizations/{OrganizationId}/speech/{Id}.wav";

    /// <summary>Whether a fake provider made it, or will: it is then a tone and nobody's voice, and is labelled so.</summary>
    public bool Fake => Provider == SpeechProvider.Fake;

    /// <summary>Whether a Scene that says this text has it: it is on its way, or made.</summary>
    public bool IsCurrent => State is SpeechState.Queued or SpeechState.Generating or SpeechState.Made;

    /// <summary>Whether its sound was made and stored.</summary>
    public bool HasSound => State == SpeechState.Made;

    /// <summary>Whether its amount counts toward what the Project has spent: everything but a failure the provider did not charge for.</summary>
    public bool CountsAsSpent => State != SpeechState.Failed || Billed;

    /// <summary>Whether this is the Scene's narration text, said in this language and this voice.</summary>
    public bool Says(Scene scene, string language, string voice) => Text == scene.NarrationText && Language == language && Voice == voice;

    /// <summary>The sound is made and stored: it is used as it is.</summary>
    /// <returns>False, with nothing changed, unless a worker was making it.</returns>
    public bool Made(string model, int durationMs, long sizeInBytes, DateTimeOffset now)
    {
        if (!Become(SpeechState.Made, now)) return false;
        Model = model;
        DurationMs = durationMs;
        SizeInBytes = sizeInBytes;
        Billed = true;
        return true;
    }

    /// <summary>Puts speech that a worker was making back in the queue, to be taken again.</summary>
    /// <returns>False, with nothing changed, unless a worker was making it.</returns>
    public bool Requeue(DateTimeOffset availableAt, DateTimeOffset now)
    {
        if (!Become(SpeechState.Queued, now)) return false;
        AvailableAt = availableAt;
        return true;
    }

    /// <returns>False, with nothing changed, unless it was queued or being made.</returns>
    public bool Fail(SpeechFailureKind kind, string message, string? detail, bool billed, DateTimeOffset now)
    {
        if (!Become(SpeechState.Failed, now)) return false;
        FailureKind = kind;
        FailureMessage = Shortened(message, RenderJob.FailureMessageMaxLength);
        FailureDetail = detail is null ? null : Shortened(detail, RenderJob.FailureDetailMaxLength);
        Billed = billed;
        return true;
    }

    private bool Become(SpeechState state, DateTimeOffset now)
    {
        if (!State.CanBecome(state)) return false;
        State = state;
        UpdatedAt = now;
        // Only speech that a worker is making is anyone's.
        if (state != SpeechState.Generating)
        {
            LeaseId = null;
            LeaseExpiresAt = null;
        }
        return true;
    }

    private static string Shortened(string text, int length) => text.Length > length ? text[..length] : text;
}

/// <summary>Where a piece of speech is.</summary>
public enum SpeechState
{
    /// <summary>Waiting for a worker.</summary>
    Queued,

    /// <summary>A worker has asked the provider for it.</summary>
    Generating,

    /// <summary>Made: it is used as it is.</summary>
    Made,

    /// <summary>It was not made.</summary>
    Failed,
}

/// <summary>Why speech was not made, which decides what the member can do about it.</summary>
public enum SpeechFailureKind
{
    /// <summary>The provider would not say this text. Change the narration text.</summary>
    Refused,

    /// <summary>Something went wrong at the provider or on the way to it, as often as speech is tried. Ask again.</summary>
    Technical,

    /// <summary>The worker stopped while it was being made. Whether the provider charged is not known, so it is taken to have.</summary>
    WorkerLost,
}

public static class SpeechStates
{
    public static bool CanBecome(this SpeechState from, SpeechState to) => (from, to) switch
    {
        (SpeechState.Queued, SpeechState.Generating) => true,
        (SpeechState.Generating, SpeechState.Queued or SpeechState.Made) => true,
        (SpeechState.Queued or SpeechState.Generating, SpeechState.Failed) => true,
        _ => false,
    };
}
