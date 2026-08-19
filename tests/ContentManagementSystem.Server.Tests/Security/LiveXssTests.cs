using System.Net;
using System.Text.Json;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Security;

/// <summary>
/// The XSS corpus against live rendering (task P9-06, spec section 20.1).
/// </summary>
/// <remarks>
/// The corpus already runs against <c>SanitizationService</c> in the unit suite, under every profile,
/// as a required merge check. This runs the same payloads the other way: stored through the real
/// draft service, published, and read back over HTTP as a visitor receives them.
/// <para>
/// <strong>What this catches that the unit suite cannot</strong> is the second half of ADR-0008. The
/// sanitizer is applied on write <em>and</em> on render, and between them is a component renderer
/// that has to put the result into a document without un-escaping it. A field renderer that reached
/// for <c>MarkupString</c> on a raw stored value would pass every assertion in the unit corpus and
/// fail every one here.
/// </para>
/// <para>
/// Both surfaces an author can write markup into are covered: <c>richText</c>, whose profile is the
/// narrow one most zones use, and <c>html</c>, which is the widest profile in the system and is the
/// one an attacker would want. The payload also goes through the page <em>title</em>, which is a
/// plain-text field rendered into the document head and the navigation — the place markup is most
/// likely to be trusted because "it is only a title".
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class LiveXssTests(SqlServerFixture fixture)
{
    private const string TemplateKey = "article";

    private PageWorkbench _bench = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    /// <summary>
    /// Publishes every payload in the corpus and reads each page back.
    /// </summary>
    /// <remarks>
    /// One test over the whole corpus rather than one per payload, and the reason is cost: a payload
    /// per test is a migrated database per payload, which put nine minutes on the suite for fifty-two
    /// assertions that share every fixture they need. The unit corpus is the one that wants a case
    /// per payload — it is milliseconds each and the report it writes is per payload.
    /// <para>
    /// Nothing is lost on a failure. Each page is published under a slug naming its payload, and the
    /// assertion message carries the payload's name and its evasion group, so a red run says which
    /// trick got through rather than that one did.
    /// </para>
    /// </remarks>
    [Test]
    public async Task NoCorpusPayloadSurvivesIntoADeliveredPage()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var template = await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "prose", Name = "Prose", FieldTypeKey = FieldTypeKeys.RichText },
            new Zone { Key = "raw", Name = "Raw", FieldTypeKey = FieldTypeKeys.Html });

        using var client = _bench.CreateClient();

        foreach (var payload in XssCorpus.All)
        {
            var page = await _bench.AddPageAsync(template, $"Corpus {payload.Name}", cancellationToken);

            _bench.Context.ChangeTracker.Clear();

            var encoded = JsonSerializer.Serialize(payload.Payload);

            var saved = await _bench.Resolve<IDraftService>().SaveAsync(
                page.Summary.Id,
                new SaveDraftRequest(
                    $$"""
                    { "schemaVersion": 1, "templateKey": "{{template.Key}}", "templateRevision": 1,
                      "zones": {
                        "prose": { "type": "richText", "format": "html", "value": {{encoded}} },
                        "raw":   { "type": "html", "value": {{encoded}} }
                      } }
                    """,
                    null),
                cancellationToken);

            saved.IsSuccess.Should().BeTrue(Because(saved));
            _bench.Context.ChangeTracker.Clear();

            var published = await _bench.Resolve<IPublishingService>()
                .PublishAsync(page.Summary.Id, true, cancellationToken);

            published.IsSuccess.Should().BeTrue(Because(published));
            _bench.Context.ChangeTracker.Clear();

            using var response = await client.GetAsync($"/{page.Summary.Slug}", cancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.OK, payload.Name);

            var html = await response.Content.ReadAsStringAsync(cancellationToken);

            DeliveredMarkupAssertions.AssertNothingExecutable(
                html,
                $"[{payload.Group}] {payload.Name} was stored in a richText and an html zone");
        }
    }

    [Test]
    public async Task APayloadInAPageTitleIsEscapedRatherThanRendered()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        var template = await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            PageWorkbench.TextZone("body"));

        const string Hostile = "Quarter results <img src=x onerror=alert(1)>";

        var page = await _bench.AddPageAsync(template, Hostile, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var saved = await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(
                $$"""
                { "schemaVersion": 1, "templateKey": "{{template.Key}}", "templateRevision": 1,
                  "zones": { "body": { "type": "plainText", "value": "Nothing to see." } } }
                """,
                null),
            cancellationToken);

        saved.IsSuccess.Should().BeTrue(Because(saved));
        _bench.Context.ChangeTracker.Clear();

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken);

        published.IsSuccess.Should().BeTrue(Because(published));
        _bench.Context.ChangeTracker.Clear();

        using var client = _bench.CreateClient();
        using var response = await client.GetAsync($"/{page.Summary.Slug}", cancellationToken);

        var html = await response.Content.ReadAsStringAsync(cancellationToken);

        // A title is not sanitized — it is plain text, and escaping is the whole of its defence. The
        // <title> element and the Open Graph tags both carry it, and an unescaped one in a meta
        // content attribute closes the attribute just as readily as one in the body closes an
        // element.
        html.Should().NotContain("<img src=x onerror")
            .And.Contain("&lt;img src=x onerror=alert(1)&gt;");

        DeliveredMarkupAssertions.AssertNothingExecutable(html, "the page title carried markup");
    }

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
