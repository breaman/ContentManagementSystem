using ContentManagementSystem.Shared.Contracts.Structure;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.E2E.Tests;

/// <summary>
/// Feeds the structure screens a fixed content model so the accessibility gate has markup to check.
/// </summary>
/// <remarks>
/// Fixed rather than empty on purpose. Most of what axe has an opinion about — table headers, form
/// labels, button names, the reading order of a list — only exists once there are rows and controls
/// on the page, so a gate run against an empty state would pass while checking almost nothing.
/// </remarks>
public sealed class FakeStructureClient : IStructureClient
{
    /// <summary>Identity of the template and block type the screens are rendered against.</summary>
    public const int Id = 1;

    /// <inheritdoc />
    public Task<IReadOnlyList<TemplateSummary>> GetTemplatesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<TemplateSummary>>(
        [
            new TemplateSummary(Id, "marketing-landing", "Marketing landing page", "Hero and body.",
                "Rendering.Templates.MarketingLanding, ContentManagementSystem.Rendering",
                IsOrphaned: false, IsEnabled: true, CurrentRevision: 3, SortOrder: 0, ZoneCount: 2),
            // An orphan and a disabled template, so every badge the list can show is on the page.
            new TemplateSummary(2, "legacy-microsite", "Legacy microsite", null, null,
                IsOrphaned: true, IsEnabled: true, CurrentRevision: 1, SortOrder: 1, ZoneCount: 0),
            new TemplateSummary(3, "retired", "Retired shape", null, null,
                IsOrphaned: false, IsEnabled: false, CurrentRevision: 9, SortOrder: 2, ZoneCount: 1),
        ]);

    /// <inheritdoc />
    public Task<TemplateDetail?> GetTemplateAsync(int id, CancellationToken cancellationToken = default) =>
        Task.FromResult<TemplateDetail?>(new TemplateDetail(
            new TemplateSummary(Id, "marketing-landing", "Marketing landing page", "Hero and body.",
                "Rendering.Templates.MarketingLanding, ContentManagementSystem.Rendering",
                IsOrphaned: false, IsEnabled: true, CurrentRevision: 3, SortOrder: 0, ZoneCount: 2),
            [
                new ZoneDefinition(10, "heroTitle", "Hero title", "Shown as the page heading.",
                    "plainText", null, IsRequired: true, IsInlineEditable: false, "Hero", 0),
                new ZoneDefinition(11, "body", "Body", null, "richText", null,
                    IsRequired: false, IsInlineEditable: true, null, 1),
            ],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));

    /// <inheritdoc />
    public Task<IReadOnlyList<BlockTypeSummary>> GetBlockTypesAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<BlockTypeSummary>>(
        [
            new BlockTypeSummary(Id, "hero-banner", "Hero banner", "A full-width headline.", null,
                "image", "{headline}", IsOrphaned: false, IsBuiltIn: false, CurrentRevision: 2,
                PropertyCount: 2),
            new BlockTypeSummary(2, "rawHtml", "Raw HTML", "A single block of HTML.", null, "code",
                "{content}", IsOrphaned: false, IsBuiltIn: true, CurrentRevision: 1, PropertyCount: 1),
        ]);

    /// <inheritdoc />
    public Task<BlockTypeDetail?> GetBlockTypeAsync(int id, CancellationToken cancellationToken = default)
    {
        var summary = new BlockTypeSummary(Id, "hero-banner", "Hero banner", "A full-width headline.",
            null, "image", "{headline}", IsOrphaned: false, IsBuiltIn: false, CurrentRevision: 2,
            PropertyCount: 2);

        var own = new PropertyDefinition(20, "headline", "Headline", null, "plainText", null,
            IsRequired: true, Group: null, SortOrder: 0);

        // A composed property too, so the "edited on its composition" row — which renders no action
        // buttons — is on the page the gate checks.
        var composed = new PropertyDefinition(21, "marginTop", "Margin top", null, "number", null,
            IsRequired: false, Group: "Spacing", SortOrder: 0, CompositionKey: "spacing-options");

        return Task.FromResult<BlockTypeDetail?>(new BlockTypeDetail(
            summary,
            [own],
            [new CompositionBinding(5, "spacing-options", "Spacing options", 0, 1)],
            [own, composed],
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<FieldTypeDescriptor>> GetFieldTypesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<FieldTypeDescriptor>>(
        [
            Descriptor("plainText", "Plain text"),
            Descriptor("richText", "Rich text"),
            Descriptor("number", "Number"),
        ]);

    /// <inheritdoc />
    public Task<StructureClientResult<TemplateDetail>> CreateTemplateAsync(
        CreateTemplateRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(WriteMessage);

    /// <inheritdoc />
    public Task<StructureClientResult<ZoneSaveResult>> CreateZoneAsync(
        int templateId,
        CreateZoneRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(WriteMessage);

    /// <inheritdoc />
    public Task<StructureClientResult<ZoneSaveResult>> UpdateZoneAsync(
        int templateId,
        int zoneId,
        UpdateZoneRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(WriteMessage);

    /// <inheritdoc />
    public Task<StructureClientResult<ZoneRemovalResult>> DeleteZoneAsync(
        int templateId,
        int zoneId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(WriteMessage);

    /// <inheritdoc />
    public Task<StructureClientResult<BlockTypeDetail>> CreateBlockTypeAsync(
        CreateBlockTypeRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(WriteMessage);

    /// <inheritdoc />
    public Task<StructureClientResult<PropertySaveResult>> CreatePropertyAsync(
        int blockTypeId,
        CreatePropertyRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(WriteMessage);

    /// <inheritdoc />
    public Task<StructureClientResult<PropertySaveResult>> UpdatePropertyAsync(
        int blockTypeId,
        int propertyId,
        UpdatePropertyRequest request,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(WriteMessage);

    /// <inheritdoc />
    public Task<StructureClientResult<PropertyRemovalResult>> DeletePropertyAsync(
        int blockTypeId,
        int propertyId,
        CancellationToken cancellationToken = default) =>
        throw new NotSupportedException(WriteMessage);

    /// <summary>
    /// Why the writes throw rather than returning a canned success.
    /// </summary>
    /// <remarks>
    /// A static render never submits a form, so reaching one of these means the gate has started
    /// testing behaviour instead of markup — which belongs in the API suite, against a real database.
    /// Throwing says so; a canned result would hide it.
    /// </remarks>
    private const string WriteMessage =
        "The accessibility gate renders the structure screens statically and never submits them. " +
        "Writes are covered by the API integration suite.";

    private static FieldTypeDescriptor Descriptor(string key, string displayName) =>
        new(key, displayName, ["Searchable"], IsContainer: false, IsDeveloperOnly: false,
            System.Text.Json.JsonDocument.Parse(
                """{"$schema":"https://json-schema.org/draft/2020-12/schema","type":"object","properties":{}}""")
                .RootElement.Clone());
}
