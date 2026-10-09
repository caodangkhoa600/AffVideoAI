namespace AffiVideo.Domain;

public static class SceneDurations
{
    /// <summary>
    /// What Scene durations are counted in. A tenth of a second is a whole number of
    /// frames at 30 frames a second, so no Scene ends between two frames.
    /// </summary>
    public const int StepMilliseconds = 100;

    /// <summary>
    /// Shares a target duration between Scenes in proportion to their shares. Each
    /// duration is in milliseconds and a whole number of steps, and together they
    /// are exactly the target: what rounding down leaves over goes, a step at a
    /// time, to the Scenes it cut shortest, the earlier Scene first.
    /// </summary>
    public static IReadOnlyList<int> Allocate(int targetDurationSeconds, IReadOnlyList<int> shares)
    {
        var steps = targetDurationSeconds * 1000 / StepMilliseconds;
        var whole = shares.Sum();
        var allocated = shares.Select(share => steps * share / whole).ToArray();

        var cutShortest = Enumerable.Range(0, shares.Count)
            .OrderByDescending(scene => steps * shares[scene] % whole)
            .ThenBy(scene => scene)
            .Take(steps - allocated.Sum());
        foreach (var scene in cutShortest) allocated[scene]++;

        return allocated.Select(count => count * StepMilliseconds).ToArray();
    }
}
