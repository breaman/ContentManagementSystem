using System.Collections.Concurrent;

namespace ContentManagementSystem.Core.Content.Schema;

/// <summary>
/// A process-wide cache of captured schema revisions.
/// </summary>
/// <remarks>
/// Safe to hold forever, because the rows behind it are immutable by construction: a revision's
/// snapshot is written when the revision is cut and never updated, and a structural change cuts a
/// <em>new</em> revision rather than editing the old one (spec section 8.5). That is what turns
/// schema resolution — asked on every draft save, every publish, and every render of content that
/// names a revision — into a dictionary lookup after the first time.
/// <para>
/// Nothing is evicted. The bound is the number of revisions a deployment has ever cut, each entry
/// being a handful of slot definitions; a site that has cut enough of them to matter has a content
/// model problem long before it has a memory problem. A cap is enforced anyway, so a runaway
/// migration script cannot make this the reason a process dies.
/// </para>
/// </remarks>
public sealed class ContentSchemaCache
{
    /// <summary>
    /// Entries held before the cache stops admitting new ones.
    /// </summary>
    /// <remarks>
    /// Not a working-set estimate: it is a ceiling far above any real content model, and reaching it
    /// means something is generating revisions in a loop. Refusing to grow past it keeps that a
    /// slow deployment rather than an out-of-memory one, and the miss path still answers correctly.
    /// </remarks>
    public const int Capacity = 20_000;

    private readonly ConcurrentDictionary<(string Key, int Revision), ContentSchema> _templates = new();
    private readonly ConcurrentDictionary<(string Key, int Revision), BlockTypeSchema> _blockTypes = new();

    /// <summary>Looks up a cached template revision.</summary>
    /// <param name="key">Key of the template.</param>
    /// <param name="revision">The revision number.</param>
    /// <returns>The schema, or null when it has not been loaded.</returns>
    public ContentSchema? FindTemplate(string key, int revision) =>
        _templates.TryGetValue((key, revision), out var schema) ? schema : null;

    /// <summary>Looks up a cached block type revision.</summary>
    /// <param name="key">Key of the block type.</param>
    /// <param name="revision">The revision number.</param>
    /// <returns>The schema, or null when it has not been loaded.</returns>
    public BlockTypeSchema? FindBlockType(string key, int revision) =>
        _blockTypes.TryGetValue((key, revision), out var schema) ? schema : null;

    /// <summary>Caches a template revision.</summary>
    /// <param name="schema">The schema to cache.</param>
    public void Add(ContentSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (_templates.Count < Capacity)
        {
            _templates[(schema.TemplateKey, schema.RevisionNumber)] = schema;
        }
    }

    /// <summary>Caches a block type revision.</summary>
    /// <param name="schema">The schema to cache.</param>
    public void Add(BlockTypeSchema schema)
    {
        ArgumentNullException.ThrowIfNull(schema);

        if (_blockTypes.Count < Capacity)
        {
            _blockTypes[(schema.BlockTypeKey, schema.RevisionNumber)] = schema;
        }
    }
}
