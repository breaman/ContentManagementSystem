using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Fields;
using ContentManagementSystem.Core.Security;

using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Contracts.Security;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Core.Tests.Security;

/// <summary>
/// Wiring (task P1-18): what a container does with and without a sanitizer registered.
/// </summary>
public class SanitizationRegistrationTests
{
    [Fact]
    public void AddCmsSanitizationSuppliesTheSanitizerTheFieldTypesNeed()
    {
        using var provider = new ServiceCollection()
            .AddCmsSanitization()
            .AddCmsFieldTypes()
            .BuildServiceProvider();

        provider.GetRequiredService<IFieldTypeRegistry>().Find(FieldTypeKeys.RichText).Should().NotBeNull();
    }

    [Fact]
    public void WithoutASanitizerTheFieldTypeRegistryRefusesToBuild()
    {
        using var provider = new ServiceCollection()
            .AddCmsFieldTypes()
            .BuildServiceProvider();

        // The intended behaviour, not an oversight: richText and html take an IContentSanitizer, so
        // a deployment that forgot to register one fails at startup instead of quietly storing
        // unsanitized markup for as long as nobody looks.
        var resolve = () => provider.GetRequiredService<IFieldTypeRegistry>();

        resolve.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void TheSanitizerIsASingleton()
    {
        using var provider = new ServiceCollection().AddCmsSanitization().BuildServiceProvider();

        // Each instance builds three configured HtmlSanitizers up front. Resolving per request would
        // pay that on every save and render, and the contract already promises thread safety.
        provider.GetRequiredService<IContentSanitizer>()
            .Should().BeSameAs(provider.GetRequiredService<IContentSanitizer>());
    }

    [Fact]
    public void ConfiguredOptionsReachTheService()
    {
        using var provider = new ServiceCollection()
            .AddCmsSanitization(options => options.AllowedCssClasses.Add("lead"))
            .BuildServiceProvider();

        provider.GetRequiredService<IContentSanitizer>()
            .Sanitize("<p class=\"lead\">x</p>", SanitizationProfile.Extended)
            .Should().Contain("class=\"lead\"");
    }

    [Fact]
    public void AddCmsContentSuppliesOneMarkdownRendererOverTheSameSanitizer()
    {
        using var provider = new ServiceCollection()
            .AddCmsSanitization()
            .AddCmsFieldTypes()
            .AddCmsContent()
            .BuildServiceProvider();

        // One registration is what makes acceptance criterion P1 #7 structural: the editor preview
        // and delivery cannot end up holding two pipelines that merely agree today.
        provider.GetRequiredService<IMarkdownRenderer>()
            .Should().BeSameAs(provider.GetRequiredService<IMarkdownRenderer>());
    }
}
