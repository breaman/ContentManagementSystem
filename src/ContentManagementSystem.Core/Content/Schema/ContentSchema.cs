namespace ContentManagementSystem.Core.Content.Schema;

/// <summary>
/// The zone definitions of one template revision — the schema a payload is checked against.
/// </summary>
/// <remarks>
/// A snapshot, not a live view of the structure tables. A page version records the revision it was
/// authored against and is validated and rendered against <em>that</em> snapshot, which is what makes
/// a template change unable to retroactively alter published content (spec section 8.5).
/// <para>
/// Immutable and safe to cache per revision, along with the parsed configuration hanging off each
/// zone.
/// </para>
/// </remarks>
public sealed class ContentSchema
{
    private readonly Dictionary<string, ContentPropertySchema> _byKey;

    /// <summary>
    /// Creates a template revision's schema.
    /// </summary>
    /// <param name="templateKey">Key of the template.</param>
    /// <param name="revisionNumber">The revision number this schema is a snapshot of.</param>
    /// <param name="zones">The zone definitions, in editor order.</param>
    /// <exception cref="ArgumentException">Two zones share a key.</exception>
    public ContentSchema(string templateKey, int revisionNumber, IEnumerable<ContentPropertySchema> zones)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(templateKey);
        ArgumentNullException.ThrowIfNull(zones);

        TemplateKey = templateKey;
        RevisionNumber = revisionNumber;
        Zones = [.. zones];
        // Ordinal: a zone key is an identifier written into stored payloads, so 'Hero' and 'hero' are
        // two different zones and a culture must never get a say in that.
        _byKey = new Dictionary<string, ContentPropertySchema>(Zones.Count, StringComparer.Ordinal);

        foreach (var zone in Zones)
        {
            if (!_byKey.TryAdd(zone.Key, zone))
            {
                throw new ArgumentException(
                    $"Template '{templateKey}' revision {revisionNumber} declares zone '{zone.Key}' twice.",
                    nameof(zones));
            }
        }
    }

    /// <summary>Key of the template this is a revision of.</summary>
    public string TemplateKey { get; }

    /// <summary>The revision number, as captured in the payload's <c>templateRevision</c>.</summary>
    public int RevisionNumber { get; }

    /// <summary>The zone definitions, in editor order.</summary>
    public IReadOnlyList<ContentPropertySchema> Zones { get; }

    /// <summary>Finds a zone definition by key.</summary>
    /// <param name="zoneKey">The zone key.</param>
    /// <returns>The zone, or null when the revision does not declare one by that key.</returns>
    public ContentPropertySchema? FindZone(string zoneKey) =>
        _byKey.GetValueOrDefault(zoneKey);

    /// <summary>Whether the revision declares a zone by this key.</summary>
    /// <param name="zoneKey">The zone key.</param>
    /// <returns><see langword="true"/> when the zone is declared.</returns>
    /// <remarks>
    /// The orphan sweep's question: a payload key this answers false for is content whose zone has
    /// been removed from the template, and is retained rather than discarded (spec section 8.5).
    /// </remarks>
    public bool DeclaresZone(string zoneKey) => _byKey.ContainsKey(zoneKey);
}
