using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// What a deployment gets to choose about sign-in hardening (task P9-04, spec section 20.3).
/// </summary>
/// <remarks>
/// The defaults are the safe reading of every question, including the one nobody has answered:
/// <strong>Q10</strong> asks whether self-service registration stays enabled and with what role, and
/// until it is answered the door is shut. That follows the shape `P5-06` used for <strong>Q7</strong>
/// — ship both branches, default to the refusal, and make the answer a line of configuration rather
/// than a change to any code.
/// </remarks>
public sealed class CmsIdentityOptions
{
    /// <summary>Configuration section these bind from.</summary>
    public const string SectionName = "Cms:Identity";

    /// <summary>
    /// Shortest password an account may have.
    /// </summary>
    /// <remarks>
    /// Twelve, per spec section 20.3, replacing the template's six. Length is the only password rule
    /// that reliably buys anything: the character-class requirements the template also set are what
    /// produce <c>Password1!</c>, so they stay off and this and the breach screen do the work.
    /// </remarks>
    public int MinimumPasswordLength { get; set; } = 12;

    /// <summary>
    /// Whether anybody may create their own account, and what they get if they do.
    /// </summary>
    public SelfRegistrationPolicy SelfRegistration { get; set; } = SelfRegistrationPolicy.Disabled;

    /// <summary>
    /// Roles that may not be used without a second factor.
    /// </summary>
    /// <remarks>
    /// The three of spec section 20.3: every role that can change what the public site says, or who
    /// else can. A user holding one of these and no second factor is sent to set one up and can reach
    /// nothing else until they have.
    /// </remarks>
    public HashSet<string> TwoFactorRequiredRoles { get; } = new(StringComparer.OrdinalIgnoreCase)
    {
        CmsRoles.Administrator,
        CmsRoles.Developer,
        CmsRoles.Approver,
    };

    /// <summary>
    /// Whether to ask Have I Been Pwned's range API whether a password appears in a breach corpus.
    /// </summary>
    /// <remarks>
    /// Off by default, and that is a deployment decision rather than a security opinion: it adds an
    /// outbound call to a third party on the path of every password change. The local common-password
    /// screen runs either way — see <see cref="IBreachedPasswordScreen"/> for what each of the two
    /// actually catches.
    /// </remarks>
    public bool UseHaveIBeenPwned { get; set; }

    /// <summary>
    /// What to do when the breach service cannot be reached.
    /// </summary>
    /// <remarks>
    /// Accept, by default. Failing closed means an outage at a third party stops every password reset
    /// in this system, including the ones being done <em>because</em> of an incident. The refusal is
    /// logged as a warning either way, so "the screen is not running" is visible rather than assumed.
    /// </remarks>
    public bool RefuseWhenBreachServiceUnavailable { get; set; }
}

/// <summary>
/// The two answers spec section 20.3 will accept to <strong>Q10</strong>.
/// </summary>
public enum SelfRegistrationPolicy
{
    /// <summary>
    /// Nobody creates their own account; the registration routes answer as though they do not exist.
    /// </summary>
    /// <remarks>
    /// The default. An open <c>/account/register</c> on a CMS is a standing risk, and 404 rather than
    /// 403 for the same reason a refused <c>Content.Read</c> answers not found: a 403 tells the
    /// caller the door is there.
    /// </remarks>
    Disabled = 0,

    /// <summary>
    /// Anybody may register, and a new account holds no role until an administrator grants one.
    /// </summary>
    /// <remarks>
    /// The other reading section 20.3 permits. Nothing in this application grants a role on
    /// registration, so this branch is the registration pages as they already behave — it is named
    /// here so that "we chose this" and "nobody has decided" are different states of the file.
    /// </remarks>
    NoRole = 1,
}
