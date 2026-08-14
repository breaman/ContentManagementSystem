namespace ContentManagementSystem.Core.Structure;

/// <summary>What the schema sync would do, or did, to one record.</summary>
/// <param name="Kind">Which structural record it concerns.</param>
/// <param name="Key">Stable key of the record.</param>
/// <param name="Change">What kind of change it is.</param>
/// <param name="Detail">One line describing it, phrased for a CLI's output.</param>
public sealed record SchemaChange(SchemaKind Kind, string Key, SchemaChangeKind Change, string Detail);

/// <summary>The kinds of change a sync pass can produce.</summary>
public enum SchemaChangeKind
{
    /// <summary>The record does not exist and would be created.</summary>
    Created = 0,

    /// <summary>A zone or property in the file is not in the database and would be added.</summary>
    SlotAdded = 1,

    /// <summary>A zone or property exists and its editor-facing or validation settings differ.</summary>
    SlotUpdated = 2,

    /// <summary>
    /// The file asks for something the sync will not do, and the record is left as it is.
    /// </summary>
    /// <remarks>
    /// Reported rather than applied, and never silently: the two cases are a field-type change on an
    /// existing slot and a configuration the field type refuses. Both would take content that
    /// validates today and make it unreadable, which is precisely what "never destructive" rules out
    /// (spec sections 8.5 and 27.1).
    /// </remarks>
    Refused = 3,

    /// <summary>
    /// The database has a zone or property the file does not, and it is being kept.
    /// </summary>
    /// <remarks>
    /// Not a failure. The sync is additive, so a definition missing from a file is left alone rather
    /// than dropped — but it is reported, because it is also how a developer notices they forgot to
    /// export.
    /// </remarks>
    KeptUnlisted = 4,
}

/// <summary>
/// The outcome of a schema sync pass (task P1-26).
/// </summary>
/// <param name="Changes">Everything the pass would do or did, in file order.</param>
/// <param name="FilesRead">How many files were read.</param>
/// <param name="Errors">Files that could not be read or understood, one message each.</param>
/// <remarks>
/// The same report backs three callers: the startup pass logs it, <c>cms schema apply</c> prints it,
/// and <c>cms schema diff</c> prints it having computed it without saving. One computation means the
/// drift check in CI and the thing that runs at startup can never disagree.
/// </remarks>
public sealed record SchemaSyncReport(
    IReadOnlyList<SchemaChange> Changes,
    int FilesRead,
    IReadOnlyList<string> Errors)
{
    /// <summary>A pass that read nothing and found nothing.</summary>
    public static SchemaSyncReport Empty { get; } = new([], 0, []);

    /// <summary>Whether anything would change the database.</summary>
    /// <remarks>
    /// <see cref="SchemaChangeKind.KeptUnlisted"/> does not count: it describes what the sync is
    /// deliberately not doing, so a deployment whose only finding is an unlisted zone is in sync.
    /// </remarks>
    public bool HasPendingWork =>
        Changes.Any(change => change.Change
            is SchemaChangeKind.Created
            or SchemaChangeKind.SlotAdded
            or SchemaChangeKind.SlotUpdated);

    /// <summary>Whether anything was refused or could not be read.</summary>
    public bool HasProblems =>
        Errors.Count > 0 || Changes.Any(change => change.Change is SchemaChangeKind.Refused);
}
