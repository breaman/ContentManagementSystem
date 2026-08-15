using System.Diagnostics.Metrics;

using ContentManagementSystem.Core;
using ContentManagementSystem.Core.Content;
using ContentManagementSystem.Core.Publishing;
using ContentManagementSystem.Core.Telemetry;
using ContentManagementSystem.Data.Models.Cms;
using ContentManagementSystem.Server.Tests.Content;
using ContentManagementSystem.Shared.Contracts.Content;
using ContentManagementSystem.Shared.Contracts.Fields;
using ContentManagementSystem.TestSupport;

namespace ContentManagementSystem.Server.Tests.Delivery;

/// <summary>
/// The two delivery metrics of spec section 24.1 (task P3-28).
/// </summary>
/// <remarks>
/// Asserted through real requests rather than by calling <c>CmsMetrics</c> directly, for the reason
/// the publish telemetry suite gives: what goes wrong is not the instrument, it is the instrument
/// never being reached — an early return that skips the recording, a name that drifted from the one
/// the dashboard queries. Recording a measurement by hand passes every one of those.
/// </remarks>
[Collection(SqlServerCollectionNames.SqlServer)]
public class DeliveryTelemetryTests(SqlServerFixture fixture) : IAsyncLifetime
{
    private PageWorkbench _bench = null!;

    public async ValueTask InitializeAsync() =>
        _bench = await PageWorkbench.CreateAsync(fixture, cancellationToken: TestContext.Current.CancellationToken);

    public async ValueTask DisposeAsync() => await _bench.DisposeAsync();

    [Fact]
    public async Task RenderingAPageRecordsItsDurationTaggedByTemplate()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await PublishedPageAsync(cancellationToken);

        using var recorder = new MeasurementRecorder(_bench.Resolve<IMeterFactory>());
        using var client = _bench.CreateClient();

        (await client.GetAsync("/pricing", cancellationToken)).EnsureSuccessStatusCode();

        // Tagged by template because that is the dimension a regression has: pages are not slow,
        // templates are, and an untagged histogram averages the one expensive layout away.
        recorder.Durations(CmsMetrics.PageRenderDurationName, "article")
            .Should().ContainSingle()
            .Which.Should().BeGreaterThanOrEqualTo(0);
    }

    [Fact]
    public async Task AUrlThatResolvesToNothingCountsAsARouteMiss()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        using var recorder = new MeasurementRecorder(_bench.Resolve<IMeterFactory>());
        using var client = _bench.CreateClient();

        foreach (var _ in Enumerable.Range(0, 2))
        {
            using var response = await client.GetAsync("/legacy/leaflet", cancellationToken);
        }

        // The counter answers "has the 404 rate changed", and NotFoundLog answers "which URLs" —
        // deliberately two different things, because a tag carrying the requested URL would be an
        // unbounded cardinality hole with an open door in front of it.
        recorder.Counts(CmsMetrics.RouteResolutionMissName).Should().Be(2);
    }

    private async Task PublishedPageAsync(CancellationToken cancellationToken)
    {
        var template = await _bench.UseTemplateAsync(
            "article",
            cancellationToken,
            new Zone { Key = "kicker", Name = "Kicker", FieldTypeKey = FieldTypeKeys.PlainText });

        var page = await _bench.AddPageAsync(template, "Pricing", cancellationToken);

        (await _bench.Resolve<IDraftService>().SaveAsync(
            page.Summary.Id,
            new SaveDraftRequest(
                $$"""
                { "schemaVersion": 1, "templateKey": "{{template.Key}}", "templateRevision": 1,
                  "zones": { "kicker": { "type": "plainText", "value": "Our best plans yet" } } }
                """,
                null),
            cancellationToken)).IsSuccess.Should().BeTrue();

        var published = await _bench.Resolve<IPublishingService>()
            .PublishAsync(page.Summary.Id, true, cancellationToken);

        published.IsSuccess.Should().BeTrue(string.Join("; ", published.Diagnostics.Diagnostics
            .Select(diagnostic => $"{diagnostic.Code}: {diagnostic.Message}")));

        _bench.Context.ChangeTracker.Clear();
    }

    /// <summary>
    /// Collects the measurements one host published on the CMS meter.
    /// </summary>
    /// <remarks>
    /// Scoped to the meter factory that made the instrument, so a host started by another suite in
    /// the same process cannot contribute to a count asserted here.
    /// </remarks>
    private sealed class MeasurementRecorder : IDisposable
    {
        private readonly List<(string Instrument, double Value, string? Template)> _measurements = [];
        private readonly MeterListener _listener;

        public MeasurementRecorder(IMeterFactory factory)
        {
            _listener = new MeterListener
            {
                InstrumentPublished = (instrument, listener) =>
                {
                    if (instrument.Meter.Name != CmsTelemetry.MeterName) return;
                    if (!ReferenceEquals(instrument.Meter.Scope, factory)) return;

                    listener.EnableMeasurementEvents(instrument);
                },
            };

            _listener.SetMeasurementEventCallback<long>(
                (instrument, measurement, tags, _) => Record(instrument.Name, measurement, tags));
            _listener.SetMeasurementEventCallback<double>(
                (instrument, measurement, tags, _) => Record(instrument.Name, measurement, tags));

            _listener.Start();
        }

        public long Counts(string instrument)
        {
            lock (_measurements)
            {
                return _measurements.Where(entry => entry.Instrument == instrument)
                    .Sum(entry => (long)entry.Value);
            }
        }

        public IReadOnlyList<double> Durations(string instrument, string templateKey)
        {
            lock (_measurements)
            {
                return [.. _measurements
                    .Where(entry => entry.Instrument == instrument && entry.Template == templateKey)
                    .Select(entry => entry.Value)];
            }
        }

        public void Dispose() => _listener.Dispose();

        private void Record(string instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
        {
            string? template = null;

            foreach (var tag in tags)
            {
                if (tag.Key == CmsMetrics.TemplateTag) template = tag.Value?.ToString();
            }

            lock (_measurements)
            {
                _measurements.Add((instrument, value, template));
            }
        }
    }
}
