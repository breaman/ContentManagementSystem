using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using S1.RuntimeSchema;

// ---------------------------------------------------------------------------------------------
// S1 — Runtime-schema payload round trip.
//
// Question: can a JSON payload be validated and deserialized against a *runtime-defined* schema
// (zones and block-type properties as data, not CLR types) with acceptable performance and clear
// errors?  Throwaway code — see docs/spikes/s1-runtime-schema.md.
// ---------------------------------------------------------------------------------------------

var dataDirectory = Path.Combine(AppContext.BaseDirectory, "data");
var catalog = SchemaLoader.Load(Path.Combine(dataDirectory, "schema.json"));

var validator = new PayloadValidator(catalog);
validator.Bind(new FieldTypeRegistry(
[
    new PlainTextFieldType(),
    new RichTextFieldType(),
    new NumberFieldType(),
    new BooleanFieldType(),
    new ChoiceFieldType(),
    new MediaFieldType(),
    new LinkFieldType(),
    new ReusableFieldType(),
    new BlocksFieldType(validator),
]));

var validJson = File.ReadAllText(Path.Combine(dataDirectory, "payload-valid.json"));
var invalidJson = File.ReadAllText(Path.Combine(dataDirectory, "payload-invalid.json"));

Console.WriteLine("S1 — runtime-schema payload round trip");
Console.WriteLine($"{DateTime.Now:yyyy-MM-dd HH:mm}  ·  .NET {Environment.Version}  ·  {Environment.OSVersion}");
Console.WriteLine($"Schema loaded from data/schema.json — no CLR type describes any zone or property.");

// ---------------------------------------------------------------------------------------------
Check.Section("1. Round trip preserves the document exactly");

using (var original = JsonDocument.Parse(validJson))
{
    var roundTripped = JsonNode.Parse(validJson)!.ToJsonString();

    using var reparsed = JsonDocument.Parse(roundTripped);

    Check.That(
        Canonical(original.RootElement) == Canonical(reparsed.RootElement),
        "parse → serialize is byte-identical after canonicalization");

    var zones = reparsed.RootElement.GetProperty("zones");

    Check.That(
        zones.TryGetProperty("legacyBanner", out _),
        "an orphaned zone (no longer in the template revision) survives the round trip",
        "spec §8.5 — removing a zone must not destroy content");

    Check.That(
        zones.GetProperty("ctaLabel").GetProperty("value").ValueKind == JsonValueKind.Null,
        "an explicitly cleared value stays null");

    Check.That(
        !zones.TryGetProperty("theme", out _),
        "a never-authored zone stays absent — absent and null remain distinguishable",
        "spec §6.2");
}

// ---------------------------------------------------------------------------------------------
Check.Section("2. A valid payload validates clean");

using (var document = JsonDocument.Parse(validJson))
{
    var draft = validator.Validate(document.RootElement, ValidationMode.Draft);
    var publish = validator.Validate(document.RootElement, ValidationMode.Publish);

    Check.That(!draft.HasErrors, "draft save: no errors", Describe(draft));
    Check.That(!publish.HasErrors, "publish: no errors", Describe(publish));

    var orphanWarnings = publish.Diagnostics
        .Where(d => d.Code == "zone.orphaned")
        .ToList();

    Check.That(
        orphanWarnings is [{ Path: "zones.legacyBanner" }],
        "the orphaned zone is reported as exactly one warning, on its own path",
        orphanWarnings.Count == 0 ? "(none reported)" : string.Join("; ", orphanWarnings.Select(d => d.Path)));
}

// ---------------------------------------------------------------------------------------------
Check.Section("3. Errors identify the exact zone, block, and property");

using (var document = JsonDocument.Parse(invalidJson))
{
    var result = validator.Validate(document.RootElement, ValidationMode.Publish);

    foreach (var diagnostic in result.Diagnostics)
    {
        var marker = diagnostic.Severity == Severity.Error ? "ERROR" : "warn ";
        Console.WriteLine($"  {marker} {diagnostic.Path}");
        Console.WriteLine($"        [{diagnostic.Code}] {diagnostic.Message}");
    }

    Console.WriteLine();

    Expect(result, "zones.hero[0].properties.headline", "field.maxLength");
    Expect(result, "zones.hero[0].properties.image", "property.required");
    Expect(result, "zones.hero[0].properties.cta", "field.link.pageId");
    Expect(result, "zones.hero[0].properties.subtitle", "property.orphaned", Severity.Warning);
    Expect(result, "zones.hero[1]", "block.id.duplicate");
    Expect(result, "zones.hero[1].properties.quote", "property.type.mismatch");
    Expect(result, "zones.hero[2]", "block.id.missing");
    Expect(result, "zones.hero[2]", "block.type.notAllowed");
    Expect(result, "zones.hero[3]", "blockType.revision.unknown");
    Expect(result, "zones.body", "field.richText.format.notAllowed");
    Expect(result, "zones.sidebar[0].properties.columnCount", "field.max");
    Expect(result, "zones.sidebar[0].properties.quote", "property.required", expectAbsent: true);
    Expect(result, "zones.ctaLabel", "field.type.mismatch");
    Expect(result, "zones.theme", "field.choice.unknownOption");
    Expect(result, "zones.readMore", "field.link.scheme");
    Expect(result, "zones.footer", "zone.type.mismatch");

    Check.That(
        result.Diagnostics.All(d => d.Path.StartsWith("zones.", StringComparison.Ordinal)),
        "every diagnostic carries a payload path an editor can jump to");
}

// ---------------------------------------------------------------------------------------------
Check.Section("4. Template evolution — the same payload against a newer revision");

var evolvedJson = validJson.Replace("\"templateRevision\": 7", "\"templateRevision\": 8", StringComparison.Ordinal);

using (var document = JsonDocument.Parse(evolvedJson))
{
    var draft = validator.Validate(document.RootElement, ValidationMode.Draft);
    var publish = validator.Validate(document.RootElement, ValidationMode.Publish);

    Check.That(
        draft.Diagnostics.Any(d => d.Code == "zone.orphaned" && d.Path == "zones.sidebar"),
        "the zone removed in revision 8 becomes orphaned content, not a hard failure");

    Check.That(
        !draft.HasErrors,
        "the draft still saves against the newer revision",
        Describe(draft));

    Check.That(
        publish.Diagnostics.Any(d => d.Code == "zone.required" && d.Path == "zones.announcement"),
        "the zone added as required in revision 8 fails only on the next publish, naming the zone");
}

// ---------------------------------------------------------------------------------------------
Check.Section("5. Reference extraction is complete");

using (var document = JsonDocument.Parse(validJson))
{
    var references = validator.ExtractReferences(document.RootElement);

    foreach (var reference in references.OrderBy(r => r.TargetType).ThenBy(r => r.TargetId))
    {
        Console.WriteLine($"        {reference.TargetType,-16} {reference.TargetId,-5} {reference.Path}");
    }

    Console.WriteLine();

    Check.That(Has(references, "Media", 812), "media referenced from a top-level block property is found");
    Check.That(Has(references, "Media", 913), "media referenced from a second block is found");
    Check.That(Has(references, "Page", 44), "an internal link inside a block is found");
    Check.That(Has(references, "Page", 91), "an internal link in a zone-level link field is found");
    Check.That(Has(references, "ReusableContent", 3), "reusable content referenced by a zone is found");
    Check.That(
        references.Any(r => r.TargetType == "Media" && r.TargetId == 977 &&
                            r.Path.Contains("children", StringComparison.Ordinal)),
        "a reference two block levels down (sidebar → text-columns → children → quote) is found",
        "the nesting depth a field type does not know about is exactly where references get dropped");

    // The P1-13 contract test in miniature: a reference-bearing field type that returns nothing for a
    // populated value is the bug that produces stale published pages.
    var referenceBearing = new (string Key, IFieldType Type, string Json)[]
    {
        ("media", new MediaFieldType(), """{ "type": "media", "mediaId": 1 }"""),
        ("link", new LinkFieldType(), """{ "type": "link", "kind": "page", "pageId": 2 }"""),
        ("reusable", new ReusableFieldType(), """{ "type": "reusable", "reusableContentId": 3 }"""),
    };

    foreach (var (key, type, json) in referenceBearing)
    {
        using var sample = JsonDocument.Parse(json);
        var found = new List<ContentReference>();
        type.ExtractReferences(sample.RootElement, new ValidationContext(catalog, ValidationMode.Draft), found);
        Check.That(found.Count > 0, $"contract: '{key}' reports a reference for a representative populated value");
    }
}

// ---------------------------------------------------------------------------------------------
Check.Section("6. Editing round trip — mutate through a runtime model and re-validate");

{
    var model = JsonNode.Parse(validJson)!;
    var items = model["zones"]!["hero"]!["items"]!.AsArray();
    var originalIds = items.Select(i => i!["id"]!.GetValue<string>()).ToList();

    var moved = items[0]!.DeepClone();
    items.RemoveAt(0);
    items.Add(moved);

    items[0]!["properties"]!["quote"]!["value"] = "It cut our publish cycle in half — twice.";

    var mutatedJson = model.ToJsonString();
    using var mutated = JsonDocument.Parse(mutatedJson);
    var result = validator.Validate(mutated.RootElement, ValidationMode.Publish);

    var mutatedIds = mutated.RootElement.GetProperty("zones").GetProperty("hero").GetProperty("items")
        .EnumerateArray().Select(i => i.GetProperty("id").GetString()!).ToList();

    Check.That(!result.HasErrors, "the mutated payload still validates for publish", Describe(result));
    Check.That(
        mutatedIds.Order().SequenceEqual(originalIds.Order()) && !mutatedIds.SequenceEqual(originalIds),
        "block GUIDs survive a reorder — a diff can report 'moved' rather than 'removed + added'",
        $"before: [{string.Join(", ", originalIds.Select(Short))}]  after: [{string.Join(", ", mutatedIds.Select(Short))}]");
}

// ---------------------------------------------------------------------------------------------
Check.Section("7. Performance");

Console.WriteLine("        blocks   bytes    parse+validate p50 / p95    +references p50    alloc/op");

var perfBudgetMet = true;
int[] blockCounts = [1, 10, 50, 200];
var payloads = blockCounts.ToDictionary(n => n, GeneratePayload);

// Warm every size before measuring any of them, so the first size measured is not paying for
// tiered JIT on behalf of the rest.
foreach (var warmPayload in payloads.Values)
{
    _ = Measure(warmPayload, withReferences: true);
}

foreach (var blockCount in blockCounts)
{
    var payload = payloads[blockCount];
    var bytes = Encoding.UTF8.GetByteCount(payload);

    var validate = Measure(payload, withReferences: false);
    var withReferences = Measure(payload, withReferences: true);

    Console.WriteLine(
        $"        {blockCount,6}   {bytes,6}   {Micro(validate.P50),12} / {Micro(validate.P95),-10}  " +
        $"{Micro(withReferences.P50),12}     {validate.BytesPerOp,7:N0} B");

    // A page render must stay well inside the NFR budget; validation runs on save and publish, not
    // on every render, so 1 ms for a large page is comfortable.
    if (blockCount <= 50 && validate.P95 > 1_000)
    {
        perfBudgetMet = false;
    }
}

Check.That(perfBudgetMet, "parse + validate of a realistic page (≤50 blocks) stays under 1 ms at p95");

// ---------------------------------------------------------------------------------------------
return Check.Summarize();

// ---------------------------------------------------------------------------------------------

void Expect(ValidationContext result, string path, string code, Severity severity = Severity.Error, bool expectAbsent = false)
{
    var found = result.Diagnostics.Any(d => d.Path == path && d.Code == code && d.Severity == severity);

    Check.That(
        found != expectAbsent,
        expectAbsent
            ? $"no '{code}' is raised at {path}"
            : $"'{code}' is raised at {path}");
}

static bool Has(List<ContentReference> references, string type, long id) =>
    references.Any(r => r.TargetType == type && r.TargetId == id);

static string Short(string id) => id[..8];

static string Describe(ValidationContext result)
{
    var errors = result.Diagnostics.Where(d => d.Severity == Severity.Error).ToList();
    return errors.Count == 0
        ? "no errors"
        : string.Join(" | ", errors.Select(e => $"{e.Path}: {e.Code}"));
}

static string Canonical(JsonElement element)
{
    var buffer = new MemoryStream();
    using (var writer = new Utf8JsonWriter(buffer))
    {
        WriteCanonical(element, writer);
    }

    return Encoding.UTF8.GetString(buffer.ToArray());

    static void WriteCanonical(JsonElement element, Utf8JsonWriter writer)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                foreach (var property in element.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    writer.WritePropertyName(property.Name);
                    WriteCanonical(property.Value, writer);
                }

                writer.WriteEndObject();
                break;

            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in element.EnumerateArray())
                {
                    WriteCanonical(item, writer);
                }

                writer.WriteEndArray();
                break;

            default:
                element.WriteTo(writer);
                break;
        }
    }
}

static string GeneratePayload(int blockCount)
{
    var builder = new StringBuilder();
    builder.Append("""{"schemaVersion":1,"templateKey":"perf-harness","templateRevision":1,"zones":{"content":{"type":"blocks","items":[""");

    for (var i = 0; i < blockCount; i++)
    {
        if (i > 0)
        {
            builder.Append(',');
        }

        var id = new Guid(i + 1, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0);

        builder.Append("{\"id\":\"").Append(id).Append("\",");

        if (i % 2 == 0)
        {
            builder.Append("\"blockTypeKey\":\"hero-banner\",\"blockTypeRevision\":3,\"properties\":{")
                .Append("\"headline\":{\"type\":\"plainText\",\"value\":\"Headline number ").Append(i).Append("\"},")
                .Append("\"body\":{\"type\":\"richText\",\"format\":\"markdown\",\"value\":\"Paragraph **").Append(i)
                .Append("** with a little inline emphasis and a reasonable amount of prose.\"},")
                .Append("\"image\":{\"type\":\"media\",\"mediaId\":").Append(800 + i)
                .Append(",\"focalPoint\":{\"x\":0.5,\"y\":0.4}},")
                .Append("\"cta\":{\"type\":\"link\",\"kind\":\"page\",\"pageId\":").Append(40 + i)
                .Append(",\"text\":\"Learn more\",\"target\":\"_self\"}}}");
        }
        else
        {
            builder.Append("\"blockTypeKey\":\"quote\",\"blockTypeRevision\":1,\"properties\":{")
                .Append("\"quote\":{\"type\":\"plainText\",\"value\":\"A representative pull quote of the sort editors ")
                .Append("actually write, number ").Append(i).Append(".\"},")
                .Append("\"attribution\":{\"type\":\"plainText\",\"value\":\"Someone, Somewhere\"},")
                .Append("\"portrait\":{\"type\":\"media\",\"mediaId\":").Append(900 + i).Append("}}}");
        }
    }

    builder.Append("]}}}");
    return builder.ToString();
}

Sample Measure(string payload, bool withReferences)
{
    const int Warmup = 200;
    const int Iterations = 2_000;

    for (var i = 0; i < Warmup; i++)
    {
        RunOnce(payload, withReferences);
    }

    var samples = new double[Iterations];
    var before = GC.GetAllocatedBytesForCurrentThread();

    for (var i = 0; i < Iterations; i++)
    {
        var start = Stopwatch.GetTimestamp();
        RunOnce(payload, withReferences);
        samples[i] = Stopwatch.GetElapsedTime(start).TotalMicroseconds;
    }

    var allocated = GC.GetAllocatedBytesForCurrentThread() - before;
    Array.Sort(samples);

    return new Sample(
        samples[(int)(Iterations * 0.50)],
        samples[(int)(Iterations * 0.95)],
        allocated / Iterations);

    void RunOnce(string json, bool references)
    {
        using var document = JsonDocument.Parse(json);
        var result = validator.Validate(document.RootElement, ValidationMode.Publish);

        if (result.HasErrors)
        {
            throw new InvalidOperationException($"Generated perf payload did not validate: {Describe(result)}");
        }

        if (references)
        {
            _ = validator.ExtractReferences(document.RootElement);
        }
    }
}

static string Micro(double microseconds) =>
    microseconds.ToString("N1", CultureInfo.InvariantCulture) + " µs";

internal readonly record struct Sample(double P50, double P95, long BytesPerOp);
