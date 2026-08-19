using System.Net;
using System.Net.Mail;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Notifications;

/// <summary>
/// Sends mail through an SMTP relay (task P7-18).
/// </summary>
/// <param name="options">Host, credentials, and the address mail comes from.</param>
/// <param name="logger">Log for failures, which are recorded rather than thrown.</param>
/// <remarks>
/// <see cref="SmtpClient"/> is obsolete for new code in the sense that Microsoft recommends
/// MailKit for anything demanding; it is used here anyway and deliberately. This sends a handful of
/// short plain-text messages a day to a submission endpoint, which is squarely inside what the
/// framework client does correctly, and it keeps a mail library out of the dependency graph for a
/// feature whose provider is still an open question (<strong>Q5</strong>). Swapping in MailKit later
/// is one more <see cref="ICmsEmailSender"/> and a registration line.
/// <para>
/// A failure is logged and reported as false, never thrown. The caller has already committed an
/// editorial action and written the in-app notification; turning a dead relay into a failed approval
/// would be strictly worse than a message nobody received.
/// </para>
/// </remarks>
public sealed class SmtpCmsEmailSender(
    IOptions<CmsEmailOptions> options,
    ILogger<SmtpCmsEmailSender> logger) : ICmsEmailSender
{
    private readonly CmsEmailOptions _options = options.Value;

    /// <inheritdoc />
    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.Host);

    /// <inheritdoc />
    public async Task<bool> SendAsync(
        string toAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(toAddress);

        if (!IsConfigured)
        {
            logger.LogWarning("No SMTP host is configured, so no message was sent to {ToAddress}.", toAddress);

            return false;
        }

        try
        {
            using var client = new SmtpClient(_options.Host, _options.Port)
            {
                EnableSsl = _options.UseTls,
                Timeout = _options.TimeoutSeconds * 1000,
            };

            if (!string.IsNullOrWhiteSpace(_options.UserName))
            {
                client.Credentials = new NetworkCredential(_options.UserName, _options.Password);
            }

            using var message = new MailMessage
            {
                From = new MailAddress(_options.FromAddress, _options.FromName),
                Subject = subject,

                // Plain text, and that is a security decision as much as a stylistic one: a
                // notification quotes a page title and an editor's note, both of which are
                // attacker-influenced, and text bodies have nowhere for markup to be interpreted.
                IsBodyHtml = false,
                Body = body,
            };

            message.To.Add(toAddress);

            await client.SendMailAsync(message, cancellationToken);

            return true;
        }
        catch (Exception exception) when (exception is SmtpException or InvalidOperationException
            or FormatException or IOException)
        {
            logger.LogError(
                exception,
                "Sending a notification to {ToAddress} failed. The in-app notification was still written.",
                toAddress);

            return false;
        }
    }
}
