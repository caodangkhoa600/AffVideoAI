using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>The states of a render job as a pure function, with no API, database or worker.</summary>
public sealed class RenderJobStateTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 9, 8, 0, 0, TimeSpan.Zero);

    private static readonly RenderJobState[] All = Enum.GetValues<RenderJobState>();

    private static readonly (RenderJobState From, RenderJobState To)[] Forward =
    [
        (RenderJobState.Created, RenderJobState.Queued),
        (RenderJobState.Queued, RenderJobState.Validating),
        (RenderJobState.Validating, RenderJobState.Planning),
        (RenderJobState.Planning, RenderJobState.GeneratingAssets),
        (RenderJobState.GeneratingAssets, RenderJobState.GeneratingVideo),
        // Product Lock generates no video, so it goes straight on to rendering.
        (RenderJobState.GeneratingAssets, RenderJobState.Rendering),
        (RenderJobState.GeneratingVideo, RenderJobState.Rendering),
        (RenderJobState.Rendering, RenderJobState.QualityReview),
        (RenderJobState.QualityReview, RenderJobState.Completed),
    ];

    public static TheoryData<RenderJobState, RenderJobState, bool> EveryChange()
    {
        var data = new TheoryData<RenderJobState, RenderJobState, bool>();
        foreach (var from in All)
        {
            foreach (var to in All)
            {
                var ended = from is RenderJobState.Completed or RenderJobState.Failed or RenderJobState.Cancelled;
                var stops = to is RenderJobState.Failed or RenderJobState.Cancelled;
                data.Add(from, to, Forward.Contains((from, to)) || (!ended && stops));
            }
        }
        return data;
    }

    [Theory]
    [MemberData(nameof(EveryChange))]
    public void A_job_only_moves_to_its_next_stage_or_stops(RenderJobState from, RenderJobState to, bool allowed)
    {
        Assert.Equal(allowed, from.CanBecome(to));
    }

    [Fact]
    public void A_new_job_is_queued_and_a_refused_change_leaves_it_exactly_as_it_was()
    {
        var job = new RenderJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.Equal(RenderJobState.Queued, job.State);

        Assert.False(job.MoveTo(RenderJobState.Rendering, Now.AddMinutes(1)));

        Assert.Equal(RenderJobState.Queued, job.State);
        Assert.Equal(Now, job.UpdatedAt);
    }

    [Fact]
    public void A_job_that_failed_keeps_its_reason_and_goes_nowhere_else()
    {
        var job = new RenderJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.True(job.MoveTo(RenderJobState.Validating, Now.AddSeconds(1)));

        Assert.True(job.Fail("A photo is gone.", Now.AddSeconds(2)));

        Assert.Equal(RenderJobState.Failed, job.State);
        Assert.Equal("A photo is gone.", job.FailureReason);
        Assert.Equal(Now.AddSeconds(2), job.UpdatedAt);
        Assert.False(job.MoveTo(RenderJobState.Planning, Now.AddSeconds(3)));
        Assert.False(job.Fail("Something else.", Now.AddSeconds(3)));
        Assert.Equal("A photo is gone.", job.FailureReason);
    }

    [Fact]
    public void A_job_is_only_completed_from_quality_review_and_then_names_its_Rendered_Video()
    {
        var video = Guid.NewGuid();
        var job = new RenderJob(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Now);
        Assert.False(job.Complete(video, Now));
        Assert.Null(job.RenderedVideoId);

        foreach (var stage in new[]
        {
            RenderJobState.Validating, RenderJobState.Planning, RenderJobState.GeneratingAssets,
            RenderJobState.Rendering, RenderJobState.QualityReview,
        })
        {
            Assert.True(job.MoveTo(stage, Now));
        }
        // Completing is the only way to the end: a job is never completed without its video.
        Assert.False(job.MoveTo(RenderJobState.Completed, Now));
        Assert.True(job.Complete(video, Now.AddMinutes(1)));

        Assert.Equal(RenderJobState.Completed, job.State);
        Assert.Equal(video, job.RenderedVideoId);
        Assert.False(job.Fail("Too late.", Now.AddMinutes(2)));
    }
}
