using ContentManagementSystem.Shared.Contracts.Workflow;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Audit;

/// <summary>
/// The audit log, filtered by entity, user, and date (task P7-20, criterion P7 #10).
/// </summary>
/// <remarks>
/// The criterion is a stopwatch: "who unpublished the homepage and when" in under three
/// interactions. That is what the filter row is shaped around — pick the entity, type its id,
/// search. Everything else the log could be filtered by is left out, because a screen with nine
/// boxes takes more than three interactions to use whatever it can do.
/// <para>
/// The two value documents are rendered inside <c>&lt;pre&gt;</c> as text. They hold whatever an
/// editor typed, including markup they typed on purpose, and this screen is read by the people with
/// the most permissions on the site.
/// </para>
/// </remarks>
public partial class AuditLogViewer : ComponentBase
{
    /// <summary>How many entries one page holds.</summary>
    private const int PageSize = 50;

    /// <summary>
    /// The tables worth filtering by, as the audit log names them.
    /// </summary>
    /// <remarks>
    /// A short list rather than every table in the schema, and specifically not read from the log's
    /// own distinct values: that query gets slower as the table it reads grows, on a screen whose
    /// whole job is answering a question quickly. Anything not listed is still reachable by leaving
    /// the filter on "everything".
    /// </remarks>
    public static IReadOnlyList<string> Entities { get; } =
    [
        "Page",
        "PageVersion",
        "PageRoute",
        "Redirect",
        "MediaItem",
        "Template",
        "BlockType",
        "SiteSettings",
        "PageAcl",
        "WorkflowTask",
    ];

    /// <summary>Reads the log.</summary>
    [Inject]
    private IWorkflowClient Client { get; set; } = default!;

    /// <summary>The entries so far, or null while the first page loads.</summary>
    [PersistentState]
    public List<AuditEntrySummary>? Entries { get; set; }

    /// <summary>Whether a request is in flight.</summary>
    private bool IsBusy { get; set; }

    private string? _entity;
    private string? _entityId;
    private int? _userId;
    private DateTime? _from;
    private DateTime? _to;
    private string? _next;

    /// <inheritdoc />
    protected override async Task OnInitializedAsync() => await LoadAsync(reset: true);

    private Task SearchAsync() => LoadAsync(reset: true);

    private Task MoreAsync() => LoadAsync(reset: false);

    private async Task LoadAsync(bool reset)
    {
        IsBusy = true;

        try
        {
            var page = await Client.GetAuditAsync(new AuditQuery(
                string.IsNullOrWhiteSpace(_entity) ? null : _entity,
                string.IsNullOrWhiteSpace(_entityId) ? null : _entityId.Trim(),
                _userId,
                _from is { } from ? new DateTimeOffset(from, TimeSpan.Zero) : null,

                // The "to" box is a date, and a date filter that meant midnight would silently
                // exclude everything that happened on the day the reader chose.
                _to is { } to ? new DateTimeOffset(to.AddDays(1).AddTicks(-1), TimeSpan.Zero) : null,
                reset ? null : _next,
                PageSize));

            if (page is null)
            {
                Entries = [];
                _next = null;

                return;
            }

            Entries = reset ? [.. page.Items] : [.. Entries ?? [], .. page.Items];
            _next = page.NextCursor;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
