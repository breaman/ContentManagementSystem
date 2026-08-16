using Azure.Storage.Blobs;

using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Core.Media.Library;
using ContentManagementSystem.Core.Media.Processing;
using ContentManagementSystem.Core.Media.Renditions;
using ContentManagementSystem.Core.Media.Stores;
using ContentManagementSystem.Core.Media.Upload;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Media;

/// <summary>
/// Registration helpers for the media library (tasks P5-03 to P5-09).
/// </summary>
public static class MediaServiceCollectionExtensions
{
    /// <summary>
    /// Registers the image processor, the malware scanner seam, and the upload pipeline.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureUpload">Optional deployment limits — sizes, megapixels, SVG policy.</param>
    /// <param name="configureValidation">
    /// Optional publish-time policy — currently whether a placed image with no description blocks a
    /// publish or merely warns (task P5-21).
    /// </param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// A store must be registered separately, with <see cref="AddCmsFileSystemMediaStore"/> or
    /// <see cref="AddCmsBlobMediaStore"/>. Deliberately not defaulted: which store a deployment uses
    /// is not something to guess at, and a host that silently fell back to the local disk would look
    /// healthy right up until the second instance came up with a different disk.
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddCmsMedia(options => options.SvgPolicy = SvgUploadPolicy.Sanitize);
    /// builder.Services.AddCmsBlobMediaStore();
    /// // after Build():
    /// app.Services.AssertCmsMediaCapabilities();
    /// </code>
    /// </example>
    public static IServiceCollection AddCmsMedia(
        this IServiceCollection services,
        Action<MediaUploadOptions>? configureUpload = null,
        Action<MediaValidationOptions>? configureValidation = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var options = new MediaUploadOptions();

        configureUpload?.Invoke(options);

        var validation = new MediaValidationOptions();

        configureValidation?.Invoke(validation);

        // The built instance rather than IOptions, matching AddCmsSanitization: these are refusal
        // thresholds read on every upload, and a limit that could change after startup is a limit
        // whose value at the time of a rejection nobody can reconstruct.
        services.TryAddSingleton(options);

        services.TryAddSingleton<IImageProcessor, SkiaSharpImageProcessor>();

        // TryAdd, so a deployment that registers a real scanner before this call keeps it. The
        // default one logs a warning at construction rather than passing silently.
        services.TryAddSingleton<IMalwareScanner, NoOpMalwareScanner>();

        services.TryAddScoped<IMediaUploadService, MediaUploadService>();

        // The resumable transport in front of it (task P5-08). It stages parts and assembles them;
        // the pipeline above is still the only thing that decides whether the result becomes an item.
        services.TryAddScoped<IChunkedUploadService, ChunkedUploadService>();

        // The read and metadata half. Registered here rather than in a call of its own because the
        // two are one feature from a host's point of view: a deployment that accepts uploads and
        // cannot browse or describe them has a write-only library.
        services.TryAddScoped<IMediaLibraryService, MediaLibraryService>();
        services.TryAddScoped<IMediaFolderService, MediaFolderService>();

        // The publish-time half of the picker settings and of the alt-text policy (tasks P5-19 and
        // P5-21). It is registered with the library rather than with delivery because it reads the
        // same rows the library writes, and because a host that accepts uploads and publishes pages
        // needs both or neither.
        services.TryAddSingleton(validation);
        services.TryAddScoped<IMediaContentValidator, MediaContentValidator>();

        // Shared with the page, routing, and preview services; TryAdd means whichever call runs
        // first wins and every timestamp in the system comes from one clock.
        services.TryAddSingleton(TimeProvider.System);

        return services;
    }

    /// <summary>
    /// Registers rendition generation and URL signing.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configureSigning">The signing keys and their rotation state.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// Separate from <see cref="AddCmsMedia"/> because the two halves have different dependencies: a
    /// host that only accepts uploads needs no signer, and the rendering path needs a signer without
    /// needing the upload pipeline. Both are registered together in the web host.
    /// </remarks>
    public static IServiceCollection AddCmsMediaDelivery(
        this IServiceCollection services,
        Action<MediaSigningOptions>? configureSigning = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var signing = new MediaSigningOptions();

        configureSigning?.Invoke(signing);

        services.TryAddSingleton(signing);

        // Shared with the page, routing, and preview services; TryAdd means whichever call runs
        // first wins and the rotation grace period is judged against the same clock everything else
        // reads.
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddSingleton<IMediaUrlSigner, MediaUrlSigner>();

        // The render-time half of the same feature: a page stores a media id and nothing else, so
        // something has to turn that id into dimensions, alternative text, and signed URLs while the
        // markup is being written (task P5-20). Scoped, because it reads the database.
        services.TryAddScoped<IMediaResolver, MediaResolver>();

        // Singleton, and it has to be: a per-request instance would give every request its own lock,
        // which is the same as having none (task P5-13).
        services.TryAddSingleton<RenditionKeyLocks>();

        services.TryAddScoped<IRenditionService, RenditionService>();

        return services;
    }

    /// <summary>
    /// Registers the development store, keeping media on the local disk.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="rootPath">Absolute or content-root-relative directory to store under.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// The store refuses a root inside <c>wwwroot</c> at construction, so a misconfigured path fails
    /// at startup rather than quietly serving uploads as static files.
    /// </remarks>
    public static IServiceCollection AddCmsFileSystemMediaStore(this IServiceCollection services, string rootPath)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentException.ThrowIfNullOrWhiteSpace(rootPath);

        services.TryAddSingleton<IMediaStore>(provider => new FileSystemMediaStore(
            rootPath,
            provider.GetRequiredService<ILogger<FileSystemMediaStore>>()));

        return services;
    }

    /// <summary>
    /// Registers the blob store over the host's <see cref="BlobServiceClient"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="containerName">Container to use. Defaults to the one in <see cref="MediaStorageOptions"/>.</param>
    /// <returns>The service collection, for chaining.</returns>
    public static IServiceCollection AddCmsBlobMediaStore(
        this IServiceCollection services,
        string? containerName = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        var container = containerName ?? new MediaStorageOptions().BlobContainerName;

        services.TryAddSingleton<IMediaStore>(provider => new AzureBlobMediaStore(
            provider.GetRequiredService<BlobServiceClient>(),
            container));

        return services;
    }

    /// <summary>
    /// Proves the image processor can encode everything it claims to, and fails startup if not.
    /// </summary>
    /// <param name="services">The built service provider.</param>
    /// <returns>The provider, for chaining.</returns>
    /// <remarks>
    /// Called from the host after <c>Build()</c> (task P5-09, spec section 13.9). It exists because
    /// SkiaSharp answers an unsupported encode with <see langword="null"/> rather than an exception:
    /// without this, a native build missing the WebP encoder would serve empty image responses and
    /// log nothing at all. A missing native library also surfaces here, at startup, rather than on
    /// the first request for a rendition.
    /// </remarks>
    public static IServiceProvider AssertCmsMediaCapabilities(this IServiceProvider services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.GetRequiredService<IImageProcessor>().AssertCapabilities();

        return services;
    }
}
