using AffiVideo.Domain;

namespace AffiVideo.Api.Tests;

/// <summary>How a target duration is shared between Scenes, as a pure function.</summary>
public sealed class SceneDurationTests
{
    [Theory]
    // The look the founder approved: 3, 5, 7 and 5 seconds of a 20-second video.
    [InlineData(20, new[] { 3, 5, 7, 5 }, new[] { 3000, 5000, 7000, 5000 })]
    [InlineData(30, new[] { 3, 5, 7, 5 }, new[] { 4500, 7500, 10500, 7500 })]
    // 2.25, 3.75, 5.25 and 3.75 seconds do not fall on tenths: the two tenths left over go to the earliest Scenes.
    [InlineData(15, new[] { 3, 5, 7, 5 }, new[] { 2300, 3800, 5200, 3700 })]
    // The tenth left over goes to the Scene that was cut shortest by rounding down.
    [InlineData(1, new[] { 1, 2 }, new[] { 300, 700 })]
    [InlineData(17, new[] { 1 }, new[] { 17000 })]
    public void A_duration_is_shared_in_proportion_in_tenths_of_a_second(int seconds, int[] shares, int[] expected)
    {
        Assert.Equal(expected, SceneDurations.Allocate(seconds, shares));
    }

    [Fact]
    public void Scene_durations_sum_exactly_to_every_target_duration_a_Project_can_have()
    {
        for (var seconds = Project.MinTargetDurationSeconds; seconds <= Project.MaxTargetDurationSeconds; seconds++)
        {
            var durations = SceneDurations.Allocate(seconds, [3, 5, 7, 5]);

            Assert.Equal(seconds * 1000, durations.Sum());
            // A tenth of a second is three frames at 30 frames a second, so every Scene is a whole number of frames.
            Assert.All(durations, duration => Assert.Equal(0, duration % 100));
        }
    }
}
