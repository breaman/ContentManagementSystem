using Microsoft.Extensions.Logging;

namespace ContentManagementSystem.Core.Notifications;

/// <summary>
/// Writes mail to the log instead of sending it, for a deployment that has not configured a relay.
/// </summary>
/// <param name="logger">Where the message goes instead.</param>
/// <remarks>
/// The replacement for <c>IdentityNoOpEmailSender</c>, which discarded messages without saying so
/// (task P7-18). The difference matters: a developer running locally can now read the password reset
/// link out of the console, and a production deployment that forgot to configure a host is warned on
/// every send and reported as unconfigured by the <c>cms-email</c> health check rather than appearing
/// to work.
/// </remarks>
public sealed class LoggingCmsEmailSender(ILogger<LoggingCmsEmailSender> logger) : ICmsEmailSender
{
    /// <inheritdoc />
    public bool IsConfigured => false;

    /// <inheritdoc />
    public Task<bool> SendAsync(
        string toAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        logger.LogWarning(
            "No mail transport is configured, so this message was not sent. To: {ToAddress}. " +
            "Subject: {Subject}. Body: {Body}",
            toAddress,
            subject,
            body);

        return Task.FromResult(false);
    }
}
