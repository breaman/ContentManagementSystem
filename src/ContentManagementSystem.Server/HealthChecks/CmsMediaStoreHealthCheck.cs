using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

using ContentManagementSystem.Core.Media.Stores;

using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace ContentManagementSystem.Server.HealthChecks;

/// <summary>
/// The <c>cms-media-store</c> health check (task P5-25, spec section 24.2).
/// </summary>
/// <remarks>
/// A full write, read, and delete round trip against the configured store, because the failures that
/// matter here are asymmetric: a container whose credentials have expired still answers a
/// connectivity probe, a full disk still lists directories, and a read-only mount still serves
/// existing renditions perfectly while every upload fails. Only actually writing something finds
/// those.
/// <para>
/// Unhealthy rather than degraded, unlike <see cref="CmsTemplatesHealthCheck"/>. An unwritable media
/// store means no editor can upload and no cold rendition can be generated — the site is serving
/// stale images and losing work, which is a state to take an instance out of rotation for.
/// </para>
/// <para>
/// The probe object is written under a key derived from a hash, like every other key, and deleted
/// afterwards. A failed delete is reported but does not fail the check: a leaked probe object is a
/// few bytes, and refusing traffic over one would be worse than the leak.
/// </para>
/// </remarks>
/// <param name="store">The configured media store.</param>
public sealed class CmsMediaStoreHealthCheck(IMediaStore store) : IHealthCheck
{
    /// <summary>The name this check is registered under.</summary>
    public const string Name = "cms-media-store";

    private static ReadOnlySpan<byte> ProbeContent => "cms-media-store health probe"u8;

    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        // A distinct key per run, so two instances probing at once cannot delete each other's object
        // and report a phantom failure.
        var key = MediaStorageKeys.ForQuarantine(SHA256.HashData(Encoding.UTF8.GetBytes(Guid.NewGuid().ToString())));
        var started = Stopwatch.GetTimestamp();

        try
        {
            using (var content = new MemoryStream(ProbeContent.ToArray(), writable: false))
            {
                await store.PutAsync(key, content, "application/octet-stream", cancellationToken);
            }

            await using var read = await store.GetAsync(key, cancellationToken);

            if (read is null)
            {
                return HealthCheckResult.Unhealthy(
                    "The media store accepted a write but could not read it back.");
            }

            using var buffer = new MemoryStream();

            await read.CopyToAsync(buffer, cancellationToken);

            if (!buffer.ToArray().AsSpan().SequenceEqual(ProbeContent))
            {
                return HealthCheckResult.Unhealthy(
                    "The media store returned different bytes from the ones written to it.");
            }
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("The media store round trip failed.", exception);
        }

        var elapsed = Stopwatch.GetElapsedTime(started);
        var data = new Dictionary<string, object> { ["roundTripMs"] = elapsed.TotalMilliseconds };

        try
        {
            await store.DeleteAsync(key, cancellationToken);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return HealthCheckResult.Degraded(
                "The media store can be written and read, but the probe object could not be deleted.",
                exception,
                data);
        }

        return HealthCheckResult.Healthy("The media store completed a write, read, and delete.", data);
    }
}
