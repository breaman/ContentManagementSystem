using Azure;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;

namespace ContentManagementSystem.Core.Media.Stores;

/// <summary>
/// The production store: media bytes in a private Azure blob container (task P5-04,
/// spec section 13.2).
/// </summary>
/// <remarks>
/// Development runs this against the Azurite emulator the AppHost provisions, so the store that
/// ships is the store that is exercised — the filesystem store exists for a machine without Docker,
/// not as the path most work happens on.
/// <para>
/// <strong>The container is created private and stays private.</strong> Blobs are addressed by
/// server-generated key and reached only through the media endpoint, which pins the content type,
/// sets <c>nosniff</c>, and applies authorization (spec section 20.7). A container with public blob
/// access would hand out originals — including ones belonging to unpublished pages — under URLs the
/// application never signed and cannot revoke.
/// </para>
/// </remarks>
public sealed class AzureBlobMediaStore : IMediaStore
{
    private readonly BlobContainerClient _container;

    /// <summary>
    /// Creates the store over a container client.
    /// </summary>
    /// <param name="serviceClient">The blob service client the host registered.</param>
    /// <param name="containerName">Name of the container media lives in.</param>
    public AzureBlobMediaStore(BlobServiceClient serviceClient, string containerName)
    {
        ArgumentNullException.ThrowIfNull(serviceClient);
        ArgumentException.ThrowIfNullOrWhiteSpace(containerName);

        _container = serviceClient.GetBlobContainerClient(containerName);
    }

    /// <inheritdoc />
    public async Task<MediaStoreResult> PutAsync(
        string key,
        Stream content,
        string contentType,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(content);
        MediaStorageKeys.Validate(key);

        await EnsureContainerAsync(cancellationToken).ConfigureAwait(false);

        var blob = _container.GetBlobClient(key);

        // Content-Type is recorded on the blob for diagnostics and for any future direct-download
        // path. It is not what the delivery endpoint trusts: that reads the sniffed type from the
        // MediaItem row, so a blob whose header was set wrongly cannot change how bytes are served.
        var options = new BlobUploadOptions
        {
            HttpHeaders = new BlobHttpHeaders { ContentType = contentType },
        };

        // Overwriting is intended. Keys are content-addressed, so a second upload under an existing
        // key is the identical file arriving again; the blob API's own upload is atomic, so a failed
        // put leaves the previous content rather than a truncated object.
        await blob.UploadAsync(content, options, cancellationToken).ConfigureAwait(false);

        var properties = await blob.GetPropertiesAsync(cancellationToken: cancellationToken).ConfigureAwait(false);

        return new MediaStoreResult(key, properties.Value.ContentLength, contentType);
    }

    /// <inheritdoc />
    public async Task<Stream?> GetAsync(string key, CancellationToken cancellationToken)
    {
        MediaStorageKeys.Validate(key);

        try
        {
            return await _container.GetBlobClient(key)
                .OpenReadAsync(cancellationToken: cancellationToken)
                .ConfigureAwait(false);
        }
        catch (RequestFailedException exception) when (exception.Status is 404)
        {
            // "Nothing stored here" is an ordinary answer — a rendition that has not been generated
            // yet reaches this every time — and the interface says it is a null, not an exception.
            return null;
        }
    }

    /// <inheritdoc />
    public async Task<bool> ExistsAsync(string key, CancellationToken cancellationToken)
    {
        MediaStorageKeys.Validate(key);

        var response = await _container.GetBlobClient(key)
            .ExistsAsync(cancellationToken)
            .ConfigureAwait(false);

        return response.Value;
    }

    /// <inheritdoc />
    public async Task DeleteAsync(string key, CancellationToken cancellationToken)
    {
        MediaStorageKeys.Validate(key);

        await _container.GetBlobClient(key)
            .DeleteIfExistsAsync(cancellationToken: cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Null in v1. The container is private, so a usable direct URL would have to be a SAS, and
    /// issuing one would route around the media endpoint that pins content types and applies the
    /// safety headers. When a CDN offload is added this is where the SAS belongs — the interface
    /// carries the shape now so that change does not reach the callers.
    /// </remarks>
    public Uri? GetPublicUrl(string key, TimeSpan? validFor = null) => null;

    /// <summary>
    /// Creates the container on first use if it is not already there.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>A task that completes once the container exists.</returns>
    /// <remarks>
    /// <see cref="PublicAccessType.None"/> is passed explicitly rather than left to the default. The
    /// default is already None, but this is the single line whose accidental change would make every
    /// stored original world-readable, and a value that is stated cannot be changed by a future
    /// default.
    /// </remarks>
    private async Task EnsureContainerAsync(CancellationToken cancellationToken)
    {
        await _container.CreateIfNotExistsAsync(
            PublicAccessType.None,
            cancellationToken: cancellationToken).ConfigureAwait(false);
    }
}
