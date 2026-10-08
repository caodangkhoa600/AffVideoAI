using Amazon.Runtime;
using Amazon.S3;
using Microsoft.Extensions.Options;

namespace AffiVideo.Infrastructure.Storage;

/// <summary>The application's bucket on S3-compatible object storage (MinIO locally).</summary>
internal sealed class S3ObjectStorage : IDisposable
{
    private readonly StorageOptions _options;
    private readonly Lazy<AmazonS3Client> _client;

    public S3ObjectStorage(IOptions<StorageOptions> options)
    {
        _options = options.Value;
        _client = new Lazy<AmazonS3Client>(CreateClient);
    }

    /// <summary>Throws unless the storage answers a signed request.</summary>
    public async Task ProbeAsync(CancellationToken cancellationToken) =>
        await _client.Value.ListBucketsAsync(cancellationToken);

    public async Task EnsureBucketExistsAsync(CancellationToken cancellationToken)
    {
        var buckets = await _client.Value.ListBucketsAsync(cancellationToken);
        if (buckets.Buckets?.Any(bucket => bucket.BucketName == _options.Bucket) != true)
        {
            await _client.Value.PutBucketAsync(_options.Bucket, cancellationToken);
        }
    }

    private AmazonS3Client CreateClient()
    {
        foreach (var (name, value) in new[]
        {
            (nameof(StorageOptions.Endpoint), _options.Endpoint),
            (nameof(StorageOptions.AccessKey), _options.AccessKey),
            (nameof(StorageOptions.SecretKey), _options.SecretKey),
            (nameof(StorageOptions.Bucket), _options.Bucket),
        })
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                throw new InvalidOperationException($"{StorageOptions.Section}:{name} is not configured.");
            }
        }

        return new AmazonS3Client(
            new BasicAWSCredentials(_options.AccessKey, _options.SecretKey),
            new AmazonS3Config
            {
                ServiceURL = _options.Endpoint,
                // MinIO serves buckets as path segments, not as subdomains.
                ForcePathStyle = true,
                AuthenticationRegion = "us-east-1",
            });
    }

    public void Dispose()
    {
        if (_client.IsValueCreated) _client.Value.Dispose();
    }
}
