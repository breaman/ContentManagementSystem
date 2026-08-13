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
    /// The value field types Phase 1 ships. The reference-bearing keys —<c>media</c>, <c>link</c>,
    /// <c>pageReference</c>, <c>reusable</c>, <c>blocks</c>, <c>tags</c> — arrive in P1-11.
    /// </summary>
    private static readonly string[] ExpectedKeys =
    [
        FieldTypeKeys.Boolean,
        FieldTypeKeys.Choice,
        FieldTypeKeys.Color,
        FieldTypeKeys.Date,
        FieldTypeKeys.DateTime,
        FieldTypeKeys.Html,
        FieldTypeKeys.Json,
        FieldTypeKeys.MultilineText,
        FieldTypeKeys.Number,
        FieldTypeKeys.PlainText,
        FieldTypeKeys.RichText,
    ];

    [Fact]
    public void EveryBuiltInValueFieldTypeIsRegisteredByTheScan()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        // Ordered by key, so the comparison also pins that the registry's ordering guarantee holds
        // for the real catalog rather than only for stubs.
        registry.All.Select(fieldType => fieldType.Key).Should().Equal(ExpectedKeys);
    }

    [Fact]
    public void EveryBuiltInFieldTypeHasADisplayName()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        registry.All.Should().OnlyContain(fieldType => !string.IsNullOrWhiteSpace(fieldType.DisplayName));
    }

    [Fact]
    public void RegisteringTheBuiltInsWithoutASanitizerFailsRatherThanStoringMarkupUnchecked()
    {
        using var provider = new ServiceCollection()
            .AddCmsFieldTypes()
            .BuildServiceProvider();

        var resolve = provider.GetRequiredService<IFieldTypeRegistry>;

        // The alternative to failing here is a deployment that quietly persists hostile markup.
        resolve.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NoValueFieldTypeClaimsToBearReferences()
    {
        using var provider = BuildProvider();

        var registry = provider.GetRequiredService<IFieldTypeRegistry>();

        // The contract test that matters runs the other way round — every ReferenceBearing field
        // type must return references for a populated value (P1-13). This is its complement: a
        // field type that cannot point at anything must not claim it can, or where-used starts
        // asking it questions it will always answer emptily.
        registry.All.Should().OnlyContain(fieldType =>
            !fieldType.Capabilities.HasFlag(FieldTypeCapabilities.ReferenceBearing));
    }

    [Fact]
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
