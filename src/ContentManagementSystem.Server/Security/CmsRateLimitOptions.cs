namespace ContentManagementSystem.Server.Security;

/// <summary>
/// The two rate-limit budgets a deployment may move (tasks P9-03, P9-13).
/// </summary>
/// <remarks>
/// Both are per-address budgets on public reads, and both default to the figures
/// [§20.6](../../../spec.md) names. They are configurable for one reason: a load test generates its
/// traffic from a few addresses, and the public budget is ten requests a second per address, so a
/// run against the defaults measures the rejection path rather than the site.
/// <para>
/// Nothing that protects a password or a write is here. Those budgets stay where the spec put them.
/// </para>
/// <example>
/// A load-test environment, in <c>appsettings.LoadTest.json</c>:
/// <code>
/// { "Cms": { "RateLimits": { "PublicPagesPerMinute": 2000000, "MediaResponsesPerMinute": 500000 } } }
/// </code>
/// </example>
/// </remarks>
public sealed class CmsRateLimitOptions
{
    /// <summary>Configuration section this binds to.</summary>
    public const string SectionName = "Cms:RateLimits";

    /// <summary>Public pages one address may fetch per minute.</summary>
    public int PublicPagesPerMinute { get; set; } = CmsRateLimits.PublicPagesPerMinute;

    /// <summary>Media responses one address may fetch per minute.</summary>
    public int MediaResponsesPerMinute { get; set; } = CmsRateLimits.MediaResponsesPerMinute;

    /// <summary>
    /// Throws when a configured budget would switch its limiter off rather than raise it.
    /// </summary>
    /// <exception cref="InvalidOperationException">A budget is zero or negative.</exception>
    /// <remarks>
    /// A permit limit of zero refuses every request, and a negative one throws from inside the
    /// limiter on the first request rather than at startup. Both are configuration mistakes worth
    /// failing the deployment over: there is no way to spell "no limit" here, on purpose.
    /// </remarks>
    public void Validate()
    {
        foreach (var (name, value) in new[]
        {
            (nameof(PublicPagesPerMinute), PublicPagesPerMinute),
            (nameof(MediaResponsesPerMinute), MediaResponsesPerMinute),
        })
        {
            if (value <= 0)
            {
                throw new InvalidOperationException(
                    $"{SectionName}:{name} is {value}. A budget must be a positive number of " +
                    "requests per minute; there is no configuration that removes the limit.");
            }
        }
    }
}
