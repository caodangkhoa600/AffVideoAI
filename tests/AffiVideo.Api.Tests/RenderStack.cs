using DotNet.Testcontainers.Containers;
using Microsoft.AspNetCore.Mvc.Testing;

namespace AffiVideo.Api.Tests;

/// <summary>
/// The system a second time, with a queue nobody else serves: an API on a database
/// of its own, and only the workers a test starts, each with the settings the test
/// gives it. Made by <see cref="AffiVideoApp.NewStackAsync"/>.
/// </summary>
public sealed class RenderStack(
    WebApplicationFactory<Program> api,
    Func<IReadOnlyCollection<(string Name, string Value)>, Task<IContainer>> runWorker) : IAsyncDisposable
{
    private readonly List<IContainer> _workers = [];

    /// <summary>A browser with no cookies yet.</summary>
    public Browser NewBrowser() => AffiVideoApp.NewBrowser(api);

    /// <summary>A browser signed in as the Owner of a new Organization.</summary>
    public async Task<Browser> NewMemberAsync()
    {
        var organization = await AffiVideoApp.CreateOrganizationAsync(api);
        var browser = NewBrowser();
        (await browser.SignInAsync(organization.Owner.Email, organization.Owner.Password)).EnsureSuccessStatusCode();
        return browser;
    }

    /// <summary>A worker serving this stack's queue, running when this returns.</summary>
    /// <param name="settings">Settings of the worker, named as its environment names them: <c>RenderQueue__MaxAttempts</c>.</param>
    public async Task<IContainer> StartWorkerAsync(params (string Name, string Value)[] settings)
    {
        var worker = await runWorker(settings);
        _workers.Add(worker);
        return worker;
    }

    /// <summary>Stops a worker as a crash does: at once, with no chance to finish or tidy up.</summary>
    public static async Task KillAsync(IContainer worker)
    {
        var (exitCode, said) = await AffiVideoApp.DockerAsync("kill", worker.Id);
        Assert.True(exitCode == 0, said);
    }

    /// <summary>The end of what a worker has logged, for saying why a job did not end as a test expected.</summary>
    public static async Task<string> LogAsync(IContainer worker)
    {
        var (output, errors) = await worker.GetLogsAsync(ct: TestContext.Current.CancellationToken);
        var log = $"{output}\n{errors}";
        return log.Length > 6000 ? log[^6000..] : log;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var worker in _workers) await worker.DisposeAsync();
        await api.DisposeAsync();
    }
}
