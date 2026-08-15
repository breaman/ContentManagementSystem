namespace ContentManagementSystem.Shared.Contracts.Preview;

/// <summary>
/// Stable diagnostic codes returned by the preview services.
/// </summary>
/// <remarks>
/// Separate from <c>PageCodes</c> for the reason that list gives for its own existence: a code is
/// what a client switches on to offer a remedy, and the remedy for "this link expired" is nothing
/// like the remedy for "that version does not belong to this page". A code does not change once
/// shipped; the wording beside it may be rewritten freely (spec section 22.2).
/// </remarks>
public static class PreviewCodes
{
    /// <summary>The token addressed does not exist.</summary>
    public const string NotFound = "preview.not-found";

    /// <summary>The caller is authenticated but holds no role permitting this.</summary>
    public const string Forbidden = "preview.forbidden";

    /// <summary>The page or version a token was to be issued for does not exist.</summary>
    public const string VersionNotFound = "preview.version-not-found";

    /// <summary>
    /// The requested expiry is beyond the thirty days spec section 12.2 permits, or in the past.
    /// </summary>
    /// <remarks>
    /// Refused rather than clamped. A link an operator believes lasts a year and which actually
    /// lasts thirty days is a support ticket on day thirty-one, and the request that asked for it is
    /// the only place the mistake is still visible.
    /// </remarks>
    public const string ExpiryInvalid = "preview.expiry-invalid";

    /// <summary>The requested use limit is not a positive number.</summary>
    public const string MaxUsesInvalid = "preview.max-uses-invalid";

    /// <summary>The housekeeping note is longer than the column that stores it.</summary>
    public const string NoteTooLong = "preview.note-too-long";

    /// <summary>
    /// The token presented is not one this deployment issued, or has been revoked.
    /// </summary>
    /// <remarks>
    /// Deliberately one code for both. Telling the holder of a string that it <em>was</em> a real
    /// token narrows the search space for anybody probing, and the person who legitimately has a
    /// revoked link needs to talk to whoever sent it either way.
    /// </remarks>
    public const string TokenInvalid = "preview.token-invalid";

    /// <summary>The token was issued but its expiry has passed.</summary>
    public const string TokenExpired = "preview.token-expired";

    /// <summary>The token has been viewed as many times as it was issued for.</summary>
    public const string TokenExhausted = "preview.token-exhausted";

    /// <summary>
    /// The token is still valid but the content behind it is gone.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="TokenInvalid"/> because it sends the reviewer to a different person:
    /// a recycled page means asking the editor to restore it, whereas an invalid link means asking
    /// for a new one.
    /// </remarks>
    public const string PageUnavailable = "preview.page-unavailable";
}
