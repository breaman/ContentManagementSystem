using System.Text.Json;

using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Fields.Types;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Shared.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.EntityFrameworkCore;

namespace ContentManagementSystem.Core.Publishing;

/// <summary>
/// Compares two versions of a page (task P2-14, spec section 11.4).
/// </summary>
/// <remarks>
/// Structural rather than textual. A JSON text diff of two payloads is unreadable in exactly the
/// cases an editor needs one — a reordered list of blocks reads as everything after the moved block
/// being deleted and re-added — so blocks are matched on their stable GUID and only then compared.
/// <para>
/// <strong>On demand, never in the publish path.</strong> The cost of a diff is unbounded in the
/// size of the content; the publish transaction has to be quick and atomic, and nothing about it
/// needs to know what changed.
/// </para>
/// </remarks>
public interface IContentDiffService
{
    /// <summary>
    /// Compares two versions of the same page.
    /// </summary>
    /// <param name="pageId">Identity of the page.</param>
    /// <param name="fromVersionId">The earlier version.</param>
    /// <param name="toVersionId">The later version.</param>
    /// <param name="cancellationToken">Token observed while querying.</param>
    /// <returns>The difference, or a not-found result when either version is not this page's.</returns>
    Task<CmsResult<ContentDiff>> CompareAsync(
        int pageId,
        int fromVersionId,
        int toVersionId,
        CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IContentDiffService" />
/// <param name="context">The application database context.</param>
/// <param name="registry">The field types, which is what renders a value comparably.</param>
/// <param name="authorization">What the caller of the current request may do.</param>
public sealed class ContentDiffService(
    ApplicationDbContext context,
    IFieldTypeRegistry registry,
    ICmsAuthorization authorization) : IContentDiffService
{
    /// <inheritdoc />
    public async Task<CmsResult<ContentDiff>> CompareAsync(
        int pageId,
        int fromVersionId,
        int toVersionId,
        CancellationToken cancellationToken = default)
    {
        if (!authorization.HasPermission(CmsPermissions.ContentRead))
        {
            return CmsResult<ContentDiff>.Forbidden("Reading pages is not permitted.", PageCodes.Forbidden);
        }

        var versions = await context.PageVersions
            .AsNoTracking()
            .Where(version => version.PageId == pageId &&
                (version.Id == fromVersionId || version.Id == toVersionId))
            .ToListAsync(cancellationToken);

        var from = versions.FirstOrDefault(version => version.Id == fromVersionId);
        var to = versions.FirstOrDefault(version => version.Id == toVersionId);

        if (from is null || to is null)
        {
            return CmsResult<ContentDiff>.NotFound(
                $"Page {pageId} does not have both version {fromVersionId} and version {toVersionId}.",
                PageCodes.VersionNotFound);
        }

        return CmsResult<ContentDiff>.Success(new ContentDiff(
            pageId,
            from.Id,
            to.Id,
            from.VersionNumber,
            to.VersionNumber,
            CompareMetadata(from, to),
            CompareZones(from, to)));
    }

    /// <summary>
    /// Compares the versioned metadata as a flat property list.
    /// </summary>
    /// <remarks>
    /// Deliberately hand-listed rather than reflected over the entity. Reflection would sweep in the
    /// row version, the audit stamps, and the foreign keys, and every one of those differs between
    /// two versions by definition — a diff in which everything always changed says nothing.
    /// </remarks>
    private static IReadOnlyList<MetadataChange> CompareMetadata(PageVersion from, PageVersion to)
    {
        var changes = new List<MetadataChange>();

        void Compare(string name, string? before, string? after)
        {
            if (!string.Equals(before, after, StringComparison.Ordinal))
            {
                changes.Add(new MetadataChange(name, before, after));
            }
        }

        Compare(nameof(PageVersion.Title), from.Title, to.Title);
        Compare(
            nameof(PageVersion.TemplateRevision),
            from.TemplateRevision.ToString(),
            to.TemplateRevision.ToString());
        Compare(nameof(PageVersion.MetaTitle), from.MetaTitle, to.MetaTitle);
        Compare(nameof(PageVersion.MetaDescription), from.MetaDescription, to.MetaDescription);
        Compare(nameof(PageVersion.CanonicalUrl), from.CanonicalUrl, to.CanonicalUrl);
        Compare(nameof(PageVersion.RobotsIndex), from.RobotsIndex.ToString(), to.RobotsIndex.ToString());
        Compare(nameof(PageVersion.RobotsFollow), from.RobotsFollow.ToString(), to.RobotsFollow.ToString());
        Compare(nameof(PageVersion.OgTitle), from.OgTitle, to.OgTitle);
        Compare(nameof(PageVersion.OgDescription), from.OgDescription, to.OgDescription);
        Compare(
            nameof(PageVersion.OgImageMediaId),
            from.OgImageMediaId?.ToString(),
            to.OgImageMediaId?.ToString());
        Compare(nameof(PageVersion.OgType), from.OgType, to.OgType);
        Compare(nameof(PageVersion.TwitterCard), from.TwitterCard, to.TwitterCard);
        Compare(nameof(PageVersion.StructuredDataJson), from.StructuredDataJson, to.StructuredDataJson);
        Compare(nameof(PageVersion.ChangeFreq), from.ChangeFreq, to.ChangeFreq);
        Compare(nameof(PageVersion.Priority), from.Priority?.ToString(), to.Priority?.ToString());

        return changes;
    }

    private IReadOnlyList<ZoneChange> CompareZones(PageVersion from, PageVersion to)
    {
        var before = ContentPayload.TryParse(from.ContentJson, out var parsedFrom) ? parsedFrom : null;
        var after = ContentPayload.TryParse(to.ContentJson, out var parsedTo) ? parsedTo : null;

        var changes = new List<ZoneChange>();

        // The later version's order first, then anything only the earlier one had. A removed zone
        // has no position in the new document, and appending it is the only honest place to put it.
        var keys = new List<string>(after?.ZoneKeys ?? []);
        keys.AddRange((before?.ZoneKeys ?? []).Where(key => !keys.Contains(key, StringComparer.Ordinal)));

        foreach (var key in keys)
        {
            var leftState = before?.GetZoneState(key) ?? ContentValueState.Absent;
            var rightState = after?.GetZoneState(key) ?? ContentValueState.Absent;
            var left = before?.GetZone(key) ?? default;
            var right = after?.GetZone(key) ?? default;

            if (leftState is ContentValueState.Absent && rightState is ContentValueState.Absent) continue;

            if (leftState is ContentValueState.Absent)
            {
                changes.Add(Zone(key, ContentChangeKind.Added, left, right));

                continue;
            }

            if (rightState is ContentValueState.Absent)
            {
                changes.Add(Zone(key, ContentChangeKind.Removed, left, right));

                continue;
            }

            if (SameJson(left, right)) continue;

            changes.Add(Zone(key, ContentChangeKind.Changed, left, right));
        }

        return changes;
    }

    private ZoneChange Zone(string key, ContentChangeKind kind, JsonElement left, JsonElement right)
    {
        var fieldTypeKey = TypeKey(right) ?? TypeKey(left);
        var blocks = HasItems(left) || HasItems(right)
            ? CompareBlocks(left, right)
            : [];

        // A container reports its changes block by block, and rendering it as text as well would put
        // the whole zone's words beside a precise account of what actually moved.
        if (blocks.Count > 0 || HasItems(left) || HasItems(right))
        {
            return new ZoneChange(key, fieldTypeKey, kind, null, null, [], blocks);
        }

        var before = Render(left);
        var after = Render(right);

        return new ZoneChange(
            key,
            fieldTypeKey,
            kind,
            before,
            after,
            IsReferenceBearing(fieldTypeKey) ? [] : WordDiff.Compute(before, after),
            []);
    }

    /// <summary>
    /// Matches block instances by their stable GUID and compares the ones that appear in both.
    /// </summary>
    /// <remarks>
    /// The identity is the <c>id</c> member the <c>blocks</c> field type writes, not the position.
    /// That is what turns a drag-and-drop reorder into one <see cref="ContentChangeKind.Moved"/>
    /// entry instead of a wall of removals and additions (acceptance criterion P2 #6).
    /// </remarks>
    private List<BlockChange> CompareBlocks(JsonElement left, JsonElement right)
    {
        var before = ReadBlocks(left);
        var after = ReadBlocks(right);
        var changes = new List<BlockChange>();

        foreach (var (id, block) in after)
        {
            if (!before.TryGetValue(id, out var was))
            {
                changes.Add(new BlockChange(
                    id, BlockTypeKey(block.Element), ContentChangeKind.Added, null, block.Index, []));

                continue;
            }

            var properties = CompareProperties(was.Element, block.Element);

            if (properties.Count > 0)
            {
                changes.Add(new BlockChange(
                    id,
                    BlockTypeKey(block.Element),
                    ContentChangeKind.Changed,
                    was.Index,
                    block.Index,
                    properties));

                continue;
            }

            if (was.Index != block.Index)
            {
                changes.Add(new BlockChange(
                    id, BlockTypeKey(block.Element), ContentChangeKind.Moved, was.Index, block.Index, []));
            }
        }

        foreach (var (id, block) in before)
        {
            if (!after.ContainsKey(id))
            {
                changes.Add(new BlockChange(
                    id, BlockTypeKey(block.Element), ContentChangeKind.Removed, block.Index, null, []));
            }
        }

        return [.. changes.OrderBy(change => change.AfterIndex ?? change.BeforeIndex ?? 0)];
    }

    private List<PropertyChange> CompareProperties(JsonElement before, JsonElement after)
    {
        var left = Member(before, BlocksFieldType.PropertiesMember);
        var right = Member(after, BlocksFieldType.PropertiesMember);
        var changes = new List<PropertyChange>();

        var keys = new List<string>();
        if (right.ValueKind is JsonValueKind.Object)
        {
            keys.AddRange(right.EnumerateObject().Select(property => property.Name));
        }

        if (left.ValueKind is JsonValueKind.Object)
        {
            keys.AddRange(left.EnumerateObject()
                .Select(property => property.Name)
                .Where(name => !keys.Contains(name, StringComparer.Ordinal)));
        }

        foreach (var key in keys)
        {
            var hasLeft = left.ValueKind is JsonValueKind.Object && left.TryGetProperty(key, out _);
            var hasRight = right.ValueKind is JsonValueKind.Object && right.TryGetProperty(key, out _);
            var leftValue = Member(left, key);
            var rightValue = Member(right, key);

            if (hasLeft && hasRight && SameJson(leftValue, rightValue)) continue;

            var kind = (hasLeft, hasRight) switch
            {
                (false, true) => ContentChangeKind.Added,
                (true, false) => ContentChangeKind.Removed,
                _ => ContentChangeKind.Changed,
            };

            var fieldTypeKey = TypeKey(rightValue) ?? TypeKey(leftValue);
            var renderedBefore = Render(leftValue);
            var renderedAfter = Render(rightValue);

            changes.Add(new PropertyChange(
                key,
                fieldTypeKey,
                kind,
                renderedBefore,
                renderedAfter,
                IsReferenceBearing(fieldTypeKey) ? [] : WordDiff.Compute(renderedBefore, renderedAfter)));
        }

        return changes;
    }

    /// <summary>
    /// Renders a stored value as the text a person compares.
    /// </summary>
    /// <remarks>
    /// Delegated to the field type that wrote it, by the stored discriminator — a value has to be
    /// read by whatever wrote it. A reference-bearing value renders as the identities it points at
    /// instead, because "Media 12 → Media 15" is the change, and the alt text beside it is not.
    /// The human labels those ids resolve to arrive with the media library in Phase 5.
    /// </remarks>
    private string? Render(JsonElement value)
    {
        if (value.ValueKind is JsonValueKind.Undefined) return null;
        if (value.ValueKind is JsonValueKind.Null) return null;

        if (TypeKey(value) is not { } key || registry.Find(key) is not { } fieldType)
        {
            // No field type to ask, which happens for content written by a build that had one. The
            // raw document is worse than a rendering and far better than reporting no change.
            return value.ToString();
        }

        if (fieldType.Capabilities.HasFlag(FieldTypeCapabilities.ReferenceBearing))
        {
            var targets = fieldType.ExtractReferences(value)
                .Select(reference => $"{reference.TargetType} {reference.TargetId}")
                .ToList();

            return targets.Count == 0 ? null : string.Join(", ", targets);
        }

        var text = fieldType.ExtractSearchText(value);

        return string.IsNullOrWhiteSpace(text) ? null : text;
    }

    private bool IsReferenceBearing(string? fieldTypeKey) =>
        fieldTypeKey is not null &&
        registry.Find(fieldTypeKey) is { } fieldType &&
        fieldType.Capabilities.HasFlag(FieldTypeCapabilities.ReferenceBearing);

    /// <summary>Reads a container's items into a map from block id to element and position.</summary>
    private static Dictionary<Guid, (JsonElement Element, int Index)> ReadBlocks(JsonElement value)
    {
        var blocks = new Dictionary<Guid, (JsonElement, int)>();
        var items = Member(value, BlocksFieldType.ItemsMember);

        if (items.ValueKind is not JsonValueKind.Array) return blocks;

        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            if (item.ValueKind is JsonValueKind.Object &&
                item.TryGetProperty(BlocksFieldType.IdMember, out var id) &&
                id.ValueKind is JsonValueKind.String &&
                Guid.TryParse(id.GetString(), out var parsed))
            {
                // A duplicate id is a malformed payload the blocks field type already reports on.
                // Keeping the first occurrence means the diff still renders rather than throwing.
                blocks.TryAdd(parsed, (item, index));
            }

            index++;
        }

        return blocks;
    }

    private static bool HasItems(JsonElement value) =>
        Member(value, BlocksFieldType.ItemsMember).ValueKind is JsonValueKind.Array;

    private static string? BlockTypeKey(JsonElement block) =>
        block.TryGetProperty(BlocksFieldType.BlockTypeKeyMember, out var key) &&
        key.ValueKind is JsonValueKind.String
            ? key.GetString()
            : null;

    private static string? TypeKey(JsonElement value) =>
        value.ValueKind is JsonValueKind.Object &&
        value.TryGetProperty(ContentPayloadMembers.Type, out var type) &&
        type.ValueKind is JsonValueKind.String
            ? type.GetString()
            : null;

    private static JsonElement Member(JsonElement owner, string name) =>
        owner.ValueKind is JsonValueKind.Object && owner.TryGetProperty(name, out var value)
            ? value
            : default;

    /// <summary>
    /// Whether two values are the same content.
    /// </summary>
    /// <remarks>
    /// Compared as canonical text rather than by walking the two documents. Member order is not
    /// meaningful inside a stored value — <c>ContentPayloadBuilder</c> preserves zone order for the
    /// diff's benefit, but nothing preserves it inside a block's properties — so the raw text is
    /// re-serialised before comparison.
    /// </remarks>
    private static bool SameJson(JsonElement left, JsonElement right)
    {
        if (left.ValueKind != right.ValueKind) return false;

        return string.Equals(Canonicalize(left), Canonicalize(right), StringComparison.Ordinal);
    }

    private static string Canonicalize(JsonElement value)
    {
        if (value.ValueKind is not JsonValueKind.Object && value.ValueKind is not JsonValueKind.Array)
        {
            return value.ToString();
        }

        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            Write(value, writer);
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void Write(JsonElement value, Utf8JsonWriter writer)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();

                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    Write(property.Value, writer);
                }

                writer.WriteEndObject();

                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();

                foreach (var item in value.EnumerateArray())
                {
                    Write(item, writer);
                }

                writer.WriteEndArray();

                break;

            default:
                value.WriteTo(writer);

                break;
        }
    }
}
