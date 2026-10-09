namespace AffiVideo.Application.Storage;

/// <summary>
/// The application's files, each under a key. Nothing here is reachable from
/// outside: a file leaves only through the API, after it has authorised the request.
/// </summary>
public interface IObjectStorage
{
    /// <summary>Stores the content under the key, replacing whatever was there.</summary>
    Task PutAsync(string key, Stream content, string contentType, CancellationToken cancellationToken);

    /// <summary>The content under the key, for the caller to dispose; null when there is none.</summary>
    Task<Stream?> OpenAsync(string key, CancellationToken cancellationToken);

    /// <summary>Deleting a key that holds nothing changes nothing.</summary>
    Task DeleteAsync(string key, CancellationToken cancellationToken);
}
