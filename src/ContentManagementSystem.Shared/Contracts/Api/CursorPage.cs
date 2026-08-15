namespace ContentManagementSystem.Shared.Contracts.Api;

/// <summary>
/// One page of a cursor-paginated collection (spec section 22).
/// </summary>
/// <typeparam name="T">What the collection holds.</typeparam>
/// <param name="Items">The items in this page, in the collection's order.</param>
/// <param name="NextCursor">
/// Opaque token to send as <c>?cursor=</c> for the following page, or null when this is the last
/// one. A client tests for null rather than comparing <c>Items.Count</c> against the limit it asked
/// for — a full page can still be the final one.
/// </param>
/// <remarks>
/// <strong>There is deliberately no total count.</strong> A keyset-paginated query knows where it is
/// and not how long the collection is, and answering with a count means a second full scan on every
/// request — which is precisely the cost cursor pagination exists to avoid. A screen that needs to
/// say "1,204 pages" should ask for that number separately and cache it, rather than making every
/// page of every list pay for it.
/// </remarks>
public sealed record CursorPage<T>(IReadOnlyList<T> Items, string? NextCursor)
{
    /// <summary>An empty page with nothing after it.</summary>
    public static CursorPage<T> Empty { get; } = new([], null);
}
