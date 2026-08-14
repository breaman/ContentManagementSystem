using System.Text.Json;
using System.Text.Json.Serialization;

namespace ContentManagementSystem.Core.Structure;

/// <summary>
/// What kind of structural record a schema file describes.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<SchemaKind>))]
public enum SchemaKind
{
    /// <summary>A template and its zone definitions.</summary>
    Template = 0,

    /// <summary>A block type, its property definitions, and the groups it composes.</summary>
    BlockType = 1,

    /// <summary>A shared property group.</summary>
    Composition = 2,
}

/// <summary>
/// One file under <c>Server/CmsSchema/</c> (spec section 27.1).
/// </summary>
/// <param name="Kind">Which structural record this file describes.</param>
/// <param name="Key">Stable key of the record. The file's identity, not its filename.</param>
/// <param name="Name">Editor-facing name, applied when the record is first created.</param>
/// <param name="Description">Optional help text.</param>
/// <param name="Slots">Zone or property definitions, in editor order.</param>
/// <param name="IconKey">Block types only: icon shown in the picker.</param>
/// <param name="SummaryTemplate">Block types only: token pattern for a collapsed block's summary.</param>
/// <param name="SortOrder">Templates only: order in the create-page picker.</param>
/// <param name="Compositions">Block types only: keys of the groups composed into it, in order.</param>
/// <remarks>
/// One file per record rather than one manifest for the whole content model. The reason the format
/// exists at all is source control (spec section 27.1), and a single file would make every structural
/// change a conflict against every other one; a developer adding a zone to one template should be
/// touching one file.
/// <para>
/// The key inside the document is authoritative, not the filename. A file renamed in a merge, or
/// exported on a case-insensitive filesystem, must still describe the record it says it does.
/// </para>
/// </remarks>
public sealed record SchemaDocument(
    SchemaKind Kind,
    string Key,
    string? Name = null,
    string? Description = null,
    IReadOnlyList<SchemaSlot>? Slots = null,
    string? IconKey = null,
    string? SummaryTemplate = null,
    int SortOrder = 0,
    IReadOnlyList<string>? Compositions = null)
{
    /// <summary>The slots, never null.</summary>
    [JsonIgnore]
    public IReadOnlyList<SchemaSlot> Definitions => Slots ?? [];

    /// <summary>The composed group keys, never null.</summary>
    [JsonIgnore]
    public IReadOnlyList<string> ComposedKeys => Compositions ?? [];
}

/// <summary>
/// One zone or property definition inside a schema file.
/// </summary>
/// <param name="Key">Stable key. Immutable — the sync refuses to change what a key means.</param>
/// <param name="Name">Editor-facing label.</param>
/// <param name="FieldTypeKey">Key of the field type that fills the slot.</param>
/// <param name="Description">Optional help text.</param>
/// <param name="Configuration">Field-type configuration, embedded as an object.</param>
/// <param name="IsRequired">Whether an empty value blocks publishing.</param>
/// <param name="IsInlineEditable">Zones only: whether the zone participates in in-context editing.</param>
/// <param name="Group">Optional editor grouping.</param>
/// <param name="SortOrder">Order in the editor.</param>
public sealed record SchemaSlot(
    string Key,
    string Name,
    string FieldTypeKey,
    string? Description = null,
    JsonElement? Configuration = null,
    bool IsRequired = false,
    bool IsInlineEditable = false,
    string? Group = null,
    int SortOrder = 0);
