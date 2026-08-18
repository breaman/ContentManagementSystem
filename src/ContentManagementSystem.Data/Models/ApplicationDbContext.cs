using ContentManagementSystem.Data.Interceptors;
using ContentManagementSystem.Data.Models.Cms;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace ContentManagementSystem.Data.Models;

public class ApplicationDbContext : AuthDbContext
{
    /// <summary>
    /// The only constructor, which is what lets the factory's activator build one.
    /// </summary>
    /// <param name="options">
    /// Context options. In the application these carry the save-time interceptors — see
    /// <see cref="CmsSaveInterceptors"/>; a context built from options that omit them saves without
    /// stamping fingerprints or writing audit rows.
    /// </param>
    /// <remarks>
    /// It used to be three, one of them marked <c>[ActivatorUtilitiesConstructor]</c>, because the
    /// context took <c>IUserService</c> and <c>TimeProvider</c> for the save-time behaviour that now
    /// lives in interceptors. <c>IDbContextFactory</c> builds contexts through
    /// <c>ActivatorUtilities</c> (ADR-0022), which — unlike the service provider's greedy rule —
    /// refuses to choose between constructors that all accept the arguments it was given, and failed
    /// at startup with "multiple constructors accepting all given argument types". With one
    /// constructor taking only options there is nothing left to choose between.
    /// </remarks>
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }

    /// <summary>Page shapes a developer defines and editors create pages from.</summary>
    public DbSet<Template> Templates => Set<Template>();

    /// <summary>Immutable snapshots of each template's zone definitions.</summary>
    public DbSet<TemplateRevision> TemplateRevisions => Set<TemplateRevision>();

    /// <summary>Typed content slots belonging to templates.</summary>
    public DbSet<Zone> Zones => Set<Zone>();

    /// <summary>Shapes of the repeatable items placed inside <c>blocks</c> zones.</summary>
    public DbSet<BlockType> BlockTypes => Set<BlockType>();

    /// <summary>Immutable snapshots of each block type's property definitions.</summary>
    public DbSet<BlockTypeRevision> BlockTypeRevisions => Set<BlockTypeRevision>();

    /// <summary>Typed properties declared directly on a block type.</summary>
    public DbSet<BlockTypeProperty> BlockTypeProperties => Set<BlockTypeProperty>();

    /// <summary>Reusable property groups block types inherit from.</summary>
    public DbSet<Composition> Compositions => Set<Composition>();

    /// <summary>Typed properties belonging to a composition.</summary>
    public DbSet<CompositionProperty> CompositionProperties => Set<CompositionProperty>();

    /// <summary>Assignments of compositions to block types.</summary>
    public DbSet<BlockTypeComposition> BlockTypeCompositions => Set<BlockTypeComposition>();

    /// <summary>The single row of site-wide configuration.</summary>
    public DbSet<SiteSettings> SiteSettings => Set<SiteSettings>();

    /// <summary>Nodes of the content tree.</summary>
    public DbSet<Page> Pages => Set<Page>();

    /// <summary>Every state of every page — drafts, published versions, and archived history.</summary>
    public DbSet<PageVersion> PageVersions => Set<PageVersion>();

    /// <summary>Content authored once and placed on many pages — footers, banners, carousels.</summary>
    public DbSet<ReusableContent> ReusableContents => Set<ReusableContent>();

    /// <summary>Every state of every reusable item — drafts, published versions, and history.</summary>
    public DbSet<ReusableContentVersion> ReusableContentVersions => Set<ReusableContentVersion>();

    /// <summary>Edges from stored content to the entities it depends on, rebuilt on every save.</summary>
    public DbSet<ContentReference> ContentReferences => Set<ContentReference>();

    /// <summary>Advisory notes that an editor has a page open.</summary>
    public DbSet<EditLock> EditLocks => Set<EditLock>();

    /// <summary>Materialized page URLs, resolved against on every public request.</summary>
    public DbSet<PageRoute> PageRoutes => Set<PageRoute>();

    /// <summary>Standing instructions to send one URL to another.</summary>
    public DbSet<Redirect> Redirects => Set<Redirect>();

    /// <summary>Unresolved URLs and how often they are asked for.</summary>
    public DbSet<NotFoundLog> NotFoundLogs => Set<NotFoundLog>();

    /// <summary>Bearer capabilities to view one unpublished page version.</summary>
    public DbSet<PreviewToken> PreviewTokens => Set<PreviewToken>();

    /// <summary>The media library's organizing tree.</summary>
    public DbSet<MediaFolder> MediaFolders => Set<MediaFolder>();

    /// <summary>Uploaded files, their metadata, and their non-destructive edits.</summary>
    public DbSet<MediaItem> MediaItems => Set<MediaItem>();

    /// <summary>Derived encodings of media items. Regenerable; excluded from the audit log.</summary>
    public DbSet<MediaRendition> MediaRenditions => Set<MediaRendition>();

    /// <inheritdoc />
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        base.OnConfiguring(optionsBuilder);

        // Page carries a soft-delete query filter and is the required end of relationships from
        // PageVersion and EditLock, which EF warns may filter dependents out unexpectedly. Here
        // that is the intent: a deleted page's version history has to stay retrievable — it is the
        // thing the recycle bin exists to preserve (spec section 23.5) — and giving the dependents
        // a matching filter would hide exactly the rows a restore needs to find. Suppressed here
        // rather than at each registration so the decision travels with the model that made it.
        // ReusableContent and ReusableContentVersion are the same pairing for the same reason.
        optionsBuilder.ConfigureWarnings(warnings => warnings.Ignore(
            CoreEventId.PossibleIncorrectRequiredNavigationWithQueryFilterInteractionWarning));
    }

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        // Applied from the assembly rather than listed one by one, so adding an entity in a later
        // phase is a single new file rather than an edit here that is easy to forget.
        builder.ApplyConfigurationsFromAssembly(typeof(ApplicationDbContext).Assembly);
    }
}
