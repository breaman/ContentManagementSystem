using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using S2.DynamicSsr.Cms;

namespace S2.DynamicSsr.Content.Blocks;

/// <summary>
/// Throws from a lifecycle method — the ordinary "my renderer hit a null" failure.
/// </summary>
[CmsBlockType("throws-in-lifecycle", "Throws during OnParametersSet")]
public sealed class ThrowsInLifecycleBlock : CmsBlockBase
{
    protected override void OnParametersSet() =>
        throw new InvalidOperationException("Deliberate failure from OnParametersSet.");

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        builder.AddMarkupContent(0, "<p>unreachable</p>");
}

/// <summary>
/// Throws while building the render tree, after sibling markup has already been emitted. This is the
/// harder case: if the boundary only wraps lifecycle methods, half-written markup escapes.
/// </summary>
[CmsBlockType("throws-in-render", "Throws during BuildRenderTree")]
public sealed class ThrowsInRenderBlock : CmsBlockBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "section");
        builder.AddAttribute(1, "class", "half-written");
        builder.AddMarkupContent(2, "<span data-cms-partial-block-output=\"1\"></span>");

        throw new InvalidOperationException("Deliberate failure from BuildRenderTree.");
    }
}

/// <summary>
/// Throws from an asynchronous lifecycle method, after an await has already yielded.
/// </summary>
[CmsBlockType("throws-async", "Throws after awaiting")]
public sealed class ThrowsAsyncBlock : CmsBlockBase
{
    protected override async Task OnParametersSetAsync()
    {
        await Task.Yield();

        throw new InvalidOperationException("Deliberate failure from OnParametersSetAsync, post-await.");
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) =>
        builder.AddMarkupContent(0, "<p>unreachable</p>");
}

/// <summary>Awaits real work before rendering — the normal asynchronous block case.</summary>
[CmsBlockType("slow", "Awaits before rendering")]
public sealed class SlowBlock : CmsBlockBase
{
    private string _body = string.Empty;

    protected override async Task OnParametersSetAsync()
    {
        await Task.Delay(5);

        _body = Text("body");
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddAttribute(1, "class", "slow");
        builder.AddContent(2, _body);
        builder.CloseElement();
    }
}
