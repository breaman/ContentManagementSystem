using System.Text.Json.Nodes;

using ContentManagementSystem.Client.Components.Admin.Fields;
using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Client.Tests.Fields;

/// <summary>
/// The envelope reader and writer every field editor binds through (tasks P6-06 to P6-15).
/// </summary>
/// <remarks>
/// Two rules carry most of the weight here, and both are about not losing things: a write keeps
/// members it did not write, and an emptied control removes the slot rather than storing an empty
/// value. The first is how a crop written by the media screen survives somebody editing the alt
/// text; the second is how a renderer keeps "never authored" and "deliberately cleared" apart.
/// </remarks>
public class StoredValueTests
{
    [Test]
    public void ReadingTakesTheValueOutOfTheEnvelope()
    {
        StoredValue.ReadText("""{ "type": "plainText", "value": "Hello" }""").Should().Be("Hello");
    }

    [Test]
    public void AWriteKeepsEveryMemberItDidNotWrite()
    {
        var stored = StoredValue.Write(
            """{ "type": "media", "mediaId": 7, "crop": { "x": 0.1 } }""",
            FieldTypeKeys.Media,
            node => node["altOverride"] = "A cat");

        var written = JsonNode.Parse(stored)!.AsObject();

        written["mediaId"]!.GetValue<int>().Should().Be(7);
        written["crop"]!["x"]!.GetValue<double>().Should().Be(0.1);
        written["altOverride"]!.GetValue<string>().Should().Be("A cat");
    }

    [Test]
    public void AnEmptiedControlRemovesTheSlotRatherThanStoringNothing()
    {
        // Absent means never authored and null means deliberately cleared. Writing "" for a box
        // somebody simply never filled in would tell the renderer a fallback had been declined.
        StoredValue.WriteText("""{ "type": "plainText", "value": "Hello" }""", FieldTypeKeys.PlainText, "")
            .Should().BeEmpty();
    }

    [Test]
    public void AValueWithNoEnvelopeYetGetsItsDiscriminator()
    {
        var stored = StoredValue.WriteText(null, FieldTypeKeys.PlainText, "Hello");

        JsonNode.Parse(stored)!["type"]!.GetValue<string>().Should().Be(FieldTypeKeys.PlainText);
    }

    [Test]
    public void AnUnparseableValueReadsAsNothingRatherThanThrowing()
    {
        // A value this cannot parse is one the validator has already complained about against the
        // same property; a second complaint from the control would put two messages on one defect.
        StoredValue.Parse("not json at all").Should().BeNull();
        StoredValue.ReadText("not json at all").Should().BeNull();
        StoredValue.ReadItems("not json at all", "items").Should().BeEmpty();
    }

    [Test]
    public void AChoiceWrittenAsOneValueStillReadsAsAList()
    {
        // The field type stores one value or an array under the same member, so a property switched
        // to multiple has to be able to read what was written before the switch.
        StoredValue.ReadTextList("""{ "type": "choice", "value": "wide" }""").Should().Equal("wide");
        StoredValue.ReadTextList("""{ "type": "choice", "value": ["wide", "tall"] }""")
            .Should().Equal("wide", "tall");
    }

    [Test]
    public void AnIntegerMemberRefusesANumberThatIsNotOne()
    {
        StoredValue.ReadInt32("""{ "value": 44 }""").Should().Be(44);
        StoredValue.ReadInt32("""{ "value": 44.5 }""").Should().BeNull();
        StoredValue.ReadInt32("""{ "value": "44" }""").Should().BeNull();
    }
}
