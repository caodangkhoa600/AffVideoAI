using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>The states of a Fact as a pure function, with no API or database.</summary>
public sealed class FactStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(FactState.Proposed, FactState.Proposed, false)]
    [InlineData(FactState.Proposed, FactState.Confirmed, true)]
    [InlineData(FactState.Proposed, FactState.Withdrawn, true)]
    [InlineData(FactState.Confirmed, FactState.Proposed, false)]
    [InlineData(FactState.Confirmed, FactState.Confirmed, false)]
    [InlineData(FactState.Confirmed, FactState.Withdrawn, true)]
    [InlineData(FactState.Withdrawn, FactState.Proposed, false)]
    [InlineData(FactState.Withdrawn, FactState.Confirmed, false)]
    [InlineData(FactState.Withdrawn, FactState.Withdrawn, false)]
    public void Only_three_changes_of_state_are_allowed(FactState from, FactState to, bool allowed)
    {
        Assert.Equal(allowed, from.CanBecome(to));
    }

    [Fact]
    public void A_refused_change_leaves_the_Fact_exactly_as_it_was()
    {
        var first = Guid.NewGuid();
        var fact = NewFact();
        Assert.True(fact.Confirm(first, Now));

        var again = fact.Confirm(Guid.NewGuid(), Now.AddHours(1));

        Assert.False(again);
        Assert.Equal(first, fact.ConfirmedByMemberId);
        Assert.Equal(Now, fact.ConfirmedAt);

        Assert.True(fact.Withdraw(Now.AddHours(2)));
        Assert.False(fact.Withdraw(Now.AddHours(3)));
        Assert.False(fact.Confirm(Guid.NewGuid(), Now.AddHours(4)));
        Assert.Equal(FactState.Withdrawn, fact.State);
        Assert.Equal(Now.AddHours(2), fact.WithdrawnAt);
        Assert.Equal(first, fact.ConfirmedByMemberId);
    }

    private static Fact NewFact() =>
        new(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), new FactDetails("Pin 30 giờ", "vi", null), Now);
}
