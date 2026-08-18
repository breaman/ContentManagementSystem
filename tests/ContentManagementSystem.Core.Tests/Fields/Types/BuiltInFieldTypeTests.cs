using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Core.Tests.Fields.Types;

/// <summary>
/// The built-in catalog as a whole (task P1-10, spec section 7.1).
/// </summary>
public class BuiltInFieldTypeTests
{
    /// <summary>
    /// Every field type in the v1 catalog (spec section 7.1), in the order the registry returns
    /// them.
    /// </summary>
    private static readonly string[] ExpectedKeys =
    [
        FieldTypeKeys.Blocks,
        FieldTypeKeys.Boolean,
        FieldTypeKeys.Choice,
        FieldTypeKeys.Color,
        FieldTypeKeys.Date,
        FieldTypeKeys.DateTime,
        FieldTypeKeys.Html,
        FieldTypeKeys.Json,
        FieldTypeKeys.Link,
        FieldTypeKeys.Media,
        FieldTypeKeys.MediaList,
        FieldTypeKeys.MultilineText,
        FieldTypeKeys.Number,
        FieldTypeKeys.PageReference,
        FieldTypeKeys.PlainText,
        FieldTypeKeys.Reusable,
        FieldTypeKeys.RichText,
        FieldTypeKeys.Tags,
    ];

    [Test]
    public void EveryBuiltInFieldTypeIsRegisteredByTheScan()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        // Ordered by key, so the comparison also pins that the registry's ordering guarantee holds
        // for the real catalog rather than only for stubs.
        registry.All.Select(fieldType => fieldType.Key).Should().Equal(ExpectedKeys);
    }

    [Test]
    public void EveryBuiltInFieldTypeHasADisplayName()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        registry.All.Should().OnlyContain(fieldType => !string.IsNullOrWhiteSpace(fieldType.DisplayName));
    }

    [Test]
    public void RegisteringTheBuiltInsWithoutASanitizerFailsRatherThanStoringMarkupUnchecked()
    {
        using var provider = new ServiceCollection()
            .AddCmsFieldTypes()
            .BuildServiceProvider();

        var resolve = provider.GetRequiredService<IFieldTypeRegistry>;

        // The alternative to failing here is a deployment that quietly persists hostile markup.
        resolve.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public void OnlyTheFieldTypesThatPointAtSomethingClaimToBearReferences()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        // The complement of the P1-13 contract test, which runs the other way round. A field type
        // that cannot point at anything must not claim it can, or where-used and cache
        // invalidation keep asking it a question it will always answer emptily. Note tags is
        // absent: a tag names a concept, not an entity, and has no target type.
        registry.All
            .Where(fieldType => fieldType.Capabilities.HasFlag(FieldTypeCapabilities.ReferenceBearing))
            .Select(fieldType => fieldType.Key)
            .Should().BeEquivalentTo(
                FieldTypeKeys.Blocks,
                FieldTypeKeys.Link,
                FieldTypeKeys.Media,
                FieldTypeKeys.MediaList,
                FieldTypeKeys.PageReference,
                FieldTypeKeys.Reusable);
    }

    [Test]
    public void OnlyBlocksIsAContainer()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        // Container is load-bearing rather than descriptive: it is how the P1-13 contract test
        // knows which field types have to be exercised with a nested value as well as a flat one.
        registry.All
            .Where(fieldType => fieldType.Capabilities.HasFlag(FieldTypeCapabilities.Container))
            .Select(fieldType => fieldType.Key)
            .Should().BeEquivalentTo(FieldTypeKeys.Blocks);
    }

    [Test]
    public void OnlyTheMarkupBearingFieldTypesClaimSanitization()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        registry.All
            .Where(fieldType => fieldType.Capabilities.HasFlag(FieldTypeCapabilities.Sanitizable))
            .Select(fieldType => fieldType.Key)
            .Should().BeEquivalentTo(FieldTypeKeys.Html, FieldTypeKeys.RichText);
    }

    private static ServiceProvider BuildProvider() =>
        new ServiceCollection()
            .AddSingleton<IContentSanitizer, RecordingSanitizer>()
            .AddCmsFieldTypes()
            .BuildServiceProvider();
}
