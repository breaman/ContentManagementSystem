using ContentManagementSystem.Data.Models;

using Microsoft.AspNetCore.Identity;

namespace ContentManagementSystem.Server.Security;

/// <summary>
/// The two password rules Identity has no setting for (task P9-04, spec section 20.3).
/// </summary>
/// <param name="screen">Whether the password is one an attacker already has.</param>
/// <remarks>
/// Registered <em>alongside</em> Identity's own validator rather than replacing it, so length and the
/// uniqueness of the character set are still its job and these two are additional. Both refusals name
/// what is wrong specifically enough to act on, because "your password is invalid" is the message that
/// produces the next password on the same list.
/// </remarks>
public sealed class CmsPasswordValidator(IBreachedPasswordScreen screen) : IPasswordValidator<User>
{
    /// <summary>Error code for a password found in a breach corpus or on the common list.</summary>
    public const string BreachedCode = "PasswordBreached";

    /// <summary>Error code for a password containing the account's own name or address.</summary>
    public const string ContainsIdentityCode = "PasswordContainsIdentity";

    /// <inheritdoc />
    /// <remarks>
    /// The identity check runs first: it needs no I/O, and a password built out of the user's own
    /// email is refused for a reason they can fix without being told anything about breach corpora.
    /// </remarks>
    public async Task<IdentityResult> ValidateAsync(
        UserManager<User> manager,
        User user,
        string? password)
    {
        ArgumentNullException.ThrowIfNull(manager);
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(password))
        {
            return IdentityResult.Success;
        }

        if (ContainsIdentity(user, password))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = ContainsIdentityCode,
                Description = "Your password must not contain your name or email address.",
            });
        }

        if (await screen.IsBreachedAsync(password))
        {
            return IdentityResult.Failed(new IdentityError
            {
                Code = BreachedCode,
                Description =
                    "This password appears in a public breach or on a common-password list. " +
                    "Choose one you have not used anywhere else.",
            });
        }

        return IdentityResult.Success;
    }

    /// <summary>
    /// Whether the password is built out of the account it belongs to.
    /// </summary>
    /// <param name="user">The account.</param>
    /// <param name="password">The candidate.</param>
    /// <returns>Whether it contains the user name or the local part of the address.</returns>
    /// <remarks>
    /// The local part of the address rather than the whole of it: nobody puts
    /// <c>@contoso.example</c> in a password, and half the organisation shares the domain anyway, so
    /// matching on the full address would catch nothing. Four characters is the floor — below that the
    /// substring is common enough that this starts refusing unrelated passwords.
    /// </remarks>
    private static bool ContainsIdentity(User user, string password)
    {
        string?[] candidates =
        [
            user.UserName,
            user.Email is { Length: > 0 } email ? email.Split('@')[0] : null,
            user.FirstName,
            user.LastName,
        ];

        return candidates.Any(candidate =>
            candidate is { Length: >= 4 } &&
            password.Contains(candidate, StringComparison.OrdinalIgnoreCase));
    }
}
