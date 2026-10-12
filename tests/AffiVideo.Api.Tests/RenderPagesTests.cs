using AffiVideo.Worker.Rendering;

namespace AffiVideo.Api.Tests;

/// <summary>How many pages one Remotion render opens, as a pure function of the machine and what the worker runs at once.</summary>
public sealed class RenderPagesTests
{
    [Theory]
    // One job of two Scenes on fourteen cores: half the cores, shared between the two renders.
    [InlineData(14, 1, 2, 3)]
    // Two jobs of two Scenes: four renders at once share the same seven pages, one each.
    [InlineData(14, 2, 2, 1)]
    [InlineData(8, 1, 1, 4)]
    // A render always has a page to draw on, however small the machine or however many run at once.
    [InlineData(2, 1, 2, 1)]
    [InlineData(1, 4, 4, 1)]
    public void The_renders_a_worker_runs_at_once_share_half_the_machines_cores_between_them(
        int cores, int jobsAtOnce, int scenesAtOnce, int pages)
    {
        var options = new RenderingOptions { JobsAtOnce = jobsAtOnce, ScenesAtOnce = scenesAtOnce };

        Assert.Equal(pages, options.PagesForEachRender(cores));
    }
}
