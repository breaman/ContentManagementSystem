namespace ContentManagementSystem.Core.Notifications;

/// <summary>
/// Sends one piece of mail (task P7-18, spec section 14.8).
/// </summary>
/// <remarks>
/// Deliberately not ASP.NET Identity's <c>IEmailSender&lt;TUser&gt;</c>, which knows only how to
/// send a confirmation link, a password reset link, and a reset code. Workflow notifications are
/// none of those, and widening that interface would put CMS concepts into the identity stack.
/// <c>IdentityCmsEmailSender</c> in the server project bridges the two so a deployment configures
/// one transport rather than two.
/// <para>
/// Implementations must not throw for a delivery failure. A notification is a side effect of an
/// editorial action, and an approval that succeeded must not be reported as having failed because a
/// mail server was down — the in-app inbox row is written first and is what makes the failure
/// recoverable.
/// </para>
/// </remarks>
public interface ICmsEmailSender
{
    /// <summary>Whether this sender actually delivers anything.</summary>
    /// <remarks>
    /// Read by the <c>cms-email</c> health check, so a deployment running on the logging fallback
    /// says so in <c>/health</c> instead of appearing to send mail nobody receives.
    /// </remarks>
    bool IsConfigured { get; }

    /// <summary>Sends one message, or records why it could not.</summary>
    /// <param name="toAddress">Recipient's email address.</param>
    /// <param name="subject">Subject line.</param>
    /// <param name="body">Plain-text body.</param>
    /// <param name="cancellationToken">Token observed while sending.</param>
    /// <returns>Whether the message was handed to a transport.</returns>
    Task<bool> SendAsync(
        string toAddress,
        string subject,
        string body,
        CancellationToken cancellationToken = default);
}
