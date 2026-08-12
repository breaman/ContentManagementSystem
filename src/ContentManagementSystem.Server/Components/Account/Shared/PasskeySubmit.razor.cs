using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Components;

namespace ContentManagementSystem.Server.Components.Account.Shared;

public partial class PasskeySubmit : ComponentBase
{
    [Inject] private IServiceProvider Services { get; set; } = default!;

    private AntiforgeryTokenSet? _tokens;

    [CascadingParameter]
    private HttpContext HttpContext { get; set; } = default!;

    [Parameter]
    [EditorRequired]
    public PasskeyOperation Operation { get; set; }

    [Parameter]
    [EditorRequired]
    public string Name { get; set; } = default!;

    [Parameter]
    public string? EmailName { get; set; }

    [Parameter]
    public RenderFragment? ChildContent { get; set; }

    [Parameter(CaptureUnmatchedValues = true)]
    public IDictionary<string, object>? AdditionalAttributes { get; set; }

    protected override void OnInitialized()
    {
        _tokens = Services.GetService<IAntiforgery>()?.GetTokens(HttpContext);
    }
}