using System.Security.Cryptography;
using AffiVideo.Application.Storage;

namespace AffiVideo.Worker.Rendering;

/// <summary>
/// The Scene clips Remotion has drawn, kept in object storage for the renders
/// after. A clip is kept under a key made from everything that decides its
/// frames, so a Scene that is the same in the next Storyboard version, or in the
/// same version rendered again, is not drawn a second time. Each Organization
/// has clips of its own.
/// </summary>
internal sealed class SceneClips(IObjectStorage storage)
{
    /// <summary>
    /// The key of a clip: how clips are drawn, the Scene as its template is handed
    /// it, and every image it names. Two Scenes with one key have the same frames.
    /// </summary>
    /// <param name="look">The templates and how they are run: <see cref="Remotion.Look"/>.</param>
    /// <param name="scene">The Scene as data, as Remotion is given it.</param>
    /// <param name="images">The images the Scene names, in a fixed order.</param>
    public static string KeyOf(string look, byte[] scene, IEnumerable<byte[]> images)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        hash.AppendData(Convert.FromHexString(look));
        hash.AppendData(SHA256.HashData(scene));
        // Each as its own hash, so where one image ends and the next begins is part of the key.
        foreach (var image in images) hash.AppendData(SHA256.HashData(image));
        return Convert.ToHexStringLower(hash.GetHashAndReset());
    }

    /// <summary>Writes the kept clip to the file.</summary>
    /// <returns>False, with nothing written, when no clip is kept under the key.</returns>
    public async Task<bool> FetchAsync(Guid organizationId, string key, string file, CancellationToken cancellationToken)
    {
        await using var kept = await storage.OpenAsync(StorageKey(organizationId, key), cancellationToken);
        if (kept is null) return false;

        await using var written = File.Create(file);
        await kept.CopyToAsync(written, cancellationToken);
        return true;
    }

    public async Task KeepAsync(Guid organizationId, string key, string file, CancellationToken cancellationToken)
    {
        await using var clip = File.OpenRead(file);
        await storage.PutAsync(StorageKey(organizationId, key), clip, "video/mp4", cancellationToken);
    }

    // It starts with the Organization, as every file an Organization has stored does.
    private static string StorageKey(Guid organizationId, string key) => $"organizations/{organizationId}/scene-clips/{key}.mp4";
}
