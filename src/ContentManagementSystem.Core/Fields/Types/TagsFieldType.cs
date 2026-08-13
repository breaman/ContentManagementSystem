using System.Text;
using System.Text.Json;

using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Fields.Types;

/// <summary>
/// A free-form list of tags (spec section 7.1).
/// </summary>
/// <remarks>
/// Stored as <c>{ "type": "tags", "value": ["release-notes", "v2"] }</c> — the tag text itself, not
/// ids. The <c>Tag</c> and <c>PageTag</c> rows that P8 introduces are a projection built from these
/// values on save, in the same way <c>ContentReference</c> is: the payload stays the record, and the
/// relational side stays derivable from it.
/// <para>
/// Configuration keys: <c>required</c>, <c>min</c> / <c>max</c> counts, <c>maxLength</c> per tag.
/// </para>
/// <para>
/// Not reference-bearing, which is a real distinction rather than an omission: a tag names a
/// concept, not an entity, so nothing breaks when one stops being used and there is nothing for
/// where-used or cache invalidation to follow. That is also why it has no target type in
/// <see cref="ContentReferenceTargetType"/>.
/// </para>
/// <para>
/// <strong>Completed in P8</strong> with search and taxonomy: autocomplete against the existing
/// vocabulary, the tag projection, merge and rename of a tag across every page that carries it, and
/// tag-filtered listings.
/// </para>
/// </remarks>
public sealed class TagsFieldType : ListFieldTypeBase
{
    /// <inheritdoc />
    public override string Key => FieldTypeKeys.Tags;

    /// <inheritdoc />
    public override string DisplayName => "Tags";

    /// <inheritdoc />
    public override FieldTypeCapabilities Capabilities => FieldTypeCapabilities.Searchable;

    /// <inheritdoc />
    protected override string ItemNoun => "tags";

    /// <inheritdoc />
    protected override void ValidateItems(
        JsonElement items,
        FieldConfiguration configuration,
        ValidationMode mode,
        ref List<ValidationDiagnostic>? diagnostics)
    {
        var maxLength = configuration.GetInt32("maxLength");

        // Case-insensitive, because "Release Notes" and "release notes" are one tag to everyone
        // except a byte comparison, and storing both produces two entries in every tag list.
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;

        foreach (var item in items.EnumerateArray())
        {
            var path = ItemPath(index);

            index++;

            if (item.ValueKind is not JsonValueKind.String ||
                item.GetString() is not { } tag ||
                string.IsNullOrWhiteSpace(tag))
            {
                Diagnostics.AddError(
                    ref diagnostics,
                    FieldValidationCodes.Shape,
                    "A tag is a non-empty piece of text.",
                    path);

                continue;
            }

            if (maxLength is { } limit && tag.Length > limit)
            {
                Diagnostics.AddError(
                    ref diagnostics,
                    FieldValidationCodes.MaxLength,
                    $"Use at most {limit} characters per tag; this one is {tag.Length}.",
                    path);
            }

            if (!seen.Add(tag.Trim()))
            {
                Diagnostics.AddError(
                    ref diagnostics,
                    FieldValidationCodes.Duplicate,
                    $"'{tag}' is already applied.",
                    path);
            }
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Tags are indexed as words, so a page tagged "release-notes" is findable by searching for it
    /// without the tag having to appear in the prose.
    /// </remarks>
    public override string ExtractSearchText(JsonElement value)
    {
        if (GetValue(value) is not { ValueKind: JsonValueKind.Array } tags) return string.Empty;

        var builder = new StringBuilder();

        foreach (var tag in tags.EnumerateArray())
        {
            if (tag.ValueKind is not JsonValueKind.String || tag.GetString() is not { } text) continue;

            if (builder.Length > 0) builder.Append(' ');

            builder.Append(text);
        }

        return SearchText.Collapse(builder.ToString());
    }
}
