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
    [Test]
    [Arguments("/about", "/about")]
    [Arguments("/About", "/about")]
    [Arguments("/about/", "/about")]
    [Arguments("about", "/about")]
    [Arguments("  /about  ", "/about")]
    [Arguments("/PRODUCTS/Widget/", "/products/widget")]
    [Arguments("/", "/")]
    [Arguments("", "/")]
    [Arguments(null, "/")]
    [Arguments("///", "/")]
    public void NormalizeProducesOneFormPerAddress(string? supplied, string expected) =>
        SiteUrls.Normalize(supplied).Should().Be(expected);

    [Test]
    public void NormalizeDecodesPercentEscapesSoATypedUrlMatchesARequestPath()
    {
        // A request path reaches the application already decoded; a URL typed into the redirect
        // editor does not. Without this the two spellings would occupy separate rows.
        SiteUrls.Normalize("/our%20team").Should().Be("/our team");
        SiteUrls.Normalize("/caf%C3%A9").Should().Be("/café");
    }

    [Test]
    public void NormalizeKeepsAQueryStringBecauseARedirectAuthorMayHaveMeantIt()
    {
        // Stripping it would silently accept a redirect row whose author meant the query to matter.
        // Routes never carry one, because the delivery endpoint hands over the path alone.
        SiteUrls.Normalize("/search?q=widgets").Should().Be("/search?q=widgets");
    }

    [Test]
    [Arguments("/about", "/About/")]
    [Arguments("/products/widget", "products/Widget")]
    [Arguments("/", "")]
    public void TwoSpellingsOfOneAddressHashTheSame(string first, string second) =>
        SiteUrls.Hash(first).Should().Equal(SiteUrls.Hash(second));

    [Test]
    public void DifferentAddressesHashDifferently() =>
        SiteUrls.Hash("/about").Should().NotEqual(SiteUrls.Hash("/about-us"));

    [Test]
    public void AHashIsTheWidthTheColumnDeclares() =>
        SiteUrls.Hash("/anything").Should().HaveCount(SiteUrls.HashLength);

    [Test]
    [Arguments(null, "products", "/products")]
    [Arguments("/", "products", "/products")]
    [Arguments("/products", "widget", "/products/widget")]
    [Arguments("/products/", "widget", "/products/widget")]
    [Arguments("/products", "Widget", "/products/widget")]
    public void CombineJoinsAnAncestorUrlToASlug(string? parent, string slug, string expected) =>
        SiteUrls.Combine(parent, slug).Should().Be(expected);

    [Test]
    [Arguments("/products", "/products", true)]
    [Arguments("/products", "/products/widget", true)]
    [Arguments("/", "/anything", true)]
    [Arguments("/products", "/services", false)]
    public void IsSelfOrDescendantAnswersTheContainmentQuestion(
        string ancestor,
        string url,
        bool expected) =>
        SiteUrls.IsSelfOrDescendant(ancestor, url).Should().Be(expected);

    [Test]
    public void ContainmentIsSegmentAwareRatherThanAPlainPrefixTest()
    {
        // The case a naive StartsWith gets wrong. On a redirect loop check it is the difference
        // between refusing a legitimate row and accepting a cycle.
        SiteUrls.IsSelfOrDescendant("/new", "/news").Should().BeFalse();
        SiteUrls.IsSelfOrDescendant("/new", "/new/york").Should().BeTrue();
    }
}
