namespace AffiVideo.Infrastructure.Storage;

/// <summary>Where the S3-compatible object storage is and how to sign requests to it.</summary>
public sealed class StorageOptions
{
    public const string Section = "Storage";

    public string Endpoint { get; set; } = "";
    public string AccessKey { get; set; } = "";
    public string SecretKey { get; set; } = "";
    public string Bucket { get; set; } = "";
}
