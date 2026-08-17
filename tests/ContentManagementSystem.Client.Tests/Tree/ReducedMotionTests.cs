using System.Text.RegularExpressions;

using Bunit;

using ContentManagementSystem.Client.Components.Admin.Tree;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;

namespace ContentManagementSystem.Client.Tests.Tree;

/// <summary>
/// Motion and colour in the backoffice (task P6-39, spec section 28).
/// </summary>
/// <remarks>
/// Two rules, both of which are broken by adding one line of CSS and neither of which any other test
/// would notice. Movement has to stop for somebody who asked for no movement, and a status has to be
/// legible to somebody who cannot tell the colours apart — which is most of what "no colour-only
/// encoding" means in practice.
/// <para>
/// The motion half is asserted against the stylesheets themselves rather than through a rendered
/// screen, because <c>prefers-reduced-motion</c> is a media query: a rendering test would have to
/// emulate the preference and then assert on a computed style, and bUnit has no browser to compute
/// one in. What can be checked here is the thing that actually goes wrong — an animation added
/// without a guard beside it.
/// </para>
/// </remarks>
public partial class ReducedMotionTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void EveryStylesheetThatMovesSomethingAlsoSaysWhenToStop()
    {
        var unguarded = new List<string>();

        foreach (var stylesheet in Stylesheets())
        {
            var css = File.ReadAllText(stylesheet);

            // Bootstrap's own components carry their guard inside the framework, so only the
            // declarations this repository wrote are judged.
            if (!Movement().IsMatch(css)) continue;

            if (!css.Contains("prefers-reduced-motion", StringComparison.Ordinal))
            {
                unguarded.Add(Path.GetFileName(stylesheet));
            }
        }

        unguarded.Should().BeEmpty(
            "a stylesheet that animates something without a reduced-motion guard is one line away " +
            "from making somebody ill, and nothing else in this suite would notice: {0}",
            string.Join(", ", unguarded));
    }

    [Fact]
    public void AtLeastOneStylesheetWasActuallyRead()
    {
        // The guard above passes vacuously if the file walk finds nothing, which is exactly what a
        // moved folder would cause.
        Stylesheets().Should().NotBeEmpty("the stylesheets could not be found, so nothing was checked");
    }

    [Theory]
    [InlineData(1, false, null, "Published")]
    [InlineData(1, true, null, "Unpublished changes")]
    [InlineData(null, false, null, "Not published")]
    [InlineData(1, true, "future", "Scheduled")]
    public void EveryTreeStatusIsAWordAndNotOnlyAColour(
        int? published,
        bool unpublishedChanges,
        string? scheduled,
        string expected)
    {
        var now = new DateTimeOffset(2026, 8, 16, 12, 0, 0, TimeSpan.Zero);

        _bunit.Services.AddSingleton<TimeProvider>(new FakeTimeProvider(now));

        var page = StubPageClient.Page(
            1,
            "Pricing",
            published: published,
            unpublishedChanges: unpublishedChanges,
            scheduled: scheduled is null ? null : now.AddDays(2));

        var indicator = _bunit.Render<PageStatusIndicator>(parameters => parameters
            .Add(component => component.Page, page)
            .Add(component => component.Now, now));

        indicator.Markup.Should().Contain(
            expected,
            "an icon in a colour is not a status to somebody who cannot tell the colours apart");

        // And the word is not merely present — it is announced. The icon carries aria-hidden, so the
        // visually-hidden text is the only thing a screen reader has to work with.
        indicator.Find(".visually-hidden").TextContent.Trim().Should().Be(expected);
        indicator.Find(".bi").GetAttribute("aria-hidden").Should().Be("true");
    }

    /// <summary>Every stylesheet this repository wrote, component-scoped and site-wide alike.</summary>
    private static List<string> Stylesheets()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        if (directory is null) return [];

        var source = Path.Combine(directory.FullName, "src");

        return
        [
            .. Directory.EnumerateFiles(source, "*.razor.css", SearchOption.AllDirectories),
            .. Directory.EnumerateFiles(source, "*.scss", SearchOption.AllDirectories)
                // Bootstrap's own sources are vendored under node_modules and carry their own
                // guards; auditing them would be auditing somebody else's stylesheet.
                .Where(path => !path.Contains("node_modules", StringComparison.Ordinal)),
        ];
    }

    /// <summary>Matches a declaration that moves something under its own steam.</summary>
    /// <remarks>
    /// Animations and transitions only. A transform on its own does not move — it is a position —
    /// and matching it would report every rotated chevron in the backoffice.
    /// </remarks>
    [GeneratedRegex(@"^\s*(animation|transition)\s*:", RegexOptions.Multiline)]
    private static partial Regex Movement();
}
