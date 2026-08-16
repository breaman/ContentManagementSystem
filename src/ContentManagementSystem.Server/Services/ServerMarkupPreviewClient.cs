using ContentManagementSystem.Core.Security;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Security;
using ContentManagementSystem.Shared.Services;

namespace ContentManagementSystem.Server.Services;

/// <summary>
/// The server half of <see cref="IMarkupPreviewClient"/>, over the pipeline directly (task P6-09).
/// </summary>
/// <param name="markdown">The single Markdig configuration the delivery path renders through.</param>
/// <param name="sanitizer">The single allowlist the delivery path cleans through.</param>
/// <param name="authorization">Whether the caller may ask for the developer profile.</param>
/// <remarks>
/// Used during pre-rendering, so a page editor arrives with its preview pane already filled rather
/// than blank until the WebAssembly runtime finishes downloading.
/// <para>
/// <strong>This is the same pair of singletons <c>RichTextRenderer</c> calls.</strong> That is what
/// acceptance criterion P6 #2 asks for — preview matching the published page exactly — and it holds
/// here by construction rather than by resemblance: there is one <c>IMarkdownRenderer</c>
/// registration in the container and both paths resolve it.
/// </para>
/// </remarks>
public sealed class ServerMarkupPreviewClient(
    IMarkdownRenderer markdown,
    IContentSanitizer sanitizer,
    ICmsAuthorization authorization) : IMarkupPreviewClient
{
    /// <inheritdoc />
    /// <remarks>
    /// An unreadable format or a refused profile answers null, which is the same thing the HTTP half
    /// answers for the endpoint's <c>422</c>. Both halves have to fail the same way or a screen would
    /// behave differently on its first paint from how it behaves a second later.
    /// </remarks>
    public Task<MarkupPreviewResult?> RenderAsync(
        MarkupPreviewRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (!TryReadProfile(request.Profile, out var profile)) return Task.FromResult<MarkupPreviewResult?>(null);

        if (profile is SanitizationProfile.Developer &&
            !authorization.HasPermission(CmsPermissions.StructureEdit))
        {
            return Task.FromResult<MarkupPreviewResult?>(null);
        }

        var result = request.Format switch
        {
            MarkupFormats.Markdown => markdown.ToHtmlWithReport(request.Source, profile),
            MarkupFormats.Html => sanitizer.SanitizeWithReport(request.Source, profile),
            _ => null,
        };

        return Task.FromResult(result is null
            ? null
            : new MarkupPreviewResult(result.Html, result.Removals));
    }

    /// <inheritdoc />
    public Task<IReadOnlyList<SanitizationProfileDescriptor>> GetProfilesAsync(
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<SanitizationProfileDescriptor>>(
        [
            .. Enum.GetValues<SanitizationProfile>()
                .Select(profile => new SanitizationProfileDescriptor(
                    profile.ToString(),
                    [.. SanitizationPolicy.TagsFor(profile).Order(StringComparer.Ordinal)])),
        ]);

    private static bool TryReadProfile(string? requested, out SanitizationProfile profile)
    {
        if (string.IsNullOrWhiteSpace(requested))
        {
            profile = SanitizationProfile.Basic;

            return true;
        }

        return Enum.TryParse(requested, ignoreCase: true, out profile) && Enum.IsDefined(profile);
    }
}
