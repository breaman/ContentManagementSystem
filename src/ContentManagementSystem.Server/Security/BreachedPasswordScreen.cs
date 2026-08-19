using System.Collections.Frozen;
using System.Security.Cryptography;
using System.Text;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// Whether a password is one an attacker already has (task P9-04, spec section 20.3).
/// </summary>
/// <remarks>
/// Two implementations, and they catch different things.
/// <see cref="CommonPasswordScreen"/> holds a list in the process and refuses the passwords people
/// reach for first; <see cref="HaveIBeenPwnedScreen"/> asks a breach corpus and refuses the ones an
/// attacker has a hash of. The first is a floor and is always on. The second is what makes "breached-
/// password screening" literally true, and it is off unless a deployment turns it on, because it puts
/// a third party on the path of every password change.
/// <para>
/// The seam exists for the reason <c>ICmsEmailSender</c>'s does (ADR-0024): the choice is a deployment
/// question, and a second implementation is a registration rather than a change to anything that
/// calls it.
/// </para>
/// </remarks>
public interface IBreachedPasswordScreen
{
    /// <summary>
    /// Whether this password is known to be compromised.
    /// </summary>
    /// <param name="password">The candidate, in the clear.</param>
    /// <param name="cancellationToken">Token observed while the screen runs.</param>
    /// <returns>True when the password must be refused.</returns>
    ValueTask<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default);
}

/// <summary>
/// The passwords a credential-stuffing list starts with, held in the process.
/// </summary>
/// <remarks>
/// <strong>This is a floor, not a breach corpus.</strong> Its value is that it costs nothing and
/// always runs; what it cannot do is know that one particular passphrase turned up in a dump last
/// year. That is <see cref="HaveIBeenPwnedScreen"/>'s job.
/// <para>
/// The list is longer than the twelve-character minimum needs, deliberately. A deployment that lowers
/// <see cref="CmsIdentityOptions.MinimumPasswordLength"/> — which it can, and which the option exists
/// to allow — would otherwise silently lose the short half of the screen at the same moment it needs
/// it most.
/// </para>
/// <para>
/// Compared with case and separators removed, because <c>P@ssword-123</c> and <c>password123</c> are
/// the same password to anybody running a list through a mangling rule set. Both sides go through the
/// same normalisation, so the list can be written the way a person reads it.
/// </para>
/// </remarks>
public sealed class CommonPasswordScreen : IBreachedPasswordScreen
{
    /// <summary>The list as it is worth reading, before normalisation.</summary>
    private static readonly string[] Raw =
        [
            // The perennial top of every list, kept for deployments that lower the minimum length.
            "123456", "password", "12345678", "qwerty", "123456789", "12345", "1234", "111111",
            "1234567", "dragon", "123123", "baseball", "abc123", "football", "monkey", "letmein",
            "shadow", "master", "666666", "qwertyuiop", "123321", "mustang", "1234567890",
            "michael", "654321", "superman", "1qaz2wsx", "7777777", "121212", "000000", "qazwsx",
            "123qwe", "killer", "trustno1", "jordan", "jennifer", "zxcvbnm", "asdfgh", "hunter",
            "buster", "soccer", "harley", "batman", "andrew", "tigger", "sunshine", "iloveyou",
            "charlie", "robert", "thomas", "hockey", "ranger", "daniel", "starwars", "klaster",
            "112233", "george", "computer", "michelle", "jessica", "pepper", "1111", "zxcvbn",
            "555555", "11111111", "131313", "freedom", "777777", "passw0rd", "maggie", "159753",
            "aaaaaa", "ginger", "princess", "joshua", "cheese", "amanda", "summer", "love",
            "ashley", "nicole", "chelsea", "biteme", "matthew", "access", "yankees", "987654321",
            "dallas", "austin", "thunder", "taylor", "matrix", "welcome", "admin", "administrator",
            "root", "guest", "changeme", "secret", "test", "letmein123", "welcome1", "abcd1234",

            // The ones that survive a twelve-character minimum, which is where this list earns its
            // keep: every one of these is a top-list entry with a mangling rule already applied.
            "password123", "password1234", "password12345", "password123456", "passw0rd123",
            "p@ssword123", "p@ssw0rd123", "administrator1", "qwerty123456", "qwertyuiop123",
            "1q2w3e4r5t6y", "1qaz2wsx3edc", "zaq12wsxcde3", "123456789012", "1234567890123",
            "12345678901234", "123456789abc", "abcdefghijkl", "abcd1234efgh", "iloveyou123",
            "letmein123456", "welcome123456", "welcome1234", "trustno1234", "sunshine123",
            "princess123", "football123", "baseball123", "superman123", "starwars123",
            "changeme123", "changemenow", "temporary123", "temppassword", "newpassword1",
            "myp@ssword12", "secretpassword", "passwordpassword", "letmeinplease",
            "iloveyousomuch", "thisismypassword", "mypasswordis1", "correcthorse",
            "correcthorsebatterystaple", "qwertyuiopasdfghjkl", "asdfghjklzxcvbnm",
        ];

    /// <summary>The same list, normalised once, which is the form a candidate is compared against.</summary>
    private static readonly FrozenSet<string> Common =
        FrozenSet.ToFrozenSet(Raw.Select(Normalize), StringComparer.Ordinal);

    /// <inheritdoc />
    public ValueTask<bool> IsBreachedAsync(string password, CancellationToken cancellationToken = default) =>
        ValueTask.FromResult(!string.IsNullOrEmpty(password) && Common.Contains(Normalize(password)));

    /// <summary>
    /// Reduces a password to what a list-mangling attacker would see.
    /// </summary>
    /// <param name="password">The candidate.</param>
    /// <returns>The comparison form.</returns>
    /// <remarks>
    /// Lower case, separators dropped, and the three substitutions every rule set applies. Both sides
    /// of the comparison go through this, so the list above can be written the way a person reads it.
    /// <para>
    /// Deliberately shallow. Anything cleverer starts refusing passwords that merely resemble a common
    /// one, and a refusal a user cannot understand is a refusal they work around with a worse
    /// password. Digits are left alone in particular: folding them would collapse every year and
    /// every counter into one entry.
    /// </para>
    /// </remarks>
    private static string Normalize(string password)
    {
        var builder = new StringBuilder(password.Length);

        foreach (var character in password.ToLowerInvariant())
        {
            switch (character)
            {
                case '@':
                    builder.Append('a');
                    break;
                case '$':
                    builder.Append('s');
                    break;
                case '!':
                case '-':
                case '_':
                case '.':
                case ' ':
                    break;
                default:
                    builder.Append(character);
                    break;
            }
        }

        return builder.ToString();
    }
}

/// <summary>
/// Asks Have I Been Pwned's range API, which never sees the password (task P9-04).
/// </summary>
/// <param name="client">The HTTP client the range request goes out on.</param>
/// <param name="inner">The local screen, which runs first and for free.</param>
/// <param name="logger">Where an unreachable service is reported.</param>
/// <param name="refuseWhenUnavailable">Whether an unreachable service refuses the password.</param>
/// <remarks>
/// <strong>k-anonymity.</strong> The first five hex characters of the SHA-1 of the password are sent;
/// the service answers with every suffix it holds under that prefix, several hundred of them, and the
/// comparison happens here. The service therefore learns a bucket containing roughly one in a million
/// of all passwords and never the password itself — which is what makes sending anything at all
/// defensible.
/// <para>
/// SHA-1 because that is the API's index, not because it is a reasonable way to store a password. It
/// is used here as a lookup key over a value the caller already holds in the clear.
/// </para>
/// </remarks>
public sealed class HaveIBeenPwnedScreen(
    HttpClient client,
    CommonPasswordScreen inner,
    ILogger<HaveIBeenPwnedScreen> logger,
    bool refuseWhenUnavailable) : IBreachedPasswordScreen
{
    /// <summary>Name of the configured <see cref="HttpClient"/>.</summary>
    public const string HttpClientName = "cms-hibp";

    /// <summary>Base address the range API is served from.</summary>
    public const string BaseAddress = "https://api.pwnedpasswords.com/";

    /// <inheritdoc />
    public async ValueTask<bool> IsBreachedAsync(
        string password,
        CancellationToken cancellationToken = default)
    {
        if (await inner.IsBreachedAsync(password, cancellationToken))
        {
            return true;
        }

        if (string.IsNullOrEmpty(password))
        {
            return false;
        }

        var hash = Convert.ToHexString(SHA1.HashData(Encoding.UTF8.GetBytes(password)));
        var prefix = hash[..5];
        var suffix = hash[5..];

        try
        {
            using var response = await client.GetAsync(
                new Uri($"range/{prefix}", UriKind.Relative),
                cancellationToken);

            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadAsStringAsync(cancellationToken);

            // Each line is "SUFFIX:COUNT". Any count at all means the hash is in the corpus; the
            // number is how often, which is interesting to a report and not to a decision.
            foreach (var line in body.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                if (line.AsSpan().TrimEnd().StartsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException)
        {
            // Logged as a warning whichever way this falls, so "the screen is not running" is
            // visible rather than assumed.
            logger.LogWarning(
                exception,
                "The breached-password service could not be reached; the password was {Outcome}.",
                refuseWhenUnavailable ? "refused" : "accepted on the local screen alone");

            return refuseWhenUnavailable;
        }
    }
}
