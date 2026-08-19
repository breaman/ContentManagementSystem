using ContentManagementSystem.Core.Notifications;
using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Server.Components.Email;

/// <summary>
/// Sends Identity's three account emails through the CMS's own mail transport (task P7-18).
/// </summary>
/// <param name="sender">The configured transport, or the logging fallback.</param>
/// <remarks>
/// Replaces <c>IdentityNoOpEmailSender</c>, which discarded every message silently — password resets
/// and account confirmations included. One transport serves both halves of the system, so a
/// deployment configures mail once and a developer running locally reads the confirmation link out
/// of the console rather than out of the database.
/// <para>
/// The bodies are plain text rather than the anchor tags the scaffolded sender used. A link that has
/// to survive an HTML-stripping mail client and a plain-text preview pane is a link, not markup, and
/// the reset URL is the whole content of the message either way.
/// </para>
/// </remarks>
public sealed class IdentityCmsEmailSender(ICmsEmailSender sender) : IEmailSender<User>
{
    /// <inheritdoc />
    public Task SendConfirmationLinkAsync(User user, string email, string confirmationLink) =>
        sender.SendAsync(
            email,
            "Confirm your email",
            $"Confirm your account by opening this link:\n\n{confirmationLink}");

    /// <inheritdoc />
    public Task SendPasswordResetLinkAsync(User user, string email, string resetLink) =>
        sender.SendAsync(
            email,
            "Reset your password",
            $"Reset your password by opening this link:\n\n{resetLink}");

    /// <inheritdoc />
    public Task SendPasswordResetCodeAsync(User user, string email, string resetCode) =>
        sender.SendAsync(
            email,
            "Reset your password",
            $"Reset your password using this code: {resetCode}");
}
