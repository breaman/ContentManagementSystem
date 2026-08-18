namespace ContentManagementSystem.Data.Interfaces;

/// <summary>
/// An entity that is retired by flag rather than removed from the table.
/// </summary>
/// <remarks>
/// Implementing this opts the entity into two things at once: the global query filter that hides
/// retired rows from ordinary queries, and <c>SoftDeleteInterceptor</c>, which rewrites a
/// stray <c>Remove</c> into a flag update. The second is a safety net rather than the intended path
/// — services expose explicit delete and restore operations — but it is what stops a hard delete of
/// a page from taking its entire version history with it (spec section 23.5).
/// </remarks>
public interface ISoftDeletable
{
    /// <summary>Whether the row is retired.</summary>
    bool IsDeleted { get; set; }

    /// <summary>When the row was retired. Null while it is live.</summary>
    DateTimeOffset? DeletedOn { get; set; }

    /// <summary>Identity of the user who retired the row. Null while it is live.</summary>
    int? DeletedBy { get; set; }
}
