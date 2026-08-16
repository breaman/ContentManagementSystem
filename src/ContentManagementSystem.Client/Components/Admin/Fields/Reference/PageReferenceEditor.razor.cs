using System.Text.Json;
using System.Text.Json.Nodes;

using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.Shared.Services;

using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Client.Components.Admin.Fields.Reference;

/// <summary>
/// The <c>pageReference</c> editor — one page or an ordered several (task P6-15, spec section 7.1).
/// </summary>
/// <remarks>
/// Stores identity and never a URL (ADR-0006), which is what makes a "related articles" list pick up
/// a retitled page without being re-authored. The consequence for the control is that a stored value
/// is a list of numbers, and numbers are not something an author can check — so each one is resolved
/// to its title, once, when the value changes.
/// <para>
/// The order is the author's and survives exactly as written, which is why the multiple form carries
/// move controls rather than only a remove. A "featured first" list whose order nobody can change is
/// a list that has to be emptied and rebuilt to move one item.
/// </para>
/// </remarks>
public partial class PageReferenceEditor : FieldEditorBase
{
    [Inject]
    private IPageClient Client { get; set; } = default!;

    /// <summary>Whether the picker is open.</summary>
    private bool IsPicking { get; set; }

    /// <summary>Titles of the chosen pages, keyed by id, filled in as they resolve.</summary>
    private Dictionary<int, string> Titles { get; } = [];

    /// <summary>Whether the slot holds several pages.</summary>
    private bool IsMultiple => ConfiguredBoolean(FieldSettingNames.Multiple);

    /// <summary>The template keys the slot accepts.</summary>
    private IReadOnlyList<string> AllowedTemplates => ConfiguredTextList(FieldSettingNames.AllowedTemplates);

    /// <summary>Fewest pages the slot requires.</summary>
    private int? Min => IsMultiple ? ConfiguredInt32(FieldSettingNames.Min) : null;

    /// <summary>Most pages the slot allows.</summary>
    private int? Max => IsMultiple ? ConfiguredInt32(FieldSettingNames.Max) : null;

    /// <summary>The chosen page ids, in stored order.</summary>
    private IReadOnlyList<int> Chosen => Read(Value);

    /// <summary>Whether the slot will take no more pages.</summary>
    private bool IsFull => !IsMultiple && Chosen.Count > 0 || Max is { } max && Chosen.Count >= max;

    /// <summary>How many pages the slot wants, said in words as well as enforced at publish.</summary>
    private string? CountRule => (Min, Max) switch
    {
        (null, null) => null,
        ({ } min, { } max) => $"Between {min} and {max} pages.",
        ({ } min, null) => $"At least {min}.",
        (null, { } max) => $"At most {max}.",
    };

    /// <summary>Page ids that have been looked up, whether or not a title came back.</summary>
    /// <remarks>
    /// Separate from <see cref="Titles"/> so a page that has since been deleted is asked about once
    /// rather than on every render. The control falls back to the id for it either way; the publish
    /// check is what reports the broken reference.
    /// </remarks>
    private readonly HashSet<int> _asked = [];

    /// <inheritdoc />
    protected override async Task OnParametersSetAsync()
    {
        if (Chosen.All(_asked.Contains)) return;

        await ResolveAsync();
    }

    /// <summary>The title of a chosen page, falling back to its id while it resolves.</summary>
    /// <remarks>
    /// The id rather than a spinner or an empty row. It is what an editor would quote in a ticket,
    /// and it is also what the control shows for a page that has since been deleted — which the
    /// publish check reports as a broken reference rather than this control hiding.
    /// </remarks>
    private string Title(int pageId) =>
        Titles.TryGetValue(pageId, out var title) ? title : $"Page {pageId}";

    /// <summary>Looks up every chosen page that has not been resolved yet.</summary>
    private async Task ResolveAsync()
    {
        foreach (var pageId in Chosen.Where(id => _asked.Add(id)).ToList())
        {
            if (await Client.GetAsync(pageId) is { } page)
            {
                Titles[pageId] = page.Summary.Title;
            }
        }
    }

    /// <summary>Adds the chosen page, or replaces the one already there.</summary>
    private async Task OnPickedAsync(PageSummary page)
    {
        IsPicking = false;
        Titles[page.Id] = page.Title;
        _asked.Add(page.Id);

        var chosen = IsMultiple ? Chosen.Append(page.Id).ToList() : [page.Id];

        await WriteChosenAsync(chosen);
    }

    private Task RemoveAsync(int pageId) =>
        WriteChosenAsync([.. Chosen.Where(id => id != pageId)]);

    /// <summary>Moves one entry by a step, clamped to the ends of the list.</summary>
    private Task MoveAsync(int index, int step)
    {
        var chosen = Chosen.ToList();
        var target = index + step;

        if (target < 0 || target >= chosen.Count) return Task.CompletedTask;

        (chosen[index], chosen[target]) = (chosen[target], chosen[index]);

        return WriteChosenAsync(chosen);
    }

    /// <summary>
    /// Writes the chosen ids in the shape the property is configured for.
    /// </summary>
    /// <remarks>
    /// A bare number for a single-value property and an array for a multiple one, under the same
    /// member either way — which is the field type's rule, and the reason it gives for it: a renderer
    /// that has to look in two places for the same thing eventually looks in only one.
    /// </remarks>
    private Task WriteChosenAsync(IReadOnlyList<int> chosen)
    {
        if (chosen.Count == 0) return WriteAsync(string.Empty);

        JsonNode value = IsMultiple
            ? new JsonArray([.. chosen.Select(id => (JsonNode?)JsonValue.Create(id))])
            : JsonValue.Create(chosen[0]);

        return WriteAsync(StoredValue.Write(Value, FieldTypeKey, value));
    }

    /// <summary>Reads the stored ids, tolerating the single form and the array form alike.</summary>
    private static IReadOnlyList<int> Read(string? json)
    {
        if (StoredValue.Parse(json) is not { } stored ||
            stored[StoredValue.ValueMember] is not { } node)
        {
            return [];
        }

        return node switch
        {
            JsonArray array =>
            [
                .. array
                    .Where(entry => entry?.GetValueKind() is JsonValueKind.Number)
                    .Select(entry => entry!.GetValue<int>()),
            ],
            _ when node.GetValueKind() is JsonValueKind.Number => [node.GetValue<int>()],
            _ => [],
        };
    }
}
