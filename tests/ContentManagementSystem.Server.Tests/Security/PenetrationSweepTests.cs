using System.Net;
using System.Net.Http.Json;
using System.Text;

using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Server.Api.Cms;
using ContentManagementSystem.Shared.Contracts.Media;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.TestSupport;

using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Security;

/// <summary>
/// The penetration pass of task P9-06, minus the two halves that have suites of their own.
/// </summary>
/// <remarks>
/// Six areas are named. Two are already covered end to end and are not restated here: the
/// <strong>IDOR sweep</strong> is <c>Workflow/IdorSweepTests</c>, which walks every content and media
/// entry point with a guessed id across an access boundary (task P7-07), and the <strong>XSS
/// corpus against live rendering</strong> is <see cref="LiveXssTests"/>. What is left is upload
/// fuzzing, unsigned rendition URLs, preview-token enumeration, and CSRF — and the first is the one
/// with no existing coverage at the HTTP boundary at all.
/// <para>
/// These are adversarial requests rather than feature tests, so each one states the attack it is: a
/// refusal that arrives as the wrong status, or that arrives after the server has already done the
/// expensive thing, is a finding even when nothing leaks.
/// </para>
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class PenetrationSweepTests(SqlServerFixture fixture)
{
    private const string Media = $"{CmsApiEndpoints.BasePath}/media";

    private CmsApplicationFactory _factory = null!;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _factory = await CmsApplicationFactory.CreateAsync(fixture, TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _factory.DisposeAsync();

    /// <summary>
    /// Hostile uploads, each named for the thing it is pretending to be.
    /// </summary>
    /// <remarks>
    /// Every one is refused by its <em>bytes</em> rather than by its name, which is the rule that
    /// makes the list interesting: every one carries an image extension and an image-shaped content
    /// type, and one leads with a real GIF header before its payload.
    /// <para>
    /// A NUL-truncated file name — <c>sneaky.jpg\0.html</c>, the classic — is deliberately absent.
    /// <c>MultipartFormDataContent</c> refuses to encode one, so there is no request to make and
    /// nothing to assert about the server: it is the client library that blocks it, and a test that
    /// pretended otherwise would be asserting about .NET.
    /// </para>
    /// </remarks>
    public static IEnumerable<(string Name, string FileName, string Bytes)> HostileUploads =>
    [
        ("html-as-jpg", "photo.jpg", "<!DOCTYPE html><html><body><script>alert(1)</script></body></html>"),
        ("svg-as-png", "logo.png", """<svg xmlns="http://www.w3.org/2000/svg"><script>alert(1)</script></svg>"""),
        ("php-as-gif", "avatar.gif", "GIF89a<?php system($_GET['c']); ?>"),
        ("script-as-webp", "banner.webp", "#!/bin/sh\nrm -rf /\n"),
        ("empty", "nothing.jpg", ""),
        ("truncated-jpeg", "half.jpg", "ÿØÿ"),
        ("double-extension", "sneaky.php.jpg", "<?php echo 1; ?>"),
        ("traversal-name", "../../../../etc/passwd", "not an image at all"),
    ];

    [Test]
    [MethodDataSource(nameof(HostileUploads))]
    public async Task AnUploadIsJudgedByItsBytesRatherThanItsName(string name, string fileName, string bytes)
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = await CmsApplicationFactory.WithAntiforgeryTokenAsync(
            _factory.CreateClientAs(CmsRoles.Administrator),
            cancellationToken);

        using var body = new MultipartFormDataContent
        {
            { Image(bytes), "file", fileName },
            { new StringContent("A picture of nothing"), "altText" },
        };

        using var response = await client.PostAsync(Media, body, cancellationToken);

        // Refused, and refused as a client error rather than as a server one: a 500 here would mean
        // the payload reached something that threw, which is a different and worse answer than "no".
        response.StatusCode.Should().BeOneOf(
            [
                HttpStatusCode.BadRequest,
                HttpStatusCode.UnprocessableEntity,
                HttpStatusCode.UnsupportedMediaType,
                HttpStatusCode.RequestEntityTooLarge,
            ],
            $"{name} is not an image");

        ((int)response.StatusCode).Should().BeLessThan(500, $"{name} must not reach anything that throws");
    }

    [Test]
    public async Task NothingHostileEverReachesTheLibrary()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = await CmsApplicationFactory.WithAntiforgeryTokenAsync(
            _factory.CreateClientAs(CmsRoles.Administrator),
            cancellationToken);

        foreach (var (_, fileName, bytes) in HostileUploads)
        {
            using var body = new MultipartFormDataContent
            {
                { Image(bytes), "file", fileName },
                { new StringContent("alt"), "altText" },
            };

            using var ignored = await client.PostAsync(Media, body, cancellationToken);
        }

        using var listing = await client.GetAsync($"{Media}?take=100", cancellationToken);

        listing.StatusCode.Should().Be(HttpStatusCode.OK);

        var page = await listing.Content.ReadFromJsonAsync<MediaListResult>(cancellationToken);

        // The status codes above say each request was refused. This says none of them was refused
        // after the row had already been written, which is a failure the response cannot show.
        page!.Items.Should().BeEmpty("no hostile upload created a library item");
    }

    [Test]
    public async Task ARenditionUrlThisSiteDidNotSignIsRefused()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _factory.CreateClient();

        // Three ways of not being signed: no signature at all, a well-formed signature from another
        // key, and a signature lifted from a different item's URL.
        string[] forged =
        [
            "/media/1/800x600/cover/photo.jpg",
            "/media/1/800x600/cover/photo.jpg?s=AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA",
            "/media/1/file/photo.jpg?s=" + Convert.ToBase64String(Encoding.UTF8.GetBytes("not-a-signature")),
        ];

        foreach (var url in forged)
        {
            using var response = await client.GetAsync(url, cancellationToken);

            // Not found or forbidden, never 200 and never a 500. The signature is checked before the
            // item is loaded and long before anything is encoded, so an attacker cannot use this to
            // make the server work either.
            response.StatusCode.Should().BeOneOf(
                [HttpStatusCode.NotFound, HttpStatusCode.Forbidden, HttpStatusCode.BadRequest],
                url);
        }
    }

    [Test]
    public async Task AGuessedPreviewTokenIsRefusedAndTellsTheGuesserNothing()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        using var client = _factory.CreateClient();

        // The token space is 256 bits and these are wrong in every way a guess can be wrong: too
        // short, right shape, and syntactically invalid.
        string[] guesses =
        [
            "/preview/s/abc",
            "/preview/s/" + Convert.ToHexString(new byte[32]),
            "/preview/s/" + new string('z', 43),
            "/preview/s/..%2f..%2fadmin",
        ];

        var bodies = new List<string>();

        foreach (var url in guesses)
        {
            using var response = await client.GetAsync(url, cancellationToken);

            response.StatusCode.Should().BeOneOf(
                [HttpStatusCode.NotFound, HttpStatusCode.Gone, HttpStatusCode.BadRequest],
                url);

            bodies.Add(await response.Content.ReadAsStringAsync(cancellationToken));
        }

        // And every refusal reads the same. A refusal that differed between "no such token" and
        // "that token expired" would be an oracle: it confirms a guess landed on a real one.
        bodies.Distinct(StringComparer.Ordinal).Should().ContainSingle(
            "a guess must not be able to tell a wrong token from an expired one");
    }

    [Test]
    public async Task ACookieAuthenticatedWriteWithoutATokenIsRefusedAcrossTheApi()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;

        // Deliberately not through WithAntiforgeryTokenAsync. This is the shape of a cross-site
        // request: the browser attaches the session cookie, and nothing else.
        using var client = _factory.CreateClientAs(CmsRoles.Administrator);

        (string Method, string Url, HttpContent? Body)[] writes =
        [
            ("POST", $"{CmsApiEndpoints.BasePath}/pages", JsonBody("""{ "title": "Forged", "templateKey": "article" }""")),
            ("POST", $"{CmsApiEndpoints.BasePath}/media/folders", JsonBody("""{ "name": "Forged" }""")),
            ("DELETE", $"{CmsApiEndpoints.BasePath}/pages/1", null),
        ];

        foreach (var (method, url, body) in writes)
        {
            using var request = new HttpRequestMessage(new HttpMethod(method), url) { Content = body };
            using var response = await client.SendAsync(request, cancellationToken);

            response.StatusCode.Should().Be(HttpStatusCode.BadRequest, $"{method} {url}");
        }
    }

    /// <summary>Body content declaring an image type it is not, which is the point.</summary>
    /// <param name="bytes">The payload.</param>
    /// <returns>The part.</returns>
    private static ByteArrayContent Image(string bytes)
    {
        var content = new ByteArrayContent(Encoding.UTF8.GetBytes(bytes));
        content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("image/jpeg");

        return content;
    }

    private static StringContent JsonBody(string json) => new(json, Encoding.UTF8, "application/json");
}
