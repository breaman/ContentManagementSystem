using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

using ContentManagementSystem.Client.Services;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Client.Tests.Services;

/// <summary>
/// How the browser's page client reads a refusal (tasks P6-19, P6-20).
/// </summary>
/// <remarks>
/// Everything else about this client is transport. What it can get wrong on its own is the reading
/// of an RFC 9457 body, and both cases here are ones where getting it wrong loses the only
/// information the screen needed: the draft that won a race, and the warnings a publish is waiting
/// to have acknowledged.
/// </remarks>
public class HttpPageClientTests
{
    [Fact]
    public async Task AConflictHandsBackTheDraftThatWon()
    {
        var client = Client(request => request.Method == HttpMethod.Put
            ? Problem(
                HttpStatusCode.Conflict,
                errors:
                [
                    new ApiDiagnostic(
                        PageCodes.ConcurrentChange,
                        "This draft was saved by someone else after you opened it."),
                ],
                conflict: new DraftSaveResult(
                    new DraftState(4, 11, 5, """{"theirs":true}""", "landing", 2, "rv-theirs", null),
                    [],
                    0))
            : Empty());

        var result = await client.SaveDraftAsync(
            4,
            new SaveDraftRequest("""{"mine":true}""", "rv-1"),
            TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse("a refusal that carries state is still a refusal");
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(PageCodes.ConcurrentChange);

        // Without this the conflict dialog has nothing to offer: it cannot compare, and "take
        // theirs" has no theirs to take.
        result.Value!.Draft.RowVersion.Should().Be("rv-theirs");
        result.Value.Draft.ContentJson.Should().Contain("theirs");
    }

    [Fact]
    public async Task ARefusalWithNothingButWarningsKeepsTheWarnings()
    {
        var client = Client(request => request.Method == HttpMethod.Post
            ? Problem(
                HttpStatusCode.UnprocessableEntity,
                errors: [],
                warnings:
                [
                    new ApiDiagnostic("seo.description-missing", "This page has no meta description."),
                ])
            : Empty());

        var result = await client.PublishAsync(
            4,
            cancellationToken: TestContext.Current.CancellationToken);

        // A publish stopped only by warnings answers 422 with an empty errors array and the warnings
        // in it, waiting to be acknowledged (spec section 22.2). Reading the errors alone turned
        // that into a bare "http.422" — a screen telling an editor their page was refused and
        // refusing to say what for.
        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().BeEmpty();
        result.Warnings.Should().ContainSingle()
            .Which.Message.Should().Contain("no meta description");
    }

    [Fact]
    public async Task ARefusalWithNoDiagnosticsAtAllStillSaysSomething()
    {
        var client = Client(request => request.Method == HttpMethod.Post
            ? new HttpResponseMessage(HttpStatusCode.BadGateway)
            : Empty());

        var result = await client.PublishAsync(
            4,
            cancellationToken: TestContext.Current.CancellationToken);

        result.IsSuccess.Should().BeFalse();
        result.Errors.Should().ContainSingle().Which.Code.Should().Be(
            "http.502",
            "a proxy that answered with no body is still something the editor has to be told about");
    }

    /// <summary>A client whose every request is answered by one function.</summary>
    private static HttpPageClient Client(Func<HttpRequestMessage, HttpResponseMessage> answer) =>
        new(new HttpClient(new StubHandler(answer)) { BaseAddress = new Uri("https://localhost/") });

    /// <summary>The antiforgery token every write fetches before it is sent.</summary>
    private static HttpResponseMessage Empty() =>
        new(HttpStatusCode.OK)
        {
            Content = JsonContent.Create(new AntiforgeryTokenResponse("X-CSRF", "token")),
        };

    /// <summary>An RFC 9457 body of the shape the management API returns (spec section 22.2).</summary>
    private static HttpResponseMessage Problem(
        HttpStatusCode status,
        IReadOnlyList<ApiDiagnostic> errors,
        IReadOnlyList<ApiDiagnostic>? warnings = null,
        object? conflict = null)
    {
        var body = new Dictionary<string, object?>
        {
            ["status"] = (int)status,
            ["title"] = "Refused",
            ["detail"] = "Something was refused.",
            ["errors"] = errors,
            ["warnings"] = warnings ?? [],
        };

        if (conflict is not null) body["conflict"] = conflict;

        return new HttpResponseMessage(status)
        {
            Content = new StringContent(
                JsonSerializer.Serialize(body, JsonSerializerOptions.Web),
                Encoding.UTF8,
                "application/problem+json"),
        };
    }

    private sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> answer)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(answer(request));
    }
}
