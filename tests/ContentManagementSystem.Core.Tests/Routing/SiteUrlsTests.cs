using ContentManagementSystem.Shared.Common;

namespace ContentManagementSystem.Core.Tests.Routing;

/// <summary>
/// URL normalization and the hash that carries the unique index (task P3-04, spec sections 10.3
/// and 23.5).
/// </summary>
/// <remarks>
/// Almost everything here is really one assertion said several ways: two spellings of the same
/// address must produce one hash. A normalizer that misses a case does not fail loudly — it produces
/// a second row in <c>PageRoute</c> that the unique index happily accepts and that no request ever
/// resolves to.
/// </remarks>
public class SiteUrlsTests
{
    [Theory]
    [InlineData("/about", "/about")]
    [InlineData("/About", "/about")]
    [InlineData("/about/", "/about")]
    [InlineData("about", "/about")]
    [InlineData("  /about  ", "/about")]
    [InlineData("/PRODUCTS/Widget/", "/products/widget")]
    [InlineData("/", "/")]
    [InlineData("", "/")]
    [InlineData(null, "/")]
    [InlineData("///", "/")]
    public void NormalizeProducesOneFormPerAddress(string? supplied, string expected) =>
        SiteUrls.Normalize(supplied).Should().Be(expected);

    [Fact]
    public void NormalizeDecodesPercentEscapesSoATypedUrlMatchesARequestPath()
    {
        // A request path reaches the application already decoded; a URL typed into the redirect
        // editor does not. Without this the two spellings would occupy separate rows.
        SiteUrls.Normalize("/our%20team").Should().Be("/our team");
        SiteUrls.Normalize("/caf%C3%A9").Should().Be("/café");
    }

    [Fact]
    public void NormalizeKeepsAQueryStringBecauseARedirectAuthorMayHaveMeantIt()
    {
        // Stripping it would silently accept a redirect row whose author meant the query to matter.
        // Routes never carry one, because the delivery endpoint hands over the path alone.
        SiteUrls.Normalize("/search?q=widgets").Should().Be("/search?q=widgets");
    }

    [Theory]
    [InlineData("/about", "/About/")]
    [InlineData("/products/widget", "products/Widget")]
    [InlineData("/", "")]
    public void TwoSpellingsOfOneAddressHashTheSame(string first, string second) =>
        SiteUrls.Hash(first).Should().Equal(SiteUrls.Hash(second));

    [Fact]
    public void DifferentAddressesHashDifferently() =>
        SiteUrls.Hash("/about").Should().NotEqual(SiteUrls.Hash("/about-us"));

    [Fact]
    public void AHashIsTheWidthTheColumnDeclares() =>
        SiteUrls.Hash("/anything").Should().HaveCount(SiteUrls.HashLength);

    [Theory]
    [InlineData(null, "products", "/products")]
    [InlineData("/", "products", "/products")]
    [InlineData("/products", "widget", "/products/widget")]
    [InlineData("/products/", "widget", "/products/widget")]
    [InlineData("/products", "Widget", "/products/widget")]
    public void CombineJoinsAnAncestorUrlToASlug(string? parent, string slug, string expected) =>
        SiteUrls.Combine(parent, slug).Should().Be(expected);

    [Theory]
    [InlineData("/products", "/products", true)]
    [InlineData("/products", "/products/widget", true)]
    [InlineData("/", "/anything", true)]
    [InlineData("/products", "/services", false)]
    public void IsSelfOrDescendantAnswersTheContainmentQuestion(
        string ancestor,
        string url,
        bool expected) =>
        SiteUrls.IsSelfOrDescendant(ancestor, url).Should().Be(expected);

    [Fact]
    public void ContainmentIsSegmentAwareRatherThanAPlainPrefixTest()
    {
        // The case a naive StartsWith gets wrong. On a redirect loop check it is the difference
        // between refusing a legitimate row and accepting a cycle.
        SiteUrls.IsSelfOrDescendant("/new", "/news").Should().BeFalse();
        SiteUrls.IsSelfOrDescendant("/new", "/new/york").Should().BeTrue();
    }
}
