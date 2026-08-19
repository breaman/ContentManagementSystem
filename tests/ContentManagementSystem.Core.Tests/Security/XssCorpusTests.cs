using ContentManagementSystem.Core.Security;

using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using TUnit.Core.Interfaces;

namespace ContentManagementSystem.Core.Tests.Security;

/// <summary>
/// The merge gate (task P1-20, acceptance criterion P1 #6): every payload in
/// <see cref="XssCorpus"/> is neutralized under every profile, and what was stripped is reported.
/// </summary>
/// <remarks>
/// Gap #11 is the highest-severity item in the specification, and this is the test that holds the
/// line on it. It runs in the <c>Unit tests</c> CI job, which is a required check — a change that
/// widens a profile far enough to let one of these through cannot merge.
/// <para>
/// The suite asserts an invariant rather than expected outputs. Adding a payload to the corpus needs
/// no new test method, and pinning exact sanitized strings would make the suite fail on a harmless
/// change in how AngleSharp serializes a document, which is the fastest way to teach a team to
/// re-baseline a security test.
/// </para>
/// </remarks>
public class XssCorpusTests
{
    private static readonly SanitizationProfile[] Profiles =
    [
        SanitizationProfile.Basic,
        SanitizationProfile.Extended,
        SanitizationProfile.Developer,
    ];

    private readonly SanitizationService _sanitizer = new();

    /// <summary>
    /// Where the suite writes what each profile stripped. TUnit reaches the current test's log
    /// through the ambient context rather than a constructor-injected helper, so this is a property
    /// rather than a field.
    /// </summary>
    private static ITestOutput Output => TestContext.Current!.Output;

    public static IEnumerable<(string Name, SanitizationProfile Profile)> Corpus
    {
        get
        {
            var data = new List<(string, SanitizationProfile)>();

            foreach (var payload in XssCorpus.All)
            {
                foreach (var profile in Profiles)
                {
                    data.Add((payload.Name, profile));
                }
            }

            return data;
        }
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public void EveryCorpusPayloadIsNeutralizedUnderEveryProfile(string name, SanitizationProfile profile)
    {
        var payload = Find(name);

        var result = _sanitizer.SanitizeWithReport(payload.Payload, profile);

        // Written whether or not the assertion holds, because the report is half of what this task
        // asks for: a payload that is safe but was gutted is risk R3, and it is invisible unless
        // someone can read what went.
        Output.WriteLine($"[{payload.Group}] {payload.Name} under {profile}");
        Output.WriteLine($"  in : {payload.Payload}");
        Output.WriteLine($"  out: {result.Html}");

        foreach (var removal in result.Removals)
        {
            Output.WriteLine($"  ✂ {removal.Describe()}");
        }

        SanitizationAssertions.AssertNeutralized(result.Html, profile);
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public void SanitizingTwiceChangesNothingFurther(string name, SanitizationProfile profile)
    {
        var payload = Find(name);

        var once = _sanitizer.Sanitize(payload.Payload, profile);
        var twice = _sanitizer.Sanitize(once, profile);

        // Content is sanitized on write and again on render (ADR 0008), so the render pass runs over
        // output this service already produced. If a second pass could change anything, the stored
        // markup and the rendered markup would differ — and a sanitizer whose output is not its own
        // fixed point is also the shape a mutation-XSS bypass takes.
        twice.Should().Be(once);
    }

    [Test]
    [MethodDataSource(nameof(Corpus))]
    public void TheReportedRemovalsAccountForTheChange(string name, SanitizationProfile profile)
    {
        var payload = Find(name);

        var result = _sanitizer.SanitizeWithReport(payload.Payload, profile);

        if (string.Equals(result.Html, payload.Payload, StringComparison.Ordinal))
        {
            return;
        }

        // A payload whose markup changed but that reports nothing is the silent-stripping failure in
        // its purest form: the editor's pre-save warning would show an author a clean bill of health
        // for content the save is about to alter.
        //
        // Re-serialization alone does not count as a change here — a sanitizer that only reformatted
        // its input leaves the same elements and attributes behind, so compare structure, not text.
        var before = SanitizationAssertions.TagNames(payload.Payload);
        var after = SanitizationAssertions.TagNames(result.Html);

        if (!before.SequenceEqual(after, StringComparer.OrdinalIgnoreCase))
        {
            result.RemovedAnything.Should().BeTrue(
                $"{payload.Name} lost elements under {profile} without reporting any of it");
        }
    }

    [Test]
    public void TheCorpusCoversEveryEvasionGroup()
    {
        // Cheap, but it is what stops the corpus from decaying into thirty variations of one trick.
        // Each group is a different assumption about how markup is parsed, and a sanitizer can pass
        // every payload in one group while failing the whole of another.
        XssCorpus.All.Select(payload => payload.Group).Distinct().Should().HaveCountGreaterThanOrEqualTo(7);

        XssCorpus.All.Select(payload => payload.Name).Should().OnlyHaveUniqueItems();
    }

    private static XssCorpus.Case Find(string name) =>
        XssCorpus.All.Single(payload => payload.Name == name);
}
