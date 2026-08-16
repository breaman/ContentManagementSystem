namespace ContentManagementSystem.Core.Delivery;

/// <summary>
/// The reusable items already being rendered above this point in one page render (task P4-06).
/// </summary>
/// <remarks>
/// An immutable singly-linked list, pushed onto as the renderer descends and never mutated, so that
/// two sibling placements at the same depth cannot see each other's descent. A shared mutable set
/// would report the second of two sibling placements of the same item as a cycle, which it is not:
/// a footer may legitimately appear twice on one page.
/// <para>
/// It is a render-time structure and carries no authority. Cycles are refused when content is
/// written (<c>ReusableCodes.Cycle</c>); this exists because content can reach the database by other
/// routes — a restore from a backup older than that check, a hand-edited payload, an import — and on
/// a public request the only acceptable answer to a loop is to stop, log, and render the rest of the
/// page (spec section 15.3).
/// </para>
/// </remarks>
public sealed class ReusableResolutionChain
{
    /// <summary>
    /// How many levels of reusable nesting the delivery path will follow.
    /// </summary>
    /// <remarks>
    /// The same ceiling the impact walk counts to and the cycle check refuses beyond, and they have
    /// to agree: an impact report that counted a page the renderer stops short of would promise a
    /// change that never arrives.
    /// </remarks>
    public const int MaxDepth = Content.ReferenceQueryService.MaxDepth;

    /// <summary>The chain at the top of a page render: nothing above it.</summary>
    public static ReusableResolutionChain Root { get; } = new(0, null, null);

    private readonly ReusableResolutionChain? _parent;
    private readonly int? _reusableContentId;

    private ReusableResolutionChain(int depth, int? reusableContentId, ReusableResolutionChain? parent)
    {
        Depth = depth;
        _reusableContentId = reusableContentId;
        _parent = parent;
    }

    /// <summary>How many reusable items are being rendered above this point.</summary>
    public int Depth { get; }

    /// <summary>Whether descending one more level would exceed <see cref="MaxDepth"/>.</summary>
    public bool IsAtMaxDepth => Depth >= MaxDepth;

    /// <summary>Whether an item is already being rendered above this point.</summary>
    /// <param name="reusableContentId">The item a placement names.</param>
    /// <returns><see langword="true"/> when rendering it would close a loop.</returns>
    public bool Contains(int reusableContentId)
    {
        for (var link = this; link is not null; link = link._parent)
        {
            if (link._reusableContentId == reusableContentId) return true;
        }

        return false;
    }

    /// <summary>Descends into an item.</summary>
    /// <param name="reusableContentId">The item now being rendered.</param>
    /// <returns>The chain as seen by everything inside that item.</returns>
    public ReusableResolutionChain Push(int reusableContentId) =>
        new(Depth + 1, reusableContentId, this);
}
