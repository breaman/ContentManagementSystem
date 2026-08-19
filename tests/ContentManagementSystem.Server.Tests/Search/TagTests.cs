using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Tags;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Api;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ContentManagementSystem.Server.Tests.Search;

/// <summary>
/// Tags as editorial metadata, and the housekeeping a free-form vocabulary needs (task P8-20).
/// </summary>
/// <remarks>
/// The taxonomy has exactly one writer — the page metadata patch — which is what these assert
/// through. The <c>tags</c> field type contributes searchable text and no rows; two writers would
/// mean a tag removed on the properties panel reappearing on the next payload save.
/// </remarks>
[ClassDataSource<SqlServerFixture>(Shared = SharedType.PerTestSession)]
[NotInParallel(SqlServerConstraint.Key)]
public class TagTests(SqlServerFixture fixture)
{
    private const string TemplateKey = "article";

    private PageWorkbench _bench = null!;
    private Template? _template;

    [Before(HookType.Test)]
    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current!.Execution.CancellationToken);

    [After(HookType.Test)]
    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Test]
    public async Task TaggingAPageCreatesTheVocabularyAndFoldsCaseAndPunctuation()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageAsync("Pricing", cancellationToken);

        await TagAsync(page.Summary.Id, ["Release Notes", "release-notes", "v2"], cancellationToken);

        var tags = await _bench.Context.Tags.AsNoTracking().OrderBy(tag => tag.Slug)
            .Select(tag => tag.Slug)
            .ToListAsync(cancellationToken);

        // Two labels, one tag. Slug is identity, so "Release Notes" and "release-notes" cannot
        // become two rows that filter to two different sets of pages.
        tags.Should().Equal("release-notes", "v2");

        (await _bench.Context.PageTags.AsNoTracking()
            .CountAsync(applied => applied.PageId == page.Summary.Id, cancellationToken))
            .Should().Be(2);
    }

    [Test]
    public async Task SendingAShorterListTakesTheMissingTagsOffThePage()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageAsync("Pricing", cancellationToken);

        await TagAsync(page.Summary.Id, ["alpha", "beta"], cancellationToken);
        var detail = await TagAsync(page.Summary.Id, ["beta"], cancellationToken);

        // The whole set, not a delta: the panel knows what the page should end up with and nothing
        // about what it had.
        detail.Tags.Should().Equal("beta");

        (await _bench.Context.PageTags.AsNoTracking()
            .CountAsync(applied => applied.PageId == page.Summary.Id, cancellationToken))
            .Should().Be(1);

        // The vocabulary keeps the tag nothing carries any more. Deleting it is the tag screen's
        // decision, with the page count in front of somebody; a save that silently pruned it would
        // make that decision on their behalf.
        (await _bench.Context.Tags.AsNoTracking().CountAsync(cancellationToken)).Should().Be(2);
    }

    [Test]
    public async Task RenamingToAnExistingNameMergesTheTwoTagsAndKeepsEveryPage()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var first = await PageAsync("Pricing", cancellationToken);
        var second = await PageAsync("Support", cancellationToken);

        await TagAsync(first.Summary.Id, ["Products"], cancellationToken);
        await TagAsync(second.Summary.Id, ["product"], cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var products = await _bench.Context.Tags
            .AsNoTracking()
            .FirstAsync(tag => tag.Slug == "products", cancellationToken);

        await using var scope = _bench.NewScope();

        var renamed = await scope.ServiceProvider.GetRequiredService<ITagService>()
            .RenameAsync(products.Id, new RenameTagRequest { Name = "Product" }, cancellationToken);

        renamed.IsSuccess.Should().BeTrue(Because(renamed));

        // Renaming onto a name that exists *is* a merge. Refusing it would leave an editor to do the
        // merge by hand on every page carrying the near-duplicate.
        renamed.Value!.Merged.Should().BeTrue();
        renamed.Value.Tag.PageCount.Should().Be(2);

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Context.Tags.AsNoTracking().CountAsync(cancellationToken)).Should().Be(1);
        (await _bench.Context.PageTags.AsNoTracking().CountAsync(cancellationToken)).Should().Be(2);
    }

    [Test]
    public async Task DeletingATagTakesItOffEveryPageWithoutTouchingThePages()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var page = await PageAsync("Pricing", cancellationToken);

        await TagAsync(page.Summary.Id, ["seasonal"], cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        var seasonal = await _bench.Context.Tags
            .AsNoTracking()
            .FirstAsync(tag => tag.Slug == "seasonal", cancellationToken);

        await using var scope = _bench.NewScope();

        var deleted = await scope.ServiceProvider.GetRequiredService<ITagService>()
            .DeleteAsync(seasonal.Id, cancellationToken);

        deleted.IsSuccess.Should().BeTrue(Because(deleted));
        deleted.Value.Should().Be(1);

        _bench.Context.ChangeTracker.Clear();

        (await _bench.Context.PageTags.AsNoTracking().AnyAsync(cancellationToken)).Should().BeFalse();

        // The join rows go and the page stays. PageTag → Tag is Restrict on purpose, so a delete is
        // an act with a page count attached rather than a cascade nobody sees.
        (await _bench.Context.Pages.AsNoTracking()
            .AnyAsync(candidate => candidate.Id == page.Summary.Id, cancellationToken))
            .Should().BeTrue();
    }

    [Test]
    public async Task SuggestionsCompleteAPrefixAndOfferTheMostUsedTagsFirst()
    {
        var cancellationToken = TestContext.Current!.Execution.CancellationToken;
        var first = await PageAsync("Pricing", cancellationToken);
        var second = await PageAsync("Support", cancellationToken);

        await TagAsync(first.Summary.Id, ["product-docs", "internal"], cancellationToken);
        await TagAsync(second.Summary.Id, ["product-docs"], cancellationToken);

        await using var scope = _bench.NewScope();
        var tags = scope.ServiceProvider.GetRequiredService<ITagService>();

        var completed = await tags.SuggestAsync("Prod", 10, cancellationToken);

        // Normalized before matching, so what an editor typed does not have to be slug-shaped.
        completed.IsSuccess.Should().BeTrue();
        completed.Value!.Should().ContainSingle().Which.Slug.Should().Be("product-docs");

        var everything = await tags.SuggestAsync(null, 10, cancellationToken);

        // Most used first: an empty box should offer the vocabulary the site actually leans on
        // rather than whatever happens to sort first.
        everything.Value![0].Slug.Should().Be("product-docs");
    }

    private async Task<PageDetail> TagAsync(
        int pageId,
        IReadOnlyList<string> tags,
        CancellationToken cancellationToken)
    {
        var patched = await _bench.Resolve<IPageService>().PatchMetadataAsync(
            pageId,
            new PatchPageMetadataRequest { Tags = new Patch<IReadOnlyList<string>>(tags) },
            null,
            cancellationToken);

        patched.IsSuccess.Should().BeTrue(Because(patched));
        _bench.Context.ChangeTracker.Clear();

        return patched.Value!;
    }

    private async Task<PageDetail> PageAsync(string title, CancellationToken cancellationToken)
    {
        var template = _template ??= await _bench.UseTemplateAsync(
            TemplateKey,
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

        var page = await _bench.AddPageAsync(template, title, cancellationToken);

        _bench.Context.ChangeTracker.Clear();

        return page;
    }

    private static string Because<T>(CmsResult<T> result) =>
        string.Join("; ", result.Diagnostics.Diagnostics.Select(
            diagnostic => $"{diagnostic.Code}: {diagnostic.Message}"));
}
