using System.Diagnostics;

namespace AffiVideo.Worker.Rendering;

/// <summary>
/// Starts Remotion and FFmpeg. A program is started directly with a list of
/// arguments: there is no shell, so nothing in an argument is ever interpreted.
/// </summary>
internal static class Processes
{
    /// <param name="temporaryDirectory">Where the program keeps its own temporary files, so that they go when this folder does.</param>
    /// <returns>What the program wrote to its output.</returns>
    public static async Task<string> RunAsync(
        string program, IReadOnlyList<string> arguments, string workingDirectory, string temporaryDirectory,
        TimeSpan timeout, CancellationToken cancellationToken) =>
        (await RunForBothAsync(program, arguments, workingDirectory, temporaryDirectory, timeout, cancellationToken)).Output;

    /// <summary>For a program that reports what it found beside its output, as FFmpeg does what a filter measured.</summary>
    /// <returns>What the program wrote to its output, and what it wrote beside it.</returns>
    public static async Task<(string Output, string Said)> RunForBothAsync(
        string program, IReadOnlyList<string> arguments, string workingDirectory, string temporaryDirectory,
        TimeSpan timeout, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(program)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        start.Environment["TMPDIR"] = temporaryDirectory;

        using var process = Process.Start(start) ?? throw new InvalidOperationException($"{program} could not be started.");
        var output = process.StandardOutput.ReadToEndAsync(CancellationToken.None);
        var errors = process.StandardError.ReadToEndAsync(CancellationToken.None);
        using var limit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        limit.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(limit.Token);
        }
        catch (OperationCanceledException)
        {
            // Remotion starts a browser, and that has to go with it.
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None);
            await Task.WhenAll(output, errors);
            cancellationToken.ThrowIfCancellationRequested();
            throw new TimeoutException($"{program} was stopped after {timeout.TotalMinutes:0.#} minutes.");
        }

        if (process.ExitCode != 0)
        {
            var said = $"{await errors}\n{await output}".Trim();
            throw new InvalidOperationException(
                $"{program} failed with exit code {process.ExitCode}: {(said.Length > 4000 ? said[^4000..] : said)}");
        }
        return (await output, await errors);
    }
}
