namespace ContentManagementSystem.Shared.Contracts.Workflow;

/// <summary>
/// A filter over the audit log (task P7-20, spec section 21.1).
/// </summary>
/// <param name="Entity">Table name, such as <c>Page</c>. Null for every table.</param>
/// <param name="EntityId">
/// Primary key of the row, as the audit log stores it. Only meaningful together with
/// <paramref name="Entity"/>.
/// </param>
/// <param name="UserId">Who did it. Null for everybody.</param>
/// <param name="From">Earliest instant to include.</param>
/// <param name="To">Latest instant to include.</param>
/// <param name="Cursor">Opaque paging token from a previous page, or null for the first.</param>
/// <param name="Limit">Most rows to return.</param>
public sealed record AuditQuery(
    string? Entity = null,
    string? EntityId = null,
    int? UserId = null,
    DateTimeOffset? From = null,
    DateTimeOffset? To = null,
    string? Cursor = null,
    int Limit = 50);

/// <summary>
/// One audit entry, as the viewer lists it.
/// </summary>
/// <param name="Id">Identity of the entry.</param>
/// <param name="Entity">The table that changed.</param>
/// <param name="EntityId">Primary key of the row that changed.</param>
/// <param name="Type">Whether the row was created, updated, or deleted.</param>
/// <param name="UserId">Who did it.</param>
/// <param name="UserName">Their name, so the answer reads as a person rather than a number.</param>
/// <param name="When">When.</param>
/// <param name="ChangedColumns">Which columns changed, comma-separated, on an update.</param>
/// <param name="OldValues">The row's previous values, as stored JSON.</param>
/// <param name="NewValues">The row's new values, as stored JSON.</param>
/// <remarks>
/// The two value documents are returned verbatim and are rendered as text, never as markup: they
/// contain whatever an editor typed, which is the point of keeping them and the reason they cannot
/// be trusted to a component that interpolates HTML.
/// </remarks>
public sealed record AuditEntrySummary(
    int Id,
    string Entity,
    string EntityId,
    string Type,
    int UserId,
    string? UserName,
    DateTimeOffset When,
    string? ChangedColumns,
    string? OldValues,
    string? NewValues);
