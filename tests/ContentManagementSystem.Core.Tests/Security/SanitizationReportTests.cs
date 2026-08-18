using ContentManagementSystem.Core.Security;

using ContentManagementSystem.Shared.Contracts.Security;

namespace ContentManagementSystem.Core.Tests.Security;

/// <summary>
/// What the sanitizer says it took out (task P1-20, spec section 14.4).
/// </summary>
/// <remarks>
/// The report is what the HTML editor's "this will be stripped on save" banner is built from
/// (task P6-13). A report that is merely non-empty would satisfy the corpus suite and still be
/// useless to an author, so the kinds and the names are asserted here rather than only the count.
/// </remarks>
public class SanitizationReportTests
{
    private readonly SanitizationService _sanitizer = new();

    [Test]
    public void ARemovedElementIsNamed()
    {
        var result = _sanitizer.SanitizeWithReport("<p>x</p><script>alert(1)</script>", SanitizationProfile.Basic);

        result.Removals.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new SanitizationRemoval(SanitizationRemovalKind.Tag, "script"));
    }

    [Test]
    public void ARemovedEventHandlerNamesTheAttributeAndItsElement()
    {
        var result = _sanitizer.SanitizeWithReport(
            "<p onmouseover=\"alert(1)\">x</p>",
            SanitizationProfile.Basic);

        var removal = result.Removals.Should().ContainSingle().Subject;

        removal.Kind.Should().Be(SanitizationRemovalKind.Attribute);
        removal.Name.Should().Be("onmouseover");
        removal.TagName.Should().Be("p");
        removal.Value.Should().Be("alert(1)");
    }

    [Test]
    public void ARefusedUrlIsReportedApartFromADisallowedAttribute()
    {
        var result = _sanitizer.SanitizeWithReport(
            "<a href=\"javascript:alert(1)\">x</a>",
            SanitizationProfile.Basic);

        // The two mean different things to an author. A disallowed attribute is markup they should
        // not have written; a refused URL is usually a link they meant, spelled unacceptably.
        result.Removals.Should().ContainSingle()
            .Which.Kind.Should().Be(SanitizationRemovalKind.Url);
    }

    [Test]
    public void ARemovedStyleNamesTheProperty()
    {
        var result = _sanitizer.SanitizeWithReport(
            "<div style=\"text-align: center; position: fixed\">x</div>",
            SanitizationProfile.Extended);

        result.Removals.Select(removal => removal.Name).Should().Contain("position");
        result.Removals.Should().Contain(removal => removal.Kind == SanitizationRemovalKind.Style);
    }

    [Test]
    public void AnEmptiedAttributeIsNotReportedTwice()
    {
        var result = _sanitizer.SanitizeWithReport(
            "<div style=\"position: fixed\">x</div>",
            SanitizationProfile.Extended);

        // The property goes, and the now-empty style attribute goes with it. Reporting both would
        // show an author two removals for one edit.
        result.Removals.Should().ContainSingle()
            .Which.Kind.Should().Be(SanitizationRemovalKind.Style);
    }

    [Test]
    public void ARemovedCommentIsReported()
    {
        var result = _sanitizer.SanitizeWithReport("<p>x</p><!-- a note -->", SanitizationProfile.Basic);

        result.Removals.Should().ContainSingle()
            .Which.Kind.Should().Be(SanitizationRemovalKind.Comment);
    }

    [Test]
    public void ADroppedClassIsReported()
    {
        var options = new SanitizationOptions();
        options.AllowedCssClasses.Add("lead");

        var result = new SanitizationService(options).SanitizeWithReport(
            "<p class=\"lead admin-only\">x</p>",
            SanitizationProfile.Extended);

        result.Removals.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(
                new SanitizationRemoval(SanitizationRemovalKind.CssClass, "admin-only", "p"));
    }

    [Test]
    public void AnExcerptIsTruncatedRatherThanCarryingTheWholePayload()
    {
        var long_ = new string('a', SanitizationRemoval.MaxValueLength + 50);

        var result = _sanitizer.SanitizeWithReport(
            $"<p onmouseover=\"{long_}\">x</p>",
            SanitizationProfile.Basic);

        // The report is written to logs and rendered in the editor. One pasted document should not
        // be able to fill either.
        result.Removals.Should().ContainSingle()
            .Which.Value!.Length.Should().Be(SanitizationRemoval.MaxValueLength + 1);
    }

    [Test]
    public void EveryRemovalDescribesItself()
    {
        var result = _sanitizer.SanitizeWithReport(
            "<script>alert(1)</script><p onmouseover=\"x\" style=\"position:fixed\">t</p><!--c-->",
            SanitizationProfile.Extended);

        result.Removals.Should().NotBeEmpty();
        result.Removals.Should().OnlyContain(removal => removal.Describe().Length > 0);
    }

    [Test]
    public void RemovalsAreNotSharedBetweenCalls()
    {
        var first = _sanitizer.SanitizeWithReport("<script>alert(1)</script>", SanitizationProfile.Basic);
        var second = _sanitizer.SanitizeWithReport("<p>clean</p>", SanitizationProfile.Basic);

        // The reporting path builds its own sanitizer for exactly this reason: the library's removal
        // events carry no per-call context, so a handler on a shared instance would hand one
        // request another request's removals.
        first.Removals.Should().ContainSingle();
        second.Removals.Should().BeEmpty();
    }
}
