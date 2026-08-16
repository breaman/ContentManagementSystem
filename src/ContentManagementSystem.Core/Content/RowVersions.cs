using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace ContentManagementSystem.Core.Content;

/// <summary>
/// Applies a caller-supplied <c>rowversion</c> as the concurrency token a write is judged against.
/// </summary>
/// <remarks>
/// <strong>Set as the entry's original value, never compared in code.</strong> A comparison would
/// check the token against the value <em>this request</em> has just read, leaving the window between
/// that read and the write unguarded — which is exactly the window two editors saving at once
/// occupy. Setting the original value moves the check into the <c>UPDATE</c> predicate, where the
/// database arbitrates it and a lost race surfaces as <c>DbUpdateConcurrencyException</c>.
/// <para>
/// Shared by the draft save and the metadata patch so the two cannot drift. They differ only in
/// whether the token is mandatory, which is the endpoint's decision rather than this one's.
/// </para>
/// </remarks>
public static class RowVersions
{
    /// <summary>Bytes a SQL Server <c>rowversion</c> occupies.</summary>
    private const int TokenLength = 8;

    /// <summary>Name of the concurrency-token property, which every guarded entity spells alike.</summary>
    private const string TokenProperty = nameof(PageVersion.RowVersion);

    /// <summary>
    /// Guards a write with the version the caller last saw.
    /// </summary>
    /// <param name="entry">Change-tracker entry for the row being written.</param>
    /// <param name="expected">The Base64 token, or null to write unconditionally.</param>
    /// <returns>
    /// <see langword="true"/> when the guard was applied, <see langword="null"/> when no token was
    /// supplied, and <see langword="false"/> when one was supplied and is not a token this server
    /// could have issued.
    /// </returns>
    /// <remarks>
    /// An unreadable token is distinguished from an absent one deliberately: silently treating
    /// garbage as "no precondition" would turn a client's encoding bug into an unguarded overwrite,
    /// which is the single failure the token exists to prevent.
    /// <para>
    /// The property is reached by name rather than by a typed lambda, so one implementation serves
    /// every guarded entity — a page version, a reusable item, a reusable version. A typed overload
    /// per entity would be four copies of the reasoning above, and the copy that drifted would be
    /// the one that compared the token in code.
    /// </para>
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The entity has no <c>RowVersion</c> property. That is a programming error rather than a bad
    /// request: an entity without a concurrency token cannot be guarded, and quietly writing it
    /// unguarded is the outcome this method exists to rule out.
    /// </exception>
    public static bool? TryApply(EntityEntry entry, string? expected)
    {
        ArgumentNullException.ThrowIfNull(entry);

        if (string.IsNullOrWhiteSpace(expected)) return null;

        Span<byte> buffer = stackalloc byte[TokenLength * 2];

        if (!Convert.TryFromBase64String(expected, buffer, out var written)) return false;

        entry.Property(TokenProperty).OriginalValue = buffer[..written].ToArray();

        return true;
    }
}
