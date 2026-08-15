using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;

namespace ContentManagementSystem.Core.Tests.Publishing;

/// <summary>
/// The diff algorithm itself — reorder, insert, delete, and a change nested inside a block
/// (task P2-25, spec section 11.4).
/// </summary>
/// <remarks>
/// Driven straight at <see cref="PayloadDiff"/> over two payload documents, with no page, no
/// template, and no database. The service around it loads two rows and compares their metadata; the
/// part that can be wrong in an interesting way is here, and reaching it through a publish would
/// spend a container per case to exercise the same method.
/// <para>
/// The block ids are written out as literals rather than generated. A diff matches blocks on their
/// GUID, so which id appears on which side <em>is</em> the arrangement — a generated one would make
/// the reorder case unreadable and its failure message worse.
/// </para>
/// </remarks>
public class PayloadDiffTests
{
    private const string BlockA = "11111111-1111-4111-8111-111111111111";
    private const string BlockB = "22222222-2222-4222-8222-222222222222";
    private const string BlockC = "33333333-3333-4333-8333-333333333333";

    private static readonly PayloadDiff Diff = new(ContentEngineHarness.Registry);

    [Fact]
    public void AReorderedBlockIsReportedAsMovedWithItsBeforeAndAfterPositions()
    {
        var changes = Compare(Blocks(BlockA, BlockB, BlockC), Blocks(BlockC, BlockA, BlockB));

        var zone = changes.Should().ContainSingle().Subject;

        // The distinction the whole structural diff exists for. Compared positionally, rotating
        // three blocks reads as three removals and three additions (acceptance criterion P2 #6).
        zone.Blocks.Should().HaveCount(3);
        zone.Blocks.Should().AllSatisfy(block => block.Kind.Should().Be(ContentChangeKind.Moved));

        zone.Blocks.Should().Contain(block =>
            block.BlockId == Guid.Parse(BlockC) && block.BeforeIndex == 2 && block.AfterIndex == 0);
        zone.Blocks.Should().Contain(block =>
            block.BlockId == Guid.Parse(BlockA) && block.BeforeIndex == 0 && block.AfterIndex == 1);
    }

    [Fact]
    public void ABlockInsertedInTheMiddleIsOneAdditionAndOneMove()
    {
        var changes = Compare(Blocks(BlockA, BlockC), Blocks(BlockA, BlockB, BlockC));

        var zone = changes.Should().ContainSingle().Subject;

        zone.Blocks.Should().Contain(block =>
            block.BlockId == Guid.Parse(BlockB) &&
            block.Kind == ContentChangeKind.Added &&
            block.BeforeIndex == null &&
            block.AfterIndex == 1);

        // The block the insertion pushed down moved and did not change; the one above it is silent.
        zone.Blocks.Should().Contain(block =>
            block.BlockId == Guid.Parse(BlockC) && block.Kind == ContentChangeKind.Moved);
        zone.Blocks.Should().NotContain(block => block.BlockId == Guid.Parse(BlockA));
    }

    [Fact]
    public void ADeletedBlockIsReportedOnceAtThePositionItHeld()
    {
        var changes = Compare(Blocks(BlockA, BlockB, BlockC), Blocks(BlockA, BlockC));

        var zone = changes.Should().ContainSingle().Subject;

        var removed = zone.Blocks.Should()
            .ContainSingle(block => block.Kind == ContentChangeKind.Removed).Subject;

        removed.BlockId.Should().Be(Guid.Parse(BlockB));
        removed.BeforeIndex.Should().Be(1);
        removed.AfterIndex.Should().BeNull("a removed block has no position in the later version");

        // Not removed-plus-added, and not a second entry for the same block.
        zone.Blocks.Should().NotContain(block =>
            block.BlockId == Guid.Parse(BlockB) && block.Kind == ContentChangeKind.Added);
    }

    [Fact]
    public void AChangeInsideOneBlockIsReportedOnThatBlockAndItsSiblingsStaySilent()
    {
        var before = Blocks(BlockA, BlockB);
        var after = before.Replace(
            $"text {BlockB}",
            "text rewritten entirely",
            StringComparison.Ordinal);

        var zone = Compare(before, after).Should().ContainSingle().Subject;

        // Only the block that changed. A diff that lists every block on the page is one nobody reads.
        var block = zone.Blocks.Should().ContainSingle().Subject;
        block.BlockId.Should().Be(Guid.Parse(BlockB));
        block.Kind.Should().Be(ContentChangeKind.Changed);
        block.BeforeIndex.Should().Be(1);
        block.AfterIndex.Should().Be(1);

        var property = block.Properties.Should().ContainSingle().Subject;
        property.Key.Should().Be("heading");
        property.FieldTypeKey.Should().Be("plainText");
        property.Segments.Should().Contain(segment => segment.Kind == ContentChangeKind.Added);
        property.Segments.Should().Contain(segment => segment.Kind == ContentChangeKind.Removed);
    }

    [Fact]
    public void ABlockThatBothMovedAndChangedIsReportedAsChanged()
    {
        var before = Blocks(BlockA, BlockB);
        var after = Blocks(BlockB, BlockA).Replace(
            $"text {BlockB}",
            "text rewritten entirely",
            StringComparison.Ordinal);

        var zone = Compare(before, after).Should().ContainSingle().Subject;

        // Changed wins over Moved, and the indexes still say where it went. Reporting the move
        // instead would hide an edit behind a reorder, which is the wrong way round to be wrong.
        var block = zone.Blocks.Single(candidate => candidate.BlockId == Guid.Parse(BlockB));
        block.Kind.Should().Be(ContentChangeKind.Changed);
        block.BeforeIndex.Should().Be(1);
        block.AfterIndex.Should().Be(0);
    }

    [Fact]
    public void AddingAPropertyToABlockIsAnAdditionAndRemovingOneIsARemoval()
    {
        var bare = $$"""
            { "id": "{{BlockA}}", "blockTypeKey": "rawHtml", "blockTypeRevision": 1,
              "properties": { "heading": { "type": "plainText", "value": "Kept" } } }
            """;

        var withSubtitle = $$"""
            { "id": "{{BlockA}}", "blockTypeKey": "rawHtml", "blockTypeRevision": 1,
              "properties": {
                "heading": { "type": "plainText", "value": "Kept" },
                "subtitle": { "type": "plainText", "value": "New" } } }
            """;

        var added = Compare(Zone(bare), Zone(withSubtitle))
            .Single().Blocks.Single().Properties.Should().ContainSingle().Subject;

        added.Key.Should().Be("subtitle");
        added.Kind.Should().Be(ContentChangeKind.Added);
        added.Before.Should().BeNull();
        added.After.Should().Be("New");

        var removed = Compare(Zone(withSubtitle), Zone(bare))
            .Single().Blocks.Single().Properties.Should().ContainSingle().Subject;

        removed.Key.Should().Be("subtitle");
        removed.Kind.Should().Be(ContentChangeKind.Removed);
        removed.After.Should().BeNull();
    }

    [Fact]
    public void AnIdenticalPairReportsNothingEvenWhenItsMembersAreInADifferentOrder()
    {
        var ordered = $$"""
            { "id": "{{BlockA}}", "blockTypeKey": "rawHtml", "blockTypeRevision": 1,
              "properties": {
                "heading": { "type": "plainText", "value": "Same" },
                "subtitle": { "type": "plainText", "value": "Also same" } } }
            """;

        var shuffled = $$"""
            { "blockTypeRevision": 1, "blockTypeKey": "rawHtml", "id": "{{BlockA}}",
              "properties": {
                "subtitle": { "type": "plainText", "value": "Also same" },
                "heading": { "value": "Same", "type": "plainText" } } }
            """;

        // Member order is not meaningful inside a stored value, and a save that happened to
        // re-serialise one must not read to an editor as an edit.
        Compare(Zone(ordered), Zone(shuffled)).Should().BeEmpty();
    }

    [Fact]
    public void AZoneAddedClearedOrRemovedIsDistinguishedFromAValueThatChanged()
    {
        var empty = """{ "schemaVersion": 1, "templateKey": "landing", "templateRevision": 1, "zones": {} }""";
        var filled = Text("hero", "The live text");
        var cleared = """
            { "schemaVersion": 1, "templateKey": "landing", "templateRevision": 1,
              "zones": { "hero": null } }
            """;

        Compare(empty, filled).Single().Kind.Should().Be(ContentChangeKind.Added);
        Compare(filled, empty).Single().Kind.Should().Be(ContentChangeKind.Removed);

        // Cleared is present-and-null, not absent: the editor deliberately emptied the zone, and
        // absent-vs-null is a distinction P1-14 went to some trouble to keep (spec section 6.2).
        Compare(filled, cleared).Single().Kind.Should().Be(ContentChangeKind.Changed);
        Compare(empty, cleared).Single().Kind.Should().Be(ContentChangeKind.Added);
    }

    [Fact]
    public void AZoneOnlyTheEarlierVersionHadIsAppendedAfterTheLaterVersionsOwnOrder()
    {
        var before = """
            { "schemaVersion": 1, "templateKey": "landing", "templateRevision": 1,
              "zones": {
                "hero": { "type": "plainText", "value": "One" },
                "retired": { "type": "plainText", "value": "Gone in the later version" } } }
            """;

        var after = """
            { "schemaVersion": 1, "templateKey": "landing", "templateRevision": 1,
              "zones": {
                "hero": { "type": "plainText", "value": "Two" },
                "added": { "type": "plainText", "value": "New here" } } }
            """;

        var changes = Compare(before, after);

        // The later version's order first — a removed zone has no position in the new document, and
        // appending it is the only honest place to put it.
        changes.Select(change => change.ZoneKey).Should().Equal("hero", "added", "retired");
        changes[2].Kind.Should().Be(ContentChangeKind.Removed);
    }

    [Fact]
    public void AReferenceBearingZoneRendersTheIdentitiesItPointsAtAndIsNotWordDiffed()
    {
        var before = """
            { "schemaVersion": 1, "templateKey": "landing", "templateRevision": 1,
              "zones": { "related": { "type": "pageReference", "value": 12 } } }
            """;

        var after = """
            { "schemaVersion": 1, "templateKey": "landing", "templateRevision": 1,
              "zones": { "related": { "type": "pageReference", "value": 15 } } }
            """;

        var zone = Compare(before, after).Should().ContainSingle().Subject;

        zone.Before.Should().Be("Page 12");
        zone.After.Should().Be("Page 15");

        // "Page 12 → Page 15" is the change. Diffing those two strings word by word would report
        // that the digits moved, which is true and useless.
        zone.Segments.Should().BeEmpty();
    }

    [Fact]
    public void AValueWrittenByAFieldTypeThisBuildDoesNotHaveStillDiffs()
    {
        var before = """
            { "schemaVersion": 1, "templateKey": "landing", "templateRevision": 1,
              "zones": { "hero": { "type": "somethingRemoved", "value": "before" } } }
            """;

        var after = """
            { "schemaVersion": 1, "templateKey": "landing", "templateRevision": 1,
              "zones": { "hero": { "type": "somethingRemoved", "value": "after" } } }
            """;

        var zone = Compare(before, after).Should().ContainSingle().Subject;

        // No field type to ask, so the raw document is rendered. Reporting "no change" for content
        // whose field type was removed would be the worst of the three available answers.
        zone.Kind.Should().Be(ContentChangeKind.Changed);
        zone.Before.Should().Contain("before");
        zone.After.Should().Contain("after");
    }

    [Fact]
    public void AnUnparseablePayloadComparesAsThoughItHeldNoZones()
    {
        var compare = () => Diff.Compare(null, ContentEngineHarness.Payload(Text("hero", "Still here")));

        // A version a later build cannot read is exactly when somebody opens the diff, so this
        // reports what it can rather than throwing.
        compare.Should().NotThrow();
        compare().Should().ContainSingle().Which.Kind.Should().Be(ContentChangeKind.Added);

        Diff.Compare(null, null).Should().BeEmpty();
    }

    [Fact]
    public void TwoBlocksSharingAnIdAreReadAsOneRatherThanThrowing()
    {
        var duplicated = Blocks(BlockA, BlockA);

        // A duplicate id is a malformed payload the blocks field type already reports on. The diff's
        // job here is to still render, because the editor is looking at it to work out what broke.
        var compare = () => Compare(Blocks(BlockA), duplicated);

        compare.Should().NotThrow();

        var zone = compare().Should().ContainSingle().Subject;

        // The zone reads as changed because the stored arrays differ, and the block list is empty
        // because the second occurrence was dropped and the first is identical. Reporting the zone
        // and no block is the honest answer to a document the block model cannot represent.
        zone.Kind.Should().Be(ContentChangeKind.Changed);
        zone.Blocks.Should().BeEmpty();
    }

    /// <summary>Compares two payload documents written as JSON.</summary>
    private static IReadOnlyList<ZoneChange> Compare(string before, string after) =>
        Diff.Compare(ContentEngineHarness.Payload(before), ContentEngineHarness.Payload(after));

    /// <summary>A payload holding one plain-text zone.</summary>
    private static string Text(string zoneKey, string value) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "landing", "templateRevision": 1,
          "zones": { "{{zoneKey}}": { "type": "plainText", "value": "{{value}}" } } }
        """;

    /// <summary>A payload whose one zone holds the given block instances, in order.</summary>
    private static string Blocks(params string[] blockIds) =>
        Zone(string.Join(",\n", blockIds.Select(id =>
            $$"""
            { "id": "{{id}}", "blockTypeKey": "rawHtml", "blockTypeRevision": 1,
              "properties": { "heading": { "type": "plainText", "value": "text {{id}}" } } }
            """)));

    /// <summary>Wraps block instance JSON in a payload with one <c>blocks</c> zone.</summary>
    private static string Zone(string items) =>
        $$"""
        { "schemaVersion": 1, "templateKey": "landing", "templateRevision": 1,
          "zones": { "body": { "type": "blocks", "items": [ {{items}} ] } } }
        """;
}
