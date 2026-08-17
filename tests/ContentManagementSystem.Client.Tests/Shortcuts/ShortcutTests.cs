using Bunit;

using ContentManagementSystem.Client.Components.Admin.Shortcuts;

namespace ContentManagementSystem.Client.Tests.Shortcuts;

/// <summary>
/// The keyboard shortcuts and the list that documents them (task P6-23).
/// </summary>
/// <remarks>
/// The listener's own key handling is driven directly rather than through a key press, because the
/// press it answers to lands on the <em>document</em> — that is the point of it — and a rendering
/// test has no document to press a key on. What the suite can pin, and what matters, is that the
/// chord table and the reference dialog are the same list, and that a chord is claimed only when
/// every modifier matches.
/// </remarks>
public class ShortcutTests : IDisposable
{
    private readonly BunitContext _bunit = new();

    public ShortcutTests() => _bunit.JSInterop.Mode = JSRuntimeMode.Loose;

    public void Dispose()
    {
        _bunit.Dispose();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public void TheReferenceDialogDocumentsEveryShortcutTheListenerAnswersTo()
    {
        var dialog = _bunit.Render<ShortcutHelpDialog>(parameters => parameters
            .Add(component => component.IsOpen, true)
            .Add(component => component.Shortcuts, EditorShortcuts.All));

        foreach (var shortcut in EditorShortcuts.All)
        {
            dialog.Markup.Should().Contain(
                shortcut.Description,
                "a shortcut that works and is not documented is one nobody finds");
        }

        // Written for both keyboards, because the browser cannot be asked which one is attached and
        // guessing from the user agent is how a Mac user is told to press a key they do not have.
        dialog.Markup.Should().Contain("Ctrl / ⌘");

        // Dismissed before teardown: a dialog torn down while open runs its focus-trap interop
        // against a renderer that is going away.
        dialog.Find("[role=dialog] .btn-outline-secondary").Click();
    }

    [Fact]
    public void AnEditorWhoMayNotWriteIsNotOfferedTheShortcutsThatWrite()
    {
        var offered = EditorShortcuts.For(canEdit: false);

        offered.Should().NotContain(shortcut => shortcut.Id == EditorShortcuts.Save);
        offered.Should().Contain(shortcut => shortcut.Id == EditorShortcuts.ShowHelp);
    }

    [Theory]
    [InlineData("s", true, false, EditorShortcuts.Save)]
    // The letter as the keyboard reports it when Shift is down, which is what a browser sends.
    [InlineData("S", true, false, EditorShortcuts.Save)]
    [InlineData("p", true, true, EditorShortcuts.Publish)]
    [InlineData("?", false, true, EditorShortcuts.ShowHelp)]
    public async Task AChordRunsTheShortcutItNames(string key, bool control, bool shift, string expected)
    {
        string? ran = null;

        var listener = Listener(id => ran = id);

        (await listener.Instance.MatchAsync(key, control, shift, alt: false)).Should().BeTrue();

        ran.Should().Be(expected);
    }

    [Theory]
    // Without the modifier it is a letter somebody typed.
    [InlineData("s", false, false, false)]
    // With one modifier too many it is a different chord, which may belong to the browser.
    [InlineData("s", true, true, false)]
    // Alt is the tree's move modifier and composes characters on several layouts, so it is matched
    // as "not held" rather than ignored.
    [InlineData("s", true, false, true)]
    public async Task AChordThatIsNotAShortcutIsLeftAlone(string key, bool control, bool shift, bool alt)
    {
        string? ran = null;

        var listener = Listener(id => ran = id);

        (await listener.Instance.MatchAsync(key, control, shift, alt)).Should().BeFalse(
            "claiming a press the editor did not define takes it away from the browser");

        ran.Should().BeNull();
    }

    /// <summary>Renders a listener over the real chord table.</summary>
    private IRenderedComponent<ShortcutListener> Listener(Action<string> onShortcut) =>
        _bunit.Render<ShortcutListener>(parameters => parameters
            .Add(component => component.Shortcuts, EditorShortcuts.All)
            .Add(component => component.OnShortcut, onShortcut));
}
