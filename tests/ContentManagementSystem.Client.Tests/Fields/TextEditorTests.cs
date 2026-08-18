using Bunit;

using ContentManagementSystem.Client.Components.Admin.Fields.Text;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Client.Tests.Fields;

/// <summary>
/// The plain-text and multiline editors, and the counter beside them (tasks P6-12 and P6-14).
/// </summary>
public class TextEditorTests : IDisposable
{
    private readonly FieldEditorHarness _harness = new();

    public void Dispose()
    {
        _harness.Dispose();
        GC.SuppressFinalize(this);
    }

    [Test]
    public void WhatIsTypedIsStoredInsideTheEnvelope()
    {
        var editor = _harness.Render<PlainTextEditor>(
            FieldEditorHarness.Context(FieldEditorHarness.Slot(FieldTypeKeys.PlainText)));

        editor.Find("input[type=text]").Input("What our plans cost");

        _harness.Last.Should().Be("""{"type":"plainText","value":"What our plans cost"}""");
    }

    [Test]
    public void ClearingTheBoxRemovesTheSlotRatherThanStoringAnEmptyString()
    {
        var editor = _harness.Render<PlainTextEditor>(
            FieldEditorHarness.Context(FieldEditorHarness.Slot(FieldTypeKeys.PlainText)),
            """{"type":"plainText","value":"Hello"}""");

        editor.Find("input[type=text]").Input(string.Empty);

        _harness.Last.Should().BeEmpty();
    }

    [Test]
    public void APlainTextZoneIsOneLineBecauseTheFieldTypeRefusesLineBreaks()
    {
        var editor = _harness.Render<PlainTextEditor>(
            FieldEditorHarness.Context(FieldEditorHarness.Slot(FieldTypeKeys.PlainText)));

        // A control an author can press Enter in is a control that invites a value the validator
        // will reject, a screen later, at publish time.
        editor.FindAll("textarea").Should().BeEmpty();
        editor.Find("input").GetAttribute("type").Should().Be("text");
    }

    [Test]
    public void TheCountUpdatesAsTheAuthorTypesRatherThanOnCommit()
    {
        var editor = _harness.Render<PlainTextEditor>(
            FieldEditorHarness.Context(FieldEditorHarness.Slot(FieldTypeKeys.PlainText)));

        editor.Find("input[type=text]").Input("Hello");

        editor.Find(".cms-field-count").TextContent.Should().Contain("5 characters");
    }

    [Test]
    public void PassingTheSoftLimitAdvisesAndPassingTheHardOneRefuses()
    {
        var slot = FieldEditorHarness.Slot(
            FieldTypeKeys.PlainText,
            $$"""{ "{{FieldSettingNames.SoftLimit}}": 3, "{{FieldSettingNames.MaxLength}}": 6 }""");

        var editor = _harness.Render<PlainTextEditor>(FieldEditorHarness.Context(slot));

        editor.Find("input[type=text]").Input("abcd");

        // Advice, phrased as advice: nothing on the server reads the soft limit and no value is
        // refused for passing it.
        var advisory = editor.Find(".cms-field-count");
        advisory.ClassList.Should().Contain("cms-field-count--warning");
        advisory.TextContent.Should().Contain("still publish");

        editor.Find("input[type=text]").Input("abcdefgh");

        var refusal = editor.Find(".cms-field-count");
        refusal.ClassList.Should().Contain("cms-field-count--over");
        refusal.TextContent.Should().Contain("publishing will refuse this");
    }

    [Test]
    public void TheBrowsersOwnCeilingIsNotTheConfiguredMaximum()
    {
        var slot = FieldEditorHarness.Slot(
            FieldTypeKeys.PlainText,
            $$"""{ "{{FieldSettingNames.MaxLength}}": 60 }""");

        var editor = _harness.Render<PlainTextEditor>(FieldEditorHarness.Context(slot));

        // A maxlength set to the real limit silently swallows the keystrokes past it: the author
        // types a longer headline, sees a shorter one, and has no idea why. The counter tells them.
        editor.Find("input[type=text]").GetAttribute("maxlength").Should().NotBe("60");
    }

    [Test]
    public void TheControlIsNamedAndDescribedByTheCardAroundIt()
    {
        var editor = _harness.Render<PlainTextEditor>(
            FieldEditorHarness.Context(FieldEditorHarness.Slot(FieldTypeKeys.PlainText)));

        var input = editor.Find("input[type=text]");

        input.Id.Should().Be("zone-zone-control");
        input.GetAttribute("aria-labelledby").Should().Be("zone-zone-name");
        input.GetAttribute("aria-describedby").Should().Contain("zone-zone-help");
    }

    [Test]
    public void TheMultilinePreviewBreaksLinesTheWayTheRendererDoes()
    {
        var editor = _harness.Render<MultilineTextEditor>(
            FieldEditorHarness.Context(FieldEditorHarness.Slot(FieldTypeKeys.MultilineText)),
            """{"type":"multilineText","value":"One\r\nTwo"}""");

        editor.Find(".cms-field__preview-toggle").Click();

        // A <br> between, not white-space: pre-wrap, and one break for \r\n rather than two —
        // which is exactly what MultilineTextRenderer emits.
        var preview = editor.Find(".cms-field__preview .cms-content p");

        preview.QuerySelectorAll("br").Should().ContainSingle();
        preview.TextContent.Should().Contain("One").And.Contain("Two");
    }

    [Test]
    public void AReadOnlyFormCannotBeTypedInto()
    {
        var editor = _harness.Render<PlainTextEditor>(
            FieldEditorHarness.Context(FieldEditorHarness.Slot(FieldTypeKeys.PlainText), disabled: true));

        editor.Find("input[type=text]").HasAttribute("disabled").Should().BeTrue();
    }
}
