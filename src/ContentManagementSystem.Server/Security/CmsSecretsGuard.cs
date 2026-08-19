using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.ServiceDefaults;

using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// Refuses to start a deployment that is running on a development secret (task P9-05, spec section 20.8).
/// </summary>
/// <remarks>
/// Both of the things checked here already <em>work</em> when they are wrong, which is the whole
/// reason this exists. A missing media-signing key produces a per-process random one, so every image
/// on the site renders correctly on the instance that signed the URL and 403s on the next one; the
/// Aspire development password produces a database that connects. Neither has a symptom at startup,
/// and both have one weeks later, during traffic.
/// <para>
/// Checked at startup rather than by a health check, and thrown rather than reported: a health check
/// says an already-serving instance is unwell, and the correct behaviour for an instance holding a
/// key it cannot sign with across the fleet is not to serve. This is the same reasoning as
/// <c>AssertCmsMediaCapabilities</c>, which runs beside it.
/// </para>
/// <para>
/// <strong>Development is exempt</strong>, and deliberately: the point is not to make a first run
/// require a key vault. The generated key logs a warning there, which is the right volume for a
/// machine where "instance A signed it and instance B rejected it" cannot happen.
/// </para>
/// </remarks>
public static class CmsSecretsGuard
{
    /// <summary>
    /// The password the Aspire app host hands SQL Server in development.
    /// </summary>
    /// <remarks>
    /// Restated here rather than referenced. The app host is not on this project's reference graph —
    /// it is the thing that starts it — and a constant shared between them would be a dependency in
    /// the wrong direction for the sake of a string that must never change without this failing.
    /// </remarks>
    public const string DevelopmentDatabasePassword = "P@ssw0rd!";

    /// <summary>
    /// Throws when a non-development deployment is holding a development secret.
    /// </summary>
    /// <param name="services">The built application's services.</param>
    /// <param name="environment">The host environment.</param>
    /// <param name="configuration">Configuration, read for the connection string.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    /// <exception cref="InvalidOperationException">A development secret is in use outside development.</exception>
    public static void AssertCmsSecrets(
        this IServiceProvider services,
        IHostEnvironment environment,
        IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(environment);
        ArgumentNullException.ThrowIfNull(configuration);

        if (environment.IsDevelopment())
        {
            return;
        }

        var problems = new List<string>();

        var signing = services.GetRequiredService<IOptions<MediaSigningOptions>>().Value;

        if (!IsUsableKey(signing.Key))
        {
            problems.Add(
                $"{MediaSigningOptions.SectionName}:{nameof(MediaSigningOptions.Key)} is not set to a " +
                $"base64 key of at least {MediaSigningOptions.MinimumKeyBytes} bytes. Without one every " +
                "instance signs rendition URLs with a key of its own, so an image served by one is " +
                "refused by the next.");
        }

        if (signing.PreviousKey is { Length: > 0 } && signing.PreviousKeyExpiresOn is null)
        {
            problems.Add(
                $"{MediaSigningOptions.SectionName}:{nameof(MediaSigningOptions.PreviousKey)} is set " +
                $"with no {nameof(MediaSigningOptions.PreviousKeyExpiresOn)}. A rotation that never " +
                "completes has not removed the old key from anything.");
        }

        var connectionString = configuration.GetConnectionString(Constants.DatabaseConnectionString);

        if (connectionString?.Contains(DevelopmentDatabasePassword, StringComparison.Ordinal) is true)
        {
            problems.Add(
                "The database connection string carries the Aspire development password. It is a " +
                "run-mode default in the app host and is not written to the deployment manifest, so " +
                "reaching production means it was copied there by hand.");
        }

        if (problems.Count > 0)
        {
            throw new InvalidOperationException(
                $"This deployment ({environment.EnvironmentName}) is running on development secrets:" +
                Environment.NewLine +
                string.Join(Environment.NewLine, problems.Select(problem => $"  - {problem}")));
        }
    }

    /// <summary>
    /// Whether a configured key is present, decodable, and long enough.
    /// </summary>
    /// <param name="key">The configured value.</param>
    /// <returns>Whether it can be used to sign.</returns>
    /// <remarks>
    /// The length is checked after decoding rather than before. A base64 string is a third longer
    /// than its bytes, so a character count would accept a 24-byte key written as 32 characters.
    /// </remarks>
    private static bool IsUsableKey(string? key) =>
        key is { Length: > 0 } &&
        Convert.TryFromBase64String(key, new byte[key.Length], out var written) &&
        written >= MediaSigningOptions.MinimumKeyBytes;
}
