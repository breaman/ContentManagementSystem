using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Structure;
using ContentManagementSystem.Server.Services;
using ContentManagementSystem.Shared.Contracts.Structure;

using NSubstitute;

namespace ContentManagementSystem.Server.Tests.Services;

/// <summary>
/// The gate that keeps a pre-rendering screen's components from using one request's
/// <c>ApplicationDbContext</c> at the same time.
/// </summary>
/// <remarks>
/// Driven against substituted services rather than a database: what is under test is whether two
/// calls can be inside the shim at once, and a fake that counts its own overlaps says that far more
/// precisely than watching EF Core throw. The bug these guard against was a page editor with two
/// block-list zones, whose editors both queried in the same render batch.
/// </remarks>
public class PrerenderGateTests
{
    [Fact]
    public async Task OverlappingOperationsRunOneAtATime()
    {
        var gate = new PrerenderGate();
        var monitor = new OverlapMonitor();

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            gate.RunAsync(_ => monitor.RunAsync(), TestContext.Current.CancellationToken)));

        monitor.Overlaps.Should().Be(0);
        monitor.Calls.Should().Be(8);
    }

    [Fact]
    public async Task AFailedOperationLeavesTheGateOpen()
    {
        var gate = new PrerenderGate();

        var failing = async () => await gate.RunAsync<int>(
            _ => Task.FromException<int>(new InvalidOperationException("boom")),
            TestContext.Current.CancellationToken);

        await failing.Should().ThrowAsync<InvalidOperationException>();

        var after = await gate.RunAsync(_ => Task.FromResult(7), TestContext.Current.CancellationToken);

        after.Should().Be(7);
    }

    [Fact]
    public async Task ComponentsInitializingTogetherDoNotOverlapInsideTheStructureShim()
    {
        var monitor = new OverlapMonitor();
        var blockTypes = Substitute.For<IBlockTypeService>();

        blockTypes.ListAsync(Arg.Any<CancellationToken>()).Returns(async _ =>
        {
            await monitor.RunAsync();

            return CmsResult<IReadOnlyList<BlockTypeSummary>>.Success([]);
        });

        var client = new ServerStructureClient(
            Substitute.For<ITemplateService>(),
            Substitute.For<IZoneService>(),
            blockTypes,
            Substitute.For<IFieldTypeCatalog>(),
            new PrerenderGate());

        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
            client.GetBlockTypesAsync(TestContext.Current.CancellationToken)));

        monitor.Overlaps.Should().Be(0);
        monitor.Calls.Should().Be(8);
    }

    /// <summary>
    /// Stands in for the shared <c>DbContext</c>: it notices when a second caller arrives before the
    /// first has left, which is precisely what EF Core's concurrency detector refuses.
    /// </summary>
    private sealed class OverlapMonitor
    {
        private int _running;
        private int _overlaps;
        private int _calls;

        /// <summary>How many times a call started while another was still inside.</summary>
        public int Overlaps => Volatile.Read(ref _overlaps);

        /// <summary>How many calls were made, so a gate that swallowed one would be caught.</summary>
        public int Calls => Volatile.Read(ref _calls);

        /// <returns>Which call this was, so the gate has something to hand back.</returns>
        public async Task<int> RunAsync()
        {
            var call = Interlocked.Increment(ref _calls);

            if (Interlocked.Increment(ref _running) > 1) Interlocked.Increment(ref _overlaps);

            // Long enough that an ungated caller would reliably arrive during the wait, which is what
            // makes this test fail without the gate rather than only failing sometimes.
            await Task.Delay(25);

            Interlocked.Decrement(ref _running);

            return call;
        }
    }
}
