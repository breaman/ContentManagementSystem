using System.Diagnostics.CodeAnalysis;

namespace ContentManagementSystem.Core.Content.Schema;

/// <summary>
/// An in-memory <see cref="IContentSchemaCatalog"/> over a fixed set of revisions.
/// </summary>
/// <remarks>
/// The catalog a caller assembles once it has loaded the revisions a payload names — from
/// <c>TemplateRevision.ZoneSnapshotJson</c> and <c>BlockTypeRevision.PropertySnapshotJson</c>, which
/// <see cref="ContentSchemaSnapshot"/> reads. Immutable and thread-safe once built, so a
/// database-backed cache can hold instances of it and hand the same one to every request validating
/// against the same revisions.
/// </remarks>
public sealed class ContentSchemaCatalog : IContentSchemaCatalog
{
    /// <summary>A catalog that knows nothing, for callers validating an envelope alone.</summary>
    public static ContentSchemaCatalog Empty { get; } = new([], []);

    private readonly Dictionary<(string Key, int Revision), ContentSchema> _templates;
    private readonly Dictionary<(string Key, int Revision), BlockTypeSchema> _blockTypes;
    private readonly Dictionary<string, int> _newestBlockTypeRevisions;

    /// <summary>
    /// Creates a catalog.
    /// </summary>
    /// <param name="templates">Template revisions to serve.</param>
    /// <param name="blockTypes">Block type revisions to serve.</param>
    public ContentSchemaCatalog(
        IEnumerable<ContentSchema> templates,
        IEnumerable<BlockTypeSchema> blockTypes)
    {
        ArgumentNullException.ThrowIfNull(templates);
        ArgumentNullException.ThrowIfNull(blockTypes);

        _templates = [];
        _blockTypes = [];
        _newestBlockTypeRevisions = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var template in templates)
        {
            _templates[(template.TemplateKey, template.RevisionNumber)] = template;
        }

        foreach (var blockType in blockTypes)
        {
            _blockTypes[(blockType.BlockTypeKey, blockType.RevisionNumber)] = blockType;

            if (!_newestBlockTypeRevisions.TryGetValue(blockType.BlockTypeKey, out var newest) ||
                blockType.RevisionNumber > newest)
            {
                _newestBlockTypeRevisions[blockType.BlockTypeKey] = blockType.RevisionNumber;
            }
        }
    }

    /// <inheritdoc />
    public bool TryGetTemplate(
        string templateKey,
        int revisionNumber,
        [NotNullWhen(true)] out ContentSchema? schema) =>
        _templates.TryGetValue((templateKey, revisionNumber), out schema);

    /// <inheritdoc />
    public bool TryGetBlockType(
        string blockTypeKey,
        int? revisionNumber,
        [NotNullWhen(true)] out BlockTypeSchema? schema)
    {
        if (revisionNumber is null)
        {
            if (_newestBlockTypeRevisions.TryGetValue(blockTypeKey, out var newest))
            {
                return _blockTypes.TryGetValue((blockTypeKey, newest), out schema);
            }

            schema = null;

            return false;
        }

        return _blockTypes.TryGetValue((blockTypeKey, revisionNumber.Value), out schema);
    }
}
