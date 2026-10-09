using System.Net.Http.Json;
using AffiVideo.Contracts;

namespace AffiVideo.Api.Tests;

/// <summary>
/// One browser's view of the API: it keeps the cookies it is given, and before a
/// state-changing request it fetches an anti-forgery token the way the web app does.
/// </summary>
public sealed class Browser(HttpClient http) : IDisposable
{
    public const string AntiforgeryHeader = "X-CSRF-TOKEN";

    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;

    /// <summary>The underlying client, for requests a well-behaved browser would not make.</summary>
    public HttpClient Http => http;

    public Task<HttpResponseMessage> GetAsync(string path) => http.GetAsync(path, Cancellation);

    public async Task<T> GetAsync<T>(string path)
    {
        var response = await GetAsync(path);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<T>(AffiVideoApp.Json, Cancellation))!;
    }

    public Task<HttpResponseMessage> PostAsync(string path, object body) => SendAsync(HttpMethod.Post, path, body);

    public Task<HttpResponseMessage> PutAsync(string path, object body) => SendAsync(HttpMethod.Put, path, body);

    public Task<HttpResponseMessage> DeleteAsync(string path) => SendAsync(HttpMethod.Delete, path, null);

    public async Task<string> AntiforgeryTokenAsync() =>
        (await GetAsync<AntiforgeryTokenResponse>("/api/v1/antiforgery-token")).RequestToken;

    public Task<HttpResponseMessage> SignInAsync(string email, string password) =>
        PostAsync("/api/v1/session", new SignInRequest(email, password));

    private async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body)
    {
        using var request = new HttpRequestMessage(method, path);
        request.Headers.Add(AntiforgeryHeader, await AntiforgeryTokenAsync());
        if (body is not null) request.Content = JsonContent.Create(body, options: AffiVideoApp.Json);
        return await http.SendAsync(request, Cancellation);
    }

    public void Dispose() => http.Dispose();
}
