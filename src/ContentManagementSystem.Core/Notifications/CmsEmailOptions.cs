namespace ContentManagementSystem.Core.Notifications;

/// <summary>
/// How this deployment sends mail (task P7-18).
/// </summary>
/// <remarks>
/// SMTP rather than a provider SDK (<c>ADR-0024</c>), and that is the answer to open question
/// <strong>Q5</strong>
/// rather than a way of dodging it. Every candidate — SendGrid, Mailgun, Amazon SES, Microsoft 365,
/// a corporate relay — offers an SMTP endpoint, so a deployment picks its provider by filling in a
/// host and a credential rather than by anyone choosing a NuGet package on its behalf. If a provider
/// SDK is later wanted for deliverability reporting, it is a second <see cref="ICmsEmailSender"/>
/// beside this one, not a change to anything that calls it.
/// <para>
/// With no <see cref="Host"/> configured the deployment falls back to
/// <see cref="LoggingCmsEmailSender"/>, which writes what it would have sent. That is deliberately
/// visible — the health check reports it — because mail that silently goes nowhere is how an
/// approval queue quietly stops working.
/// </para>
/// </remarks>
public sealed class CmsEmailOptions
{
    /// <summary>Configuration section these are bound from.</summary>
    public const string SectionName = "Cms:Email";

    /// <summary>SMTP host, or null to run without sending mail.</summary>
    public string? Host { get; set; }

    /// <summary>SMTP port. 587 is submission with STARTTLS, which is what most providers want.</summary>
    public int Port { get; set; } = 587;

    /// <summary>Whether to negotiate TLS. Leave on: a relay that needs it off is on the same host.</summary>
    public bool UseTls { get; set; } = true;

    /// <summary>Username, when the relay authenticates.</summary>
    public string? UserName { get; set; }

    /// <summary>Password or API key, which belongs in a secret store rather than in appsettings.</summary>
    public string? Password { get; set; }

    /// <summary>Address mail is sent from.</summary>
    public string FromAddress { get; set; } = "cms@localhost";

    /// <summary>Display name mail is sent from.</summary>
    public string FromName { get; set; } = "Content Management System";

    /// <summary>How long to wait on the transport before giving up on one message.</summary>
    /// <remarks>
    /// Short on purpose. Notifications are sent on the request path of an editorial action, and a
    /// thirty-second SMTP timeout would make approving a page feel broken.
    /// </remarks>
    public int TimeoutSeconds { get; set; } = 10;

    /// <summary>
    /// Absolute base URL the backoffice is reachable at, used to turn a notification's relative link
    /// into one a mail client can follow.
    /// </summary>
    /// <remarks>
    /// Configured rather than taken from the incoming request's <c>Host</c> header. A notification is
    /// often raised by a background job, which has no request; and taking it from a request would let
    /// a forged header decide where a link in an email points.
    /// </remarks>
    public string? SiteBaseUrl { get; set; }
}
