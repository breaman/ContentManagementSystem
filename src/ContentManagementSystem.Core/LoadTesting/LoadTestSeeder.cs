using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

using ContentManagementSystem.Core.Content.Schema;
using ContentManagementSystem.Core.Media.Stores;
using ContentManagementSystem.Data.Models;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Data.Seeding;
using ContentManagementSystem.Shared.Common;
using ContentManagementSystem.Shared.Contracts.Fields;

using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

// Two types share this name: the entity, and the contract the field types report. Only the entity
// is written here, and the alias is what keeps that unambiguous at every use.
using EntityReference = ContentManagementSystem.Data.Models.Cms.ContentReference;

namespace ContentManagementSystem.Core.LoadTesting;

/// <inheritdoc cref="ILoadTestSeeder" />
/// <param name="context">The database.</param>
/// <param name="store">Where the pool images are written.</param>
/// <param name="clock">Supplies the timestamps the rows carry.</param>
/// <param name="logger">Records what was written.</param>
public sealed class LoadTestSeeder(
    ApplicationDbContext context,
    IMediaStore store,
    TimeProvider clock,
    ILogger<LoadTestSeeder> logger) : ILoadTestSeeder
{
    /// <summary>Fingerprint user id for rows nobody authored.</summary>
    private const int SystemUser = 0;

    /// <summary>Slug prefix on every tag the seeder creates, which is how the purge finds them.</summary>
    private const string TagPrefix = "lt-";

    private readonly EntityBulkWriter _writer = new(context);

    /// <inheritdoc />
    public async Task<LoadTestSeedReport> SeedAsync(
        LoadTestSeedOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.Validate();

        var started = Stopwatch.GetTimestamp();
        var existing = await FindRootAsync(options.RootSlug, cancellationToken);

        if (existing is not null && !options.Reset)
        {
            return await DescribeAsync(existing, options, Stopwatch.GetElapsedTime(started), cancellationToken);
        }

        if (existing is not null)
        {
            await PurgeAsync(options, progress, cancellationToken);
        }

        var now = clock.GetUtcNow();

        Report(progress, "Ensuring templates and the shared footer.");

        var structure = await EnsureStructureAsync(options, cancellationToken);

        Report(progress, $"Writing {options.MediaItems:N0} media items over {options.DistinctImages} images.");

        var media = await SeedMediaAsync(options, now, cancellationToken);

        Report(progress, $"Planning {options.Pages:N0} pages.");

        var tags = await SeedTagsAsync(options, now, cancellationToken);
        var plan = await PlanAsync(options, structure, media, tags, now, cancellationToken);

        Report(progress, $"Writing {plan.Pages.Count:N0} pages and {plan.VersionCount:N0} versions.");

        await WritePagesAsync(options, plan, cancellationToken);

        Report(progress, "Writing tags, redirects, and search documents.");

        await WriteAncillaryAsync(options, plan, media, now, cancellationToken);

        var elapsed = Stopwatch.GetElapsedTime(started);
        var manifest = await WriteManifestAsync(options, plan, media, now, cancellationToken);

        logger.LogInformation(
            "Load-test dataset seeded under {RootUrl}: {PageCount} pages ({PublishedCount} published), " +
            "{MediaCount} media items in {Elapsed}.",
            plan.RootUrl,
            plan.Pages.Count,
            plan.PublishedCount,
            options.MediaItems,
            elapsed);

        return new LoadTestSeedReport(
            plan.Root.Entity.Id,
            plan.RootUrl,
            plan.Pages.Count,
            plan.PublishedCount,
            plan.VersionCount,
            options.MediaItems,
            options.DistinctImages,
            options.Tags,
            options.Redirects,
            plan.SearchDocumentCount,
            elapsed,
            manifest,
            AlreadySeeded: false);
    }

    /// <inheritdoc />
    public async Task<bool> PurgeAsync(
        LoadTestSeedOptions options,
        IProgress<string>? progress = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);

        var removed = false;
        var root = await FindRootAsync(options.RootSlug, cancellationToken);

        if (root is not null)
        {
            Report(progress, $"Deleting the page tree below {root.Slug}.");

            await PurgePagesAsync(root, cancellationToken);

            removed = true;
        }

        var folder = await context.MediaFolders
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(
                candidate => candidate.ParentId == null && candidate.Name == options.RootSlug,
                cancellationToken);

        if (folder is not null)
        {
            Report(progress, "Deleting the seeded media library.");

            await PurgeMediaAsync(folder, cancellationToken);

            removed = true;
        }

        // Tags and the shared footer are not under either root, so they are found by the naming the
        // seeder gives them. Templates are deliberately left behind: a person may have authored
        // against them, and a tool that removes its own scaffolding is a tool that deletes content.
        await context.Tags
            .Where(tag => tag.Slug.StartsWith(TagPrefix))
            .ExecuteDeleteAsync(cancellationToken);

        var footer = await context.ReusableContents
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(candidate => candidate.Key == FooterKey(options), cancellationToken);

        if (footer is not null)
        {
            await context.ReusableContents
                .Where(candidate => candidate.Id == footer.Id)
                .ExecuteUpdateAsync(
                    setters => setters
                        .SetProperty(candidate => candidate.DraftVersionId, (int?)null)
                        .SetProperty(candidate => candidate.PublishedVersionId, (int?)null),
                    cancellationToken);

            await context.ReusableContentVersions
                .Where(version => version.ReusableContentId == footer.Id)
                .ExecuteDeleteAsync(cancellationToken);

            await context.ReusableContents
                .IgnoreQueryFilters()
                .Where(candidate => candidate.Id == footer.Id)
                .ExecuteDeleteAsync(cancellationToken);

            removed = true;
        }

        context.ChangeTracker.Clear();

        return removed;
    }

    private async Task PurgePagesAsync(Page root, CancellationToken cancellationToken)
    {
        var prefix = root.Path;

        var pageIds = context.Pages
            .IgnoreQueryFilters()
            .Where(page => page.Path.StartsWith(prefix))
            .Select(page => page.Id);

        await context.SearchDocuments
            .Where(document =>
                document.EntityType == SearchEntityKind.Page && pageIds.Contains(document.EntityId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.PageTags
            .Where(tag => pageIds.Contains(tag.PageId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.PageRoutes
            .Where(route => pageIds.Contains(route.PageId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.PageAcls
            .Where(acl => pageIds.Contains(acl.PageId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.Redirects
            .Where(redirect => redirect.ToPageId != null && pageIds.Contains(redirect.ToPageId!.Value))
            .ExecuteDeleteAsync(cancellationToken);

        // The version pointers are what stop the versions being deletable, so they go first — the
        // relationship is restricted on purpose, to make a page that points at a deleted version
        // impossible (spec section 23.5).
        await context.Pages
            .IgnoreQueryFilters()
            .Where(page => page.Path.StartsWith(prefix))
            .ExecuteUpdateAsync(
                setters => setters
                    .SetProperty(page => page.DraftVersionId, (int?)null)
                    .SetProperty(page => page.PublishedVersionId, (int?)null),
                cancellationToken);

        var versionIds = context.PageVersions
            .Where(version => pageIds.Contains(version.PageId))
            .Select(version => version.Id);

        await context.ContentReferences
            .Where(reference =>
                reference.SourceType == ContentSourceType.PageVersion &&
                versionIds.Contains(reference.SourceVersionId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.PageVersions
            .Where(version => pageIds.Contains(version.PageId))
            .ExecuteDeleteAsync(cancellationToken);

        var deepest = await context.Pages
            .IgnoreQueryFilters()
            .Where(page => page.Path.StartsWith(prefix))
            .MaxAsync(page => (int?)page.Depth, cancellationToken) ?? root.Depth;

        // Deepest first: a page's parent relationship is restricted, so a branch cannot be deleted
        // while anything still hangs below it.
        for (var depth = deepest; depth >= root.Depth; depth--)
        {
            var level = depth;

            await context.Pages
                .IgnoreQueryFilters()
                .Where(page => page.Path.StartsWith(prefix) && page.Depth == level)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private async Task PurgeMediaAsync(MediaFolder root, CancellationToken cancellationToken)
    {
        var prefix = root.Path;

        var folderIds = context.MediaFolders
            .IgnoreQueryFilters()
            .Where(folder => folder.Path.StartsWith(prefix))
            .Select(folder => folder.Id);

        var mediaIds = context.MediaItems
            .IgnoreQueryFilters()
            .Where(item => item.FolderId != null && folderIds.Contains(item.FolderId!.Value))
            .Select(item => item.Id);

        await context.SearchDocuments
            .Where(document =>
                document.EntityType == SearchEntityKind.Media && mediaIds.Contains(document.EntityId))
            .ExecuteDeleteAsync(cancellationToken);

        // Rendition rows go; the blobs behind them do not. Deleting a hundred thousand generated
        // files one call at a time would take longer than the seeding did, and the media store of a
        // load-test environment is scratch space by definition — see docs/load-testing.md.
        await context.MediaRenditions
            .Where(rendition => mediaIds.Contains(rendition.MediaItemId))
            .ExecuteDeleteAsync(cancellationToken);

        await context.MediaItems
            .IgnoreQueryFilters()
            .Where(item => item.FolderId != null && folderIds.Contains(item.FolderId!.Value))
            .ExecuteDeleteAsync(cancellationToken);

        var deepest = await context.MediaFolders
            .IgnoreQueryFilters()
            .Where(folder => folder.Path.StartsWith(prefix))
            .MaxAsync(folder => (int?)folder.Path.Length, cancellationToken) ?? prefix.Length;

        for (var length = deepest; length >= prefix.Length; length--)
        {
            var depth = length;

            await context.MediaFolders
                .IgnoreQueryFilters()
                .Where(folder => folder.Path.StartsWith(prefix) && folder.Path.Length == depth)
                .ExecuteDeleteAsync(cancellationToken);
        }
    }

    private Task<Page?> FindRootAsync(string slug, CancellationToken cancellationToken) =>
        context.Pages
            .AsNoTracking()
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(page => page.ParentId == null && page.Slug == slug, cancellationToken);

    private async Task<LoadTestSeedReport> DescribeAsync(
        Page root,
        LoadTestSeedOptions options,
        TimeSpan elapsed,
        CancellationToken cancellationToken)
    {
        var pages = await context.Pages
            .IgnoreQueryFilters()
            .Where(page => page.Path.StartsWith(root.Path))
            .CountAsync(cancellationToken);

        var published = await context.Pages
            .IgnoreQueryFilters()
            .Where(page => page.Path.StartsWith(root.Path) && page.PublishedVersionId != null)
            .CountAsync(cancellationToken);

        var url = await context.PageRoutes
            .Where(route => route.PageId == root.Id && !route.IsPublished)
            .Select(route => route.Url)
            .FirstOrDefaultAsync(cancellationToken) ?? SiteUrls.Combine(null, options.RootSlug);

        logger.LogInformation(
            "A load-test dataset is already present under {RootUrl}: {PageCount} pages. " +
            "Pass reset to rebuild it.",
            url,
            pages);

        return new LoadTestSeedReport(
            root.Id,
            url,
            pages,
            published,
            PageVersions: 0,
            MediaItems: 0,
            DistinctImages: 0,
            Tags: 0,
            Redirects: 0,
            SearchDocuments: 0,
            elapsed,
            ManifestPath: null,
            AlreadySeeded: true);
    }

    private static string FooterKey(LoadTestSeedOptions options) => $"{options.RootSlug}-footer";

    private static void Report(IProgress<string>? progress, string message) => progress?.Report(message);

    /// <summary>The structure the seeded pages are authored against.</summary>
    private sealed record SeedStructure(
        int ArticleTemplateId,
        int ArticleRevision,
        int LandingTemplateId,
        int LandingRevision,
        int FooterReusableId);

    /// <summary>The media rows that exist for content to point at.</summary>
    private sealed record MediaSet(int FirstId, int Count);

    /// <summary>One page, and everything derived from it that later passes need.</summary>
    private sealed class PlannedPage
    {
        /// <summary>The row itself, with its identity and path already decided.</summary>
        public required Page Entity { get; init; }

        /// <summary>The URL its routes carry.</summary>
        public required string Url { get; init; }

        /// <summary>The title its versions carry.</summary>
        public required string Title { get; init; }

        /// <summary>Whether it has a published version and a live route.</summary>
        public required bool Published { get; init; }

        /// <summary>Whether its draft has moved on since it was published.</summary>
        public required bool Edited { get; init; }

        /// <summary>Whether it is authored against the landing template rather than the article one.</summary>
        public required bool IsLanding { get; init; }

        /// <summary>Its ordinal in the plan, which decides which media it points at.</summary>
        public required int Index { get; init; }

        /// <summary>Identity of the draft version row.</summary>
        public required int DraftVersionId { get; init; }

        /// <summary>Identity of the published version row, or zero when there is none.</summary>
        public required int PublishedVersionId { get; init; }

        /// <summary>Tags applied to it.</summary>
        public int[] TagIds { get; set; } = [];
    }

    /// <summary>Everything decided before a row is written.</summary>
    private sealed class SeedPlan
    {
        /// <summary>Every page, parents before children.</summary>
        public required List<PlannedPage> Pages { get; init; }

        /// <summary>The root everything hangs below.</summary>
        public required PlannedPage Root { get; init; }

        /// <summary>The one branch that runs to the depth the ACL work is measured at.</summary>
        public required List<PlannedPage> DeepChain { get; init; }

        /// <summary>The structure the payloads name.</summary>
        public required SeedStructure Structure { get; init; }

        /// <summary>The media the payloads point at.</summary>
        public required MediaSet Media { get; init; }

        /// <summary>The instant every seeded row is stamped with.</summary>
        public required DateTimeOffset Now { get; init; }

        /// <summary>The seed the content generator was given.</summary>
        public required int RandomSeed { get; init; }

        /// <summary>Identity of the first tag, which turns a tag id back into its slug.</summary>
        public required int TagFirstId { get; init; }

        /// <summary>The URLs the seeded redirects are served at.</summary>
        public List<string> RedirectUrls { get; } = [];

        /// <summary>URL of the root page.</summary>
        public string RootUrl => Root.Url;

        /// <summary>How many pages an anonymous request can reach.</summary>
        public int PublishedCount => Pages.Count(page => page.Published);

        /// <summary>How many version rows the plan implies.</summary>
        public int VersionCount => Pages.Count + PublishedCount;

        /// <summary>How many search rows were written for pages.</summary>
        public int SearchDocumentCount { get; set; }
    }

    /// <summary>
    /// Creates the templates and the shared footer, adding to whatever is already there.
    /// </summary>
    /// <remarks>
    /// The templates almost always exist before this runs and almost always have no zones: the
    /// structure reconciler creates a row for every <c>[CmsTemplate]</c> component it finds at
    /// startup, and zones are defined by a developer afterwards. So adopting an existing template
    /// as it stands is not an option — the payloads would name zones it does not declare, every
    /// zone would be <em>orphaned</em>, and the first editor to press publish on a seeded page
    /// would be refused.
    /// <para>
    /// Missing zones are therefore added, exactly as <c>cms schema apply</c> does it: additively,
    /// never touching a zone that is already there, and with a new revision recording the result.
    /// A zone that exists under a different field type is left alone and logged, because its
    /// definition is somebody's decision and this is a load-test tool.
    /// </para>
    /// </remarks>
    private async Task<SeedStructure> EnsureStructureAsync(
        LoadTestSeedOptions options,
        CancellationToken cancellationToken)
    {
        var article = await EnsureTemplateAsync(
            LoadTestContent.ArticleTemplateKey,
            "Article",
            ArticleZones(),
            cancellationToken);

        var landing = await EnsureTemplateAsync(
            LoadTestContent.LandingTemplateKey,
            "Marketing Landing Page",
            LandingZones(),
            cancellationToken);

        var footer = await EnsureFooterAsync(options, cancellationToken);

        return new SeedStructure(
            article.Id,
            article.CurrentRevision,
            landing.Id,
            landing.CurrentRevision,
            footer);
    }

    private async Task<Template> EnsureTemplateAsync(
        string key,
        string name,
        Zone[] zones,
        CancellationToken cancellationToken)
    {
        var template = await context.Templates
            .Include(candidate => candidate.Zones)
            .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

        if (template is null)
        {
            template = new Template
            {
                Key = key,
                Name = name,
                Description = "Created by the load-test seeder (task P9-12).",
                IsEnabled = true,
                CurrentRevision = 0,
            };

            context.Templates.Add(template);
        }

        var added = false;

        foreach (var wanted in zones)
        {
            var zone = template.Zones.FirstOrDefault(
                candidate => string.Equals(candidate.Key, wanted.Key, StringComparison.OrdinalIgnoreCase));

            if (zone is null)
            {
                template.Zones.Add(wanted);
                added = true;

                continue;
            }

            if (string.Equals(zone.FieldTypeKey, wanted.FieldTypeKey, StringComparison.Ordinal)) continue;

            // Left as it is. The seeded payload will carry a value of the wrong shape for this one
            // zone, which renders as nothing and fails a publish check on that zone alone — better
            // than a tool that rewrites a field type somebody's existing content is stored under.
            logger.LogWarning(
                "Template {TemplateKey} declares zone {ZoneKey} as {Existing}, not {Wanted}. It was " +
                "left alone, so seeded content for that zone will not match it.",
                key,
                zone.Key,
                zone.FieldTypeKey,
                wanted.FieldTypeKey);
        }

        if (added)
        {
            template.Revisions.Add(new TemplateRevision
            {
                RevisionNumber = template.CurrentRevision + 1,
                ZoneSnapshotJson = ContentSchemaSnapshot.WriteZones(template.Zones),
                Notes = "Zones added by the load-test seeder.",
            });

            template.CurrentRevision += 1;
        }

        await context.SaveChangesAsync(cancellationToken);

        logger.LogInformation(
            "Template {TemplateKey} is at revision {Revision} with {ZoneCount} zone(s).",
            key,
            template.CurrentRevision,
            template.Zones.Count);

        return template;
    }

    /// <summary>The zones the article payloads fill, matching the shipped <c>Article</c> component.</summary>
    private static Zone[] ArticleZones() =>
    [
        Zone("kicker", "Kicker", FieldTypeKeys.PlainText, 0),
        Zone("standfirst", "Standfirst", FieldTypeKeys.MultilineText, 1),
        Zone("publishedAt", "Published at", FieldTypeKeys.DateTime, 2),
        Zone("readingMinutes", "Reading time", FieldTypeKeys.Number, 3),
        Zone("isFeatured", "Featured", FieldTypeKeys.Boolean, 4),
        Zone("poster", "Poster", FieldTypeKeys.Media, 5),
        Zone("body", "Body", FieldTypeKeys.Blocks, 6),
        Zone("gallery", "Gallery", FieldTypeKeys.MediaList, 7),
        Zone("tags", "Tags", FieldTypeKeys.Tags, 8),
        Zone("related", "Related", FieldTypeKeys.PageReference, 9),
    ];

    /// <summary>The zones the landing payloads fill.</summary>
    private static Zone[] LandingZones() =>
    [
        Zone("hero", "Hero", FieldTypeKeys.Media, 0),
        Zone("intro", "Introduction", FieldTypeKeys.RichText, 1),
        Zone("body", "Body", FieldTypeKeys.Blocks, 2),
        Zone("cta", "Call to action", FieldTypeKeys.Link, 3),
        Zone("footer", "Footer", FieldTypeKeys.Reusable, 4),
    ];

    private static Zone Zone(string key, string name, string fieldTypeKey, int sortOrder) =>
        new() { Key = key, Name = name, FieldTypeKey = fieldTypeKey, SortOrder = sortOrder };

    /// <summary>
    /// Creates the one reusable item every landing page references late-bound.
    /// </summary>
    /// <remarks>
    /// Its point is the fan-out: publishing it has to invalidate every cached page that shows it,
    /// which at this scale is thousands of them at once. That is the cost <c>R8</c> names and the
    /// one thing a dataset of independent pages could never measure.
    /// </remarks>
    private async Task<int> EnsureFooterAsync(LoadTestSeedOptions options, CancellationToken cancellationToken)
    {
        var key = FooterKey(options);

        var existing = await context.ReusableContents
            .AsNoTracking()
            .FirstOrDefaultAsync(candidate => candidate.Key == key, cancellationToken);

        if (existing is not null) return existing.Id;

        var blockType = await context.BlockTypes
            .AsNoTracking()
            .FirstOrDefaultAsync(
                candidate => candidate.Key == CmsSeedData.RawHtmlBlockTypeKey,
                cancellationToken)
            ?? throw new InvalidOperationException(
                $"The built-in '{CmsSeedData.RawHtmlBlockTypeKey}' block type is missing, so the " +
                "database has not had its migrations applied.");

        var item = new ReusableContent
        {
            Key = key,
            Name = "Load-test footer",
            Description = "Referenced late-bound by every seeded landing page.",
            BlockTypeId = blockType.Id,
        };

        context.ReusableContents.Add(item);

        await context.SaveChangesAsync(cancellationToken);

        var payload = LoadTestContent.FooterPayload(
            blockType.Key,
            CmsSeedData.RawHtmlContentPropertyKey,
            blockType.CurrentRevision);

        var draft = new ReusableContentVersion
        {
            ReusableContentId = item.Id,
            VersionNumber = 1,
            Status = PageVersionStatus.Draft,
            ContentJson = payload,
            BlockTypeRevision = blockType.CurrentRevision,
        };

        var published = new ReusableContentVersion
        {
            ReusableContentId = item.Id,
            VersionNumber = 2,
            Status = PageVersionStatus.Published,
            ContentJson = payload,
            BlockTypeRevision = blockType.CurrentRevision,
            PublishedOn = clock.GetUtcNow(),
            PublishedBy = SystemUser,
        };

        context.ReusableContentVersions.AddRange(draft, published);

        await context.SaveChangesAsync(cancellationToken);

        item.DraftVersionId = draft.Id;
        item.PublishedVersionId = published.Id;

        await context.SaveChangesAsync(cancellationToken);

        context.ChangeTracker.Clear();

        return item.Id;
    }

    /// <summary>
    /// Writes the media library: a folder tree, and the rows that point at the image pool.
    /// </summary>
    private async Task<MediaSet> SeedMediaAsync(
        LoadTestSeedOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var pool = await LoadTestImagePool.CreateAsync(store, options.DistinctImages, cancellationToken);
        var folders = await SeedMediaFoldersAsync(options, cancellationToken);
        var first = await NextIdAsync<MediaItem>(cancellationToken);
        var batch = new List<MediaItem>(Math.Min(options.BatchSize, options.MediaItems));

        for (var index = 0; index < options.MediaItems; index++)
        {
            var image = pool[index % pool.Count];

            batch.Add(new MediaItem
            {
                Id = first + index,
                FolderId = folders[index % folders.Count],
                FileName = $"lt-{index:D6}{image.Extension}",
                OriginalFileName = $"load-test-{index:D6}{image.Extension}",
                ContentType = image.ContentType,
                SizeBytes = image.SizeBytes,

                // Synthetic, and deliberately not the hash of the bytes behind the storage key: the
                // live-hash index is unique, so a hundred thousand rows cannot honestly share two
                // dozen blobs. Nothing about deduplication can be measured on this data.
                Sha256 = SyntheticHash($"media:{index}"),
                StorageKey = image.StorageKey,
                MediaKind = MediaKind.Image,
                Width = image.Width,
                Height = image.Height,
                AltText = $"Generated load-test image {index:D6}",
                Title = $"Load-test image {index:D6}",
                Credit = "Load-test seeder",
                CreatedOn = now,
                CreatedBy = SystemUser,
                ModifiedOn = now,
                ModifiedBy = SystemUser,
            });

            if (batch.Count >= options.BatchSize)
            {
                await _writer.WriteAsync(batch, options.BatchSize, cancellationToken);
                batch.Clear();
            }
        }

        await _writer.WriteAsync(batch, options.BatchSize, cancellationToken);
        await _writer.ReseedAsync<MediaItem>(cancellationToken);

        return new MediaSet(first, options.MediaItems);
    }

    private async Task<List<int>> SeedMediaFoldersAsync(
        LoadTestSeedOptions options,
        CancellationToken cancellationToken)
    {
        var root = new MediaFolder { Name = options.RootSlug, Path = string.Empty };

        context.MediaFolders.Add(root);

        await context.SaveChangesAsync(cancellationToken);

        root.Path = $"/{root.Id}/";

        await context.SaveChangesAsync(cancellationToken);

        // One folder per thousand items, which is roughly what a library of this size looks like
        // once somebody has organised it, and enough for a folder listing to be a real query.
        var count = Math.Clamp(options.MediaItems / 1_000, 1, 200);
        var children = new List<MediaFolder>(count);

        for (var index = 0; index < count; index++)
        {
            children.Add(new MediaFolder
            {
                ParentId = root.Id,
                Name = $"Set {index + 1:D3}",
                Path = string.Empty,
                SortOrder = index,
            });
        }

        context.MediaFolders.AddRange(children);

        await context.SaveChangesAsync(cancellationToken);

        foreach (var child in children)
        {
            child.Path = $"{root.Path}{child.Id}/";
        }

        await context.SaveChangesAsync(cancellationToken);

        context.ChangeTracker.Clear();

        return [.. children.Select(child => child.Id)];
    }

    /// <summary>
    /// Decides every page, its identity, its URL, and which version rows it will have.
    /// </summary>
    /// <remarks>
    /// Identities are assigned here rather than by the database because a page's materialized path
    /// contains its own id and its parent's — deciding them up front is what turns the whole tree
    /// into one bulk insert instead of one round trip per node.
    /// <para>
    /// The shape is a wide, shallow site with one deliberately deep branch. Wide and shallow is
    /// what a real site of this size looks like, and the deep branch is there because ACL
    /// resolution and path prefix matching are the two costs that grow with depth (risk R15).
    /// </para>
    /// </remarks>
    private async Task<SeedPlan> PlanAsync(
        LoadTestSeedOptions options,
        SeedStructure structure,
        MediaSet media,
        TagSet tags,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var nextPageId = await NextIdAsync<Page>(cancellationToken);
        var nextVersionId = await NextIdAsync<PageVersion>(cancellationToken);
        var random = new Random(options.RandomSeed);
        var pages = new List<PlannedPage>(options.Pages);
        var chain = new List<PlannedPage>();

        PlannedPage Add(
            PlannedPage? parent,
            string slug,
            string title,
            bool isLanding,
            bool published,
            bool edited,
            bool recycled,
            int sortOrder)
        {
            var id = nextPageId++;
            var url = SiteUrls.Combine(parent?.Url, slug);

            var entity = new Page
            {
                Id = id,
                ParentId = parent?.Entity.Id,
                PublicId = NewGuid(random),
                Slug = slug,
                Path = parent is null ? $"/{id}/" : $"{parent.Entity.Path}{id}/",
                Depth = parent is null ? 0 : parent.Entity.Depth + 1,
                SortOrder = sortOrder,
                TemplateId = isLanding ? structure.LandingTemplateId : structure.ArticleTemplateId,
                ShowInNavigation = true,
                IsDeleted = recycled,
                DeletedOn = recycled ? now : null,
                DeletedBy = recycled ? SystemUser : null,
                CreatedOn = now,
                CreatedBy = SystemUser,
                ModifiedOn = now,
                ModifiedBy = SystemUser,
            };

            var planned = new PlannedPage
            {
                Entity = entity,
                Url = url,
                Title = title,
                Published = published,
                Edited = edited,
                IsLanding = isLanding,
                Index = pages.Count,
                DraftVersionId = nextVersionId++,
                PublishedVersionId = published ? nextVersionId++ : 0,
            };

            if (tags.Count > 0 && !isLanding)
            {
                planned.TagIds = [.. Enumerable
                    .Range(0, random.Next(0, 4))
                    .Select(_ => tags.FirstId + random.Next(tags.Count))
                    .Distinct()];
            }

            pages.Add(planned);

            return planned;
        }

        var root = Add(null, options.RootSlug, "Load test", true, true, false, false, 0);

        // Twelve sections and four hundred-odd topics at the default size. Both are derived from the
        // page count so that a small run is the same site in miniature rather than a different one.
        var sectionCount = Math.Clamp(options.Pages / 4_000, 3, 24);
        var topicCount = Math.Clamp(options.Pages / sectionCount / 120, 2, 60);
        var sections = new List<PlannedPage>();
        var topics = new List<PlannedPage>();

        for (var index = 0; index < sectionCount && pages.Count < options.Pages; index++)
        {
            sections.Add(Add(
                root,
                $"section-{index + 1:D2}",
                LoadTestContent.Title(random),
                true,
                true,
                false,
                false,
                index));
        }

        var topicNumber = 0;

        foreach (var section in sections)
        {
            for (var index = 0; index < topicCount && pages.Count < options.Pages; index++)
            {
                topics.Add(Add(
                    section,
                    $"topic-{++topicNumber:D4}",
                    LoadTestContent.Title(random),
                    true,
                    true,
                    false,
                    false,
                    index));
            }
        }

        var remaining = options.Pages - pages.Count;
        var hosts = topics.Count > 0 ? topics : sections.Count > 0 ? sections : new List<PlannedPage> { root };

        // The deep branch, taken out of the budget before the rest is spread. Eight levels below a
        // topic puts its leaf at depth ten, which is the depth the ACL measurement uses.
        var deep = Math.Min(8, remaining);
        var deepParent = hosts[0];

        for (var index = 0; index < deep; index++)
        {
            deepParent = Add(
                deepParent,
                $"deep-{index + 1:D2}",
                LoadTestContent.Title(random),
                false,
                true,
                false,
                false,
                0);

            chain.Add(deepParent);
        }

        remaining -= deep;

        foreach (var (host, count) in Spread(hosts, remaining, random))
        {
            for (var index = 0; index < count; index++)
            {
                var recycled = random.NextDouble() < options.RecycledShare;
                var published = !recycled && random.NextDouble() < options.PublishedShare;

                Add(
                    host,
                    $"page-{pages.Count:D6}",
                    LoadTestContent.Title(random),

                    // A fifth of the leaves are landing pages, which is what puts the shared footer
                    // on thousands of pages rather than on the four hundred branch pages. The
                    // invalidation fan-out of R8 is stated against ten thousand.
                    random.NextDouble() < options.LandingShare,
                    published,
                    published && random.NextDouble() < options.EditedShare,
                    recycled,
                    index + 1);
            }
        }

        return new SeedPlan
        {
            Pages = pages,
            Root = root,
            DeepChain = chain,
            Structure = structure,
            Media = media,
            Now = now,
            RandomSeed = options.RandomSeed,
            TagFirstId = tags.FirstId,
        };
    }

    /// <summary>
    /// Divides a page budget over the branches that will hold it, unevenly.
    /// </summary>
    /// <remarks>
    /// Unevenly on purpose: a site where every section holds the same number of pages makes every
    /// listing query cost the same, and the queries that hurt in practice are the ones over the one
    /// section that grew to ten times its neighbours.
    /// </remarks>
    private static List<(PlannedPage Host, int Count)> Spread(
        List<PlannedPage> hosts,
        int total,
        Random random)
    {
        var weights = new double[hosts.Count];
        var sum = 0d;

        for (var index = 0; index < hosts.Count; index++)
        {
            // Squared uniform: most branches near the average, a few several times larger.
            weights[index] = 0.2 + (Math.Pow(random.NextDouble(), 2) * 2);
            sum += weights[index];
        }

        var counts = new int[hosts.Count];
        var allocated = 0;

        for (var index = 0; index < hosts.Count; index++)
        {
            counts[index] = (int)Math.Floor(total * weights[index] / sum);
            allocated += counts[index];
        }

        // Rounding always leaves a few over. They go round-robin rather than all onto one branch.
        for (var index = 0; allocated < total; index = (index + 1) % hosts.Count)
        {
            counts[index]++;
            allocated++;
        }

        return [.. hosts.Select((host, index) => (host, counts[index]))];
    }

    /// <summary>The tag rows, and the id range the plan draws from.</summary>
    private sealed record TagSet(int FirstId, int Count);

    /// <summary>Writes the tag vocabulary the seeded pages are filed under.</summary>
    private async Task<TagSet> SeedTagsAsync(
        LoadTestSeedOptions options,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (options.Tags == 0) return new TagSet(0, 0);

        var first = await NextIdAsync<Tag>(cancellationToken);
        var tags = new List<Tag>(options.Tags);

        for (var index = 0; index < options.Tags; index++)
        {
            tags.Add(new Tag
            {
                Id = first + index,
                Name = $"Load test tag {index + 1:D3}",
                Slug = TagSlug(index),
                CreatedOn = now,
                CreatedBy = SystemUser,
                ModifiedOn = now,
                ModifiedBy = SystemUser,
            });
        }

        await _writer.WriteAsync(tags, options.BatchSize, cancellationToken);
        await _writer.ReseedAsync<Tag>(cancellationToken);

        return new TagSet(first, options.Tags);
    }

    private static string TagSlug(int index) => $"{TagPrefix}tag-{index + 1:D3}";

    /// <summary>
    /// Writes the pages, their versions, and their routes, then joins the three together.
    /// </summary>
    /// <remarks>
    /// Pages go in with their version pointers null and are repointed afterwards, because the two
    /// tables reference each other: a page names its draft and its published version, and a version
    /// names its page. Bulk copy checks the foreign keys — which is what keeps them trusted, and
    /// therefore what keeps the query plans a load test measures the same as production's — so one
    /// of the two directions has to be filled in by a later statement.
    /// </remarks>
    private async Task WritePagesAsync(
        LoadTestSeedOptions options,
        SeedPlan plan,
        CancellationToken cancellationToken)
    {
        // Parents before children: the self-reference is checked as each row lands.
        var ordered = plan.Pages.OrderBy(page => page.Entity.Depth).ToList();

        await _writer.WriteAsync(
            [.. ordered.Select(page => page.Entity)],
            options.BatchSize,
            cancellationToken);

        await _writer.ReseedAsync<Page>(cancellationToken);

        var versions = new List<PageVersion>(options.BatchSize);

        foreach (var page in plan.Pages)
        {
            foreach (var version in VersionsFor(page, plan))
            {
                versions.Add(version);

                if (versions.Count < options.BatchSize) continue;

                await _writer.WriteAsync(versions, options.BatchSize, cancellationToken);
                versions.Clear();
            }
        }

        await _writer.WriteAsync(versions, options.BatchSize, cancellationToken);
        await _writer.ReseedAsync<PageVersion>(cancellationToken);

        var nextRouteId = await NextIdAsync<PageRoute>(cancellationToken);
        var routes = new List<PageRoute>(options.BatchSize);

        foreach (var page in plan.Pages)
        {
            foreach (var route in RoutesFor(page, plan.Now))
            {
                route.Id = nextRouteId++;
                routes.Add(route);

                if (routes.Count < options.BatchSize) continue;

                await _writer.WriteAsync(routes, options.BatchSize, cancellationToken);
                routes.Clear();
            }
        }

        await _writer.WriteAsync(routes, options.BatchSize, cancellationToken);
        await _writer.ReseedAsync<PageRoute>(cancellationToken);

        await WriteReferencesAsync(options, plan, cancellationToken);
        await LinkVersionsAsync(plan, cancellationToken);
    }

    /// <summary>
    /// The version rows one page has: a draft always, and a published copy when it is live.
    /// </summary>
    /// <remarks>
    /// Two rows rather than one, because that is what publishing produces: the draft stays where the
    /// editor left it and the published row is an immutable copy of it (spec section 11.2). A page
    /// marked edited gets a draft that has moved on, which is the state the second non-negotiable
    /// test scenario is about and the state a delivery cache has to keep serving the older of.
    /// </remarks>
    private static IEnumerable<PageVersion> VersionsFor(PlannedPage page, SeedPlan plan)
    {
        var random = new Random(plan.RandomSeed ^ page.Entity.Id);
        var revision = page.IsLanding ? plan.Structure.LandingRevision : plan.Structure.ArticleRevision;
        var draftJson = Payload(page, plan, random);

        yield return new PageVersion
        {
            Id = page.DraftVersionId,
            PageId = page.Entity.Id,
            VersionNumber = 1,
            Status = PageVersionStatus.Draft,
            Title = page.Title,
            ContentJson = draftJson,
            TemplateId = page.Entity.TemplateId,
            TemplateRevision = revision,
            MetaTitle = page.Title,
            MetaDescription = LoadTestContent.Sentence(random),
            RobotsIndex = true,
            RobotsFollow = true,
            CreatedOn = plan.Now,
            CreatedBy = SystemUser,
            ModifiedOn = plan.Now,
            ModifiedBy = SystemUser,
        };

        if (!page.Published) yield break;

        yield return new PageVersion
        {
            Id = page.PublishedVersionId,
            PageId = page.Entity.Id,
            VersionNumber = 2,
            Status = PageVersionStatus.Published,
            Title = page.Title,

            // An edited page's published content is a different payload from its draft, so that a
            // request for the public page and a preview of the draft return different bytes.
            ContentJson = page.Edited ? Payload(page, plan, random) : draftJson,
            TemplateId = page.Entity.TemplateId,
            TemplateRevision = revision,
            MetaTitle = page.Title,
            MetaDescription = LoadTestContent.Sentence(random),
            RobotsIndex = true,
            RobotsFollow = true,
            PublishedOn = plan.Now,
            PublishedBy = SystemUser,
            CreatedOn = plan.Now,
            CreatedBy = SystemUser,
            ModifiedOn = plan.Now,
            ModifiedBy = SystemUser,
        };
    }

    private static string Payload(PlannedPage page, SeedPlan plan, Random random)
    {
        var poster = MediaAt(plan.Media, page.Index);
        var related = page.Entity.ParentId ?? page.Entity.Id;

        return page.IsLanding
            ? LoadTestContent.LandingPayload(
                random,
                plan.Structure.LandingRevision,
                poster,
                related,
                plan.Structure.FooterReusableId)
            : LoadTestContent.ArticlePayload(
                random,
                plan.Structure.ArticleRevision,
                poster,
                [MediaAt(plan.Media, page.Index + 1), MediaAt(plan.Media, page.Index + 2), MediaAt(plan.Media, page.Index + 3)],
                related,
                [.. page.TagIds.Select(id => TagSlug(id - plan.TagFirstId))],
                plan.Now.AddDays(-(page.Index % 900)));
    }

    private static int MediaAt(MediaSet media, int offset) => media.FirstId + (offset % media.Count);

    /// <summary>
    /// The routes one page has: a draft route always, and a live one when it is published.
    /// </summary>
    /// <remarks>
    /// The draft route exists from the moment the page does, which is what lets preview address a
    /// page by URL before it has ever been published (spec section 10.4). A recycled page has
    /// neither a published version nor a live route — the recycle bin is not a way to keep serving.
    /// </remarks>
    private static IEnumerable<PageRoute> RoutesFor(PlannedPage page, DateTimeOffset now)
    {
        yield return new PageRoute
        {
            PageId = page.Entity.Id,
            Url = page.Url,
            UrlHash = SiteUrls.Hash(page.Url),
            IsPrimary = true,
            IsPublished = false,
            CreatedOn = now,
        };

        if (!page.Published) yield break;

        yield return new PageRoute
        {
            PageId = page.Entity.Id,
            Url = page.Url,
            UrlHash = SiteUrls.Hash(page.Url),
            IsPrimary = true,
            IsPublished = true,
            CreatedOn = now,
        };
    }

    /// <summary>
    /// Writes the reference rows the payloads imply.
    /// </summary>
    /// <remarks>
    /// Derived from what the seeder just wrote rather than extracted from the JSON it wrote it into.
    /// Extraction is what the application does — <c>ReferenceIndexer</c> over every version — and
    /// re-parsing a hundred thousand payloads here would add most of a minute to a run for an answer
    /// this code already has.
    /// <para>
    /// Without these rows the table the where-used walk reads would be empty, and a load test of
    /// publishing the shared footer would report the cost of invalidating nothing at all. This is
    /// the dataset half of risk <c>R8</c>.
    /// </para>
    /// </remarks>
    private async Task WriteReferencesAsync(
        LoadTestSeedOptions options,
        SeedPlan plan,
        CancellationToken cancellationToken)
    {
        var nextId = await NextIdAsync<EntityReference>(cancellationToken);
        var batch = new List<EntityReference>(options.BatchSize);

        foreach (var page in plan.Pages)
        {
            foreach (var versionId in Versions(page))
            {
                foreach (var reference in ReferencesFor(page, plan, versionId))
                {
                    reference.Id = nextId++;
                    batch.Add(reference);

                    if (batch.Count < options.BatchSize) continue;

                    await _writer.WriteAsync(batch, options.BatchSize, cancellationToken);
                    batch.Clear();
                }
            }
        }

        await _writer.WriteAsync(batch, options.BatchSize, cancellationToken);
        await _writer.ReseedAsync<EntityReference>(cancellationToken);
    }

    /// <summary>The version ids one page has.</summary>
    private static IEnumerable<int> Versions(PlannedPage page)
    {
        yield return page.DraftVersionId;

        if (page.Published) yield return page.PublishedVersionId;
    }

    /// <summary>What one version points at.</summary>
    private static IEnumerable<EntityReference> ReferencesFor(PlannedPage page, SeedPlan plan, int versionId)
    {
        EntityReference Row(ContentReferenceTargetType type, int targetId, string zoneKey) => new()
        {
            SourceType = ContentSourceType.PageVersion,
            SourceVersionId = versionId,
            TargetType = type,
            TargetId = targetId,
            ZoneKey = zoneKey,
            IsPinned = false,
        };

        var related = page.Entity.ParentId ?? page.Entity.Id;

        if (page.IsLanding)
        {
            yield return Row(ContentReferenceTargetType.Media, MediaAt(plan.Media, page.Index), "hero");
            yield return Row(ContentReferenceTargetType.Page, related, "cta");

            // Late-bound: no pinned version, so publishing the footer changes what this page shows.
            yield return Row(ContentReferenceTargetType.ReusableContent, plan.Structure.FooterReusableId, "footer");

            yield break;
        }

        yield return Row(ContentReferenceTargetType.Media, MediaAt(plan.Media, page.Index), "poster");

        for (var offset = 1; offset <= 3; offset++)
        {
            yield return Row(ContentReferenceTargetType.Media, MediaAt(plan.Media, page.Index + offset), "gallery");
        }

        yield return Row(ContentReferenceTargetType.Page, related, "related");
    }

    /// <summary>Points every seeded page at the versions written for it.</summary>
    private async Task LinkVersionsAsync(SeedPlan plan, CancellationToken cancellationToken)
    {
        var firstId = plan.Pages[0].Entity.Id;
        var lastId = plan.Pages[^1].Entity.Id;

        foreach (var (property, status) in new[]
        {
            (nameof(Page.DraftVersionId), PageVersionStatus.Draft),
            (nameof(Page.PublishedVersionId), PageVersionStatus.Published),
        })
        {
            var sql =
                $"""
                UPDATE p
                SET p.{_writer.ColumnName<Page>(property)} = v.{_writer.ColumnName<PageVersion>(nameof(PageVersion.Id))}
                FROM {_writer.QualifiedTableName<Page>()} AS p
                INNER JOIN {_writer.QualifiedTableName<PageVersion>()} AS v
                    ON v.{_writer.ColumnName<PageVersion>(nameof(PageVersion.PageId))} = p.{_writer.ColumnName<Page>(nameof(Page.Id))}
                    AND v.{_writer.ColumnName<PageVersion>(nameof(PageVersion.Status))} = @status
                WHERE p.{_writer.ColumnName<Page>(nameof(Page.Id))} BETWEEN @first AND @last
                """;

            await context.Database.ExecuteSqlRawAsync(
                sql,
                [
                    new SqlParameter("@status", (byte)status),
                    new SqlParameter("@first", firstId),
                    new SqlParameter("@last", lastId),
                ],
                cancellationToken);
        }
    }

    /// <summary>
    /// Writes what hangs off the pages: their tags, the redirects into them, and the search index.
    /// </summary>
    /// <remarks>
    /// The search rows are written here rather than left to the indexer for the same reason the
    /// pages are: the outbox would rebuild a hundred and fifty thousand documents one message at a
    /// time. What is lost is the extraction itself — these bodies are generated prose rather than
    /// text pulled out of each zone — so a search measurement over this data is a measurement of the
    /// query and the index, never of the indexer.
    /// </remarks>
    private async Task WriteAncillaryAsync(
        LoadTestSeedOptions options,
        SeedPlan plan,
        MediaSet media,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var nextTagId = await NextIdAsync<PageTag>(cancellationToken);
        var pageTags = new List<PageTag>();

        foreach (var page in plan.Pages)
        {
            foreach (var tagId in page.TagIds)
            {
                pageTags.Add(new PageTag
                {
                    Id = nextTagId++,
                    PageId = page.Entity.Id,
                    TagId = tagId,
                    CreatedOn = now,
                    CreatedBy = SystemUser,
                    ModifiedOn = now,
                    ModifiedBy = SystemUser,
                });
            }
        }

        await WriteBatchedAsync(pageTags, options, cancellationToken);
        await _writer.ReseedAsync<PageTag>(cancellationToken);

        await WriteRedirectsAsync(options, plan, now, cancellationToken);
        await WriteSearchDocumentsAsync(options, plan, media, now, cancellationToken);
    }

    /// <summary>
    /// Leaves redirects pointing into the seeded tree, so the 301 path has something to serve.
    /// </summary>
    private async Task WriteRedirectsAsync(
        LoadTestSeedOptions options,
        SeedPlan plan,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var targets = plan.Pages.Where(page => page.Published).ToList();

        if (options.Redirects == 0 || targets.Count == 0) return;

        var nextId = await NextIdAsync<Redirect>(cancellationToken);
        var redirects = new List<Redirect>(options.Redirects);

        for (var index = 0; index < options.Redirects; index++)
        {
            var from = SiteUrls.Combine(plan.RootUrl, $"archive/{index + 1:D5}");

            plan.RedirectUrls.Add(from);

            redirects.Add(new Redirect
            {
                Id = nextId++,
                FromUrl = from,
                FromUrlHash = SiteUrls.Hash(from),
                ToPageId = targets[index % targets.Count].Entity.Id,
                StatusCode = 301,
                IsAutomatic = true,
                IsEnabled = true,
                Notes = "Seeded by the load-test seeder.",
                CreatedOn = now,
                CreatedBy = SystemUser,
                ModifiedOn = now,
                ModifiedBy = SystemUser,
            });
        }

        await WriteBatchedAsync(redirects, options, cancellationToken);
        await _writer.ReseedAsync<Redirect>(cancellationToken);
    }

    /// <summary>Writes one search row per live page and one per media item.</summary>
    private async Task WriteSearchDocumentsAsync(
        LoadTestSeedOptions options,
        SeedPlan plan,
        MediaSet media,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var nextId = await NextIdAsync<SearchDocument>(cancellationToken);
        var batch = new List<SearchDocument>(options.BatchSize);
        var written = 0;

        async Task FlushAsync(bool force)
        {
            if (batch.Count == 0 || (!force && batch.Count < options.BatchSize)) return;

            await _writer.WriteAsync(batch, options.BatchSize, cancellationToken);
            batch.Clear();
        }

        foreach (var page in plan.Pages)
        {
            // A recycled page is not findable, which is what the indexer does with one: the document
            // is removed rather than kept with a flag (spec section 18.4).
            if (page.Entity.IsDeleted) continue;

            var random = new Random(plan.RandomSeed ^ page.Entity.Id);

            batch.Add(new SearchDocument
            {
                Id = nextId++,
                EntityType = SearchEntityKind.Page,
                EntityId = page.Entity.Id,
                Title = page.Title,
                Body = LoadTestContent.SearchBody(random),
                Keywords = string.Join(' ', page.TagIds.Select(id => TagSlug(id - plan.TagFirstId))),
                Url = page.Url,
                IsPublished = page.Published,
                UpdatedOn = now,
            });

            written++;

            await FlushAsync(false);
        }

        for (var index = 0; index < media.Count; index++)
        {
            batch.Add(new SearchDocument
            {
                Id = nextId++,
                EntityType = SearchEntityKind.Media,
                EntityId = media.FirstId + index,
                Title = $"Load-test image {index:D6}",
                Body = $"Generated load-test image {index:D6}",
                Keywords = "image jpeg load-test",
                Url = null,
                IsPublished = true,
                UpdatedOn = now,
            });

            written++;

            await FlushAsync(false);
        }

        await FlushAsync(true);
        await _writer.ReseedAsync<SearchDocument>(cancellationToken);

        plan.SearchDocumentCount = written;
    }

    private async Task WriteBatchedAsync<TEntity>(
        List<TEntity> rows,
        LoadTestSeedOptions options,
        CancellationToken cancellationToken)
        where TEntity : class
    {
        for (var offset = 0; offset < rows.Count; offset += options.BatchSize)
        {
            var slice = rows.GetRange(offset, Math.Min(options.BatchSize, rows.Count - offset));

            await _writer.WriteAsync(slice, options.BatchSize, cancellationToken);
        }
    }

    /// <summary>
    /// Writes the file the load-test scripts read their URLs out of.
    /// </summary>
    /// <remarks>
    /// A k6 script that discovered URLs by crawling would spend its first minutes crawling, and a
    /// script with URLs hard-coded in it would go stale the first time the dataset was reseeded.
    /// The sample is taken across the tree rather than off the front of it, so a run does not spend
    /// itself on one section's rows.
    /// </remarks>
    private static async Task<string?> WriteManifestAsync(
        LoadTestSeedOptions options,
        SeedPlan plan,
        MediaSet media,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        if (options.ManifestPath is not { Length: > 0 } path) return null;

        var published = plan.Pages.Where(page => page.Published).ToList();

        var manifest = new LoadTestManifest(
            now,
            options.RandomSeed,
            plan.RootUrl,
            new LoadTestManifestCounts(
                plan.Pages.Count,
                published.Count,
                media.Count,
                options.DistinctImages,
                options.Tags,
                options.Redirects,
                plan.SearchDocumentCount),
            [.. Sample(published, options.ManifestSampleSize).Select(page => page.Url)],
            [.. Sample([.. published.Where(page => page.IsLanding)], options.ManifestSampleSize).Select(page => page.Url)],
            [.. plan.DeepChain.Select(page => page.Url)],
            [.. Sample(plan.RedirectUrls, Math.Min(options.ManifestSampleSize, 200))],
            [.. Enumerable.Range(1, 50).Select(index => SiteUrls.Combine(plan.RootUrl, $"missing/{index:D3}"))],
            [.. Enumerable.Range(0, Math.Min(options.Tags, 50)).Select(TagSlug)],
            media.FirstId,
            media.Count);

        var directory = Path.GetDirectoryName(Path.GetFullPath(path));

        if (directory is { Length: > 0 })
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(path, LoadTestManifest.Write(manifest), cancellationToken);

        return Path.GetFullPath(path);
    }

    /// <summary>Takes an evenly spaced sample, so it spans the tree rather than its first branch.</summary>
    private static List<T> Sample<T>(List<T> source, int wanted)
    {
        if (wanted <= 0 || source.Count == 0) return [];
        if (source.Count <= wanted) return [.. source];

        var step = (double)source.Count / wanted;
        var sample = new List<T>(wanted);

        for (var index = 0; index < wanted; index++)
        {
            sample.Add(source[(int)(index * step)]);
        }

        return sample;
    }

    private async Task<int> NextIdAsync<TEntity>(CancellationToken cancellationToken)
        where TEntity : EntityBase =>
        (await context.Set<TEntity>()
            .IgnoreQueryFilters()
            .MaxAsync(entity => (int?)entity.Id, cancellationToken) ?? 0) + 1;

    /// <summary>
    /// A public id drawn from the seeded generator rather than <see cref="Guid.NewGuid"/>.
    /// </summary>
    /// <remarks>
    /// Reproducibility beats uniqueness across databases here: two runs of the same options must
    /// produce the same site, and these ids never leave the load-test environment.
    /// </remarks>
    private static Guid NewGuid(Random random)
    {
        var bytes = new byte[16];

        random.NextBytes(bytes);

        return new Guid(bytes);
    }

    /// <summary>A distinct 32-byte value for a row whose bytes are shared with other rows.</summary>
    private static byte[] SyntheticHash(string value) =>
        SHA256.HashData(Encoding.UTF8.GetBytes(value));
}
