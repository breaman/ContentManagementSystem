using System.Buffers;
using System.Globalization;
using System.Text;
using System.Text.Json;

namespace ContentManagementSystem.Core.LoadTesting;

/// <summary>
/// The prose and the payloads the seeded pages are filled with.
/// </summary>
/// <remarks>
/// Written through <see cref="Utf8JsonWriter"/> rather than interpolated into a template string:
/// the text is generated, so it contains apostrophes and quotation marks, and a payload that is not
/// valid JSON reaches the delivery path as a page that logs an error and serves a 404 — fifty
/// thousand times, discovered halfway through a load test.
/// <para>
/// Everything here is deterministic in the <see cref="Random"/> it is handed, so the same seed
/// produces the same site. Two load-test runs that differ only in their content are two runs that
/// cannot be compared.
/// </para>
/// </remarks>
internal static class LoadTestContent
{
    /// <summary>Zone and property keys the seeded templates declare.</summary>
    public const string ArticleTemplateKey = "article";

    /// <summary>Key of the block-driven landing template.</summary>
    public const string LandingTemplateKey = "marketing-landing";

    private static readonly string[] Adjectives =
    [
        "quarterly", "regional", "modular", "inbound", "seasonal", "distributed", "annual",
        "provisional", "combined", "revised", "northern", "compact", "extended", "shared",
    ];

    private static readonly string[] Nouns =
    [
        "sprocket", "gearbox", "logistics", "handbook", "roadmap", "bulletin", "programme",
        "workshop", "inventory", "briefing", "framework", "directory", "schedule", "review",
    ];

    private static readonly string[] Verbs =
    [
        "explains", "records", "compares", "introduces", "summarises", "revisits", "measures",
    ];

    private static readonly string[] Connectives =
    [
        "which means that", "although", "because", "so that", "even where", "given that",
    ];

    /// <summary>A short title, of the length an editor would actually type.</summary>
    /// <param name="random">The generator.</param>
    /// <returns>Something like "Regional gearbox review".</returns>
    public static string Title(Random random)
    {
        var title = $"{Pick(random, Adjectives)} {Pick(random, Nouns)} {Pick(random, Nouns)}";

        return char.ToUpperInvariant(title[0]) + title[1..];
    }

    /// <summary>One sentence of filler.</summary>
    /// <param name="random">The generator.</param>
    /// <returns>A sentence ending in a full stop.</returns>
    public static string Sentence(Random random)
    {
        var sentence =
            $"The {Pick(random, Adjectives)} {Pick(random, Nouns)} {Pick(random, Verbs)} " +
            $"the {Pick(random, Nouns)}, {Pick(random, Connectives)} the {Pick(random, Adjectives)} " +
            $"{Pick(random, Nouns)} is {Pick(random, Verbs).TrimEnd('s')}d each {Pick(random, Nouns)}.";

        return char.ToUpperInvariant(sentence[0]) + sentence[1..];
    }

    /// <summary>Several sentences, as one paragraph of plain text.</summary>
    /// <param name="random">The generator.</param>
    /// <param name="sentences">How many sentences to write.</param>
    /// <returns>The paragraph.</returns>
    public static string Prose(Random random, int sentences)
    {
        var builder = new StringBuilder();

        for (var index = 0; index < sentences; index++)
        {
            if (index > 0) builder.Append(' ');

            builder.Append(Sentence(random));
        }

        return builder.ToString();
    }

    /// <summary>
    /// The payload of an article page: metadata zones, a picture, a gallery, and a block body.
    /// </summary>
    /// <param name="random">The generator.</param>
    /// <param name="revision">Revision of the template the payload is authored against.</param>
    /// <param name="posterMediaId">Media shown at the top.</param>
    /// <param name="galleryMediaIds">Media shown in the gallery.</param>
    /// <param name="relatedPageId">A page this one points at.</param>
    /// <param name="tags">Tag slugs to carry in the tags zone.</param>
    /// <param name="publishedOn">The instant the dateTime zone reports.</param>
    /// <returns>The content JSON.</returns>
    public static string ArticlePayload(
        Random random,
        int revision,
        int posterMediaId,
        IReadOnlyList<int> galleryMediaIds,
        int relatedPageId,
        IReadOnlyList<string> tags,
        DateTimeOffset publishedOn) =>
        Write(writer =>
        {
            Envelope(writer, ArticleTemplateKey, revision);

            writer.WritePropertyName("kicker");
            Scalar(writer, "plainText", value => value.WriteString("value", Pick(random, Nouns)));

            writer.WritePropertyName("standfirst");
            Scalar(writer, "multilineText", value => value.WriteString("value", Prose(random, 2)));

            writer.WritePropertyName("publishedAt");
            Scalar(writer, "dateTime", value => value.WriteString(
                "value",
                publishedOn.ToString("yyyy-MM-ddTHH:mm:sszzz", CultureInfo.InvariantCulture)));

            writer.WritePropertyName("readingMinutes");
            Scalar(writer, "number", value => value.WriteNumber("value", random.Next(2, 14)));

            writer.WritePropertyName("isFeatured");
            Scalar(writer, "boolean", value => value.WriteBoolean("value", random.Next(10) == 0));

            writer.WritePropertyName("poster");
            Media(writer, posterMediaId);

            writer.WritePropertyName("gallery");
            writer.WriteStartObject();
            writer.WriteString("type", "mediaList");
            writer.WriteStartArray("items");

            foreach (var mediaId in galleryMediaIds)
            {
                writer.WriteStartObject();
                writer.WriteNumber("mediaId", mediaId);
                writer.WriteEndObject();
            }

            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WritePropertyName("tags");
            writer.WriteStartObject();
            writer.WriteString("type", "tags");
            writer.WriteStartArray("value");

            foreach (var tag in tags)
            {
                writer.WriteStringValue(tag);
            }

            writer.WriteEndArray();
            writer.WriteEndObject();

            writer.WritePropertyName("related");
            Scalar(writer, "pageReference", value => value.WriteNumber("value", relatedPageId));

            writer.WritePropertyName("body");
            Blocks(writer, random, random.Next(3, 7));

            writer.WriteEndObject();
            writer.WriteEndObject();
        });

    /// <summary>
    /// The payload of a landing page: a picture, an introduction, blocks, a link, and the footer
    /// every landing page shares.
    /// </summary>
    /// <param name="random">The generator.</param>
    /// <param name="revision">Revision of the template the payload is authored against.</param>
    /// <param name="heroMediaId">Media shown at the top.</param>
    /// <param name="ctaPageId">Page the call to action points at.</param>
    /// <param name="footerReusableContentId">The shared footer, referenced late-bound.</param>
    /// <returns>The content JSON.</returns>
    public static string LandingPayload(
        Random random,
        int revision,
        int heroMediaId,
        int ctaPageId,
        int footerReusableContentId) =>
        Write(writer =>
        {
            Envelope(writer, LandingTemplateKey, revision);

            writer.WritePropertyName("hero");
            Media(writer, heroMediaId);

            writer.WritePropertyName("intro");
            writer.WriteStartObject();
            writer.WriteString("type", "richText");
            writer.WriteString("format", "html");
            writer.WriteString("value", $"<p>{Prose(random, 3)}</p>");
            writer.WriteEndObject();

            writer.WritePropertyName("body");
            Blocks(writer, random, random.Next(2, 5));

            writer.WritePropertyName("cta");
            writer.WriteStartObject();
            writer.WriteString("type", "link");
            writer.WriteString("kind", "page");
            writer.WriteNumber("pageId", ctaPageId);
            writer.WriteString("text", "Read the briefing");
            writer.WriteString("target", "_self");
            writer.WriteNull("rel");
            writer.WriteEndObject();

            // Late-bound rather than pinned, which is the case that matters under load: publishing
            // the footer has to invalidate every page holding one (spec section 9.3).
            writer.WritePropertyName("footer");
            writer.WriteStartObject();
            writer.WriteString("type", "reusable");
            writer.WriteNumber("reusableContentId", footerReusableContentId);
            writer.WriteNull("pinnedVersionId");
            writer.WriteEndObject();

            writer.WriteEndObject();
            writer.WriteEndObject();
        });

    /// <summary>The shared footer's own payload, authored against the built-in HTML block type.</summary>
    /// <param name="blockTypeKey">Key of the block type the reusable item is built from.</param>
    /// <param name="propertyKey">Key of its single property.</param>
    /// <param name="revision">Revision of that block type.</param>
    /// <returns>The content JSON.</returns>
    public static string FooterPayload(string blockTypeKey, string propertyKey, int revision) =>
        Write(writer =>
        {
            Envelope(writer, blockTypeKey, revision);

            writer.WritePropertyName(propertyKey);
            Scalar(writer, "html", value => value.WriteString(
                "value",
                "<p>Seeded load-test footer. Published once and referenced by every landing page.</p>"));

            writer.WriteEndObject();
            writer.WriteEndObject();
        });

    /// <summary>
    /// The text the search index would have extracted, for the search rows the seeder writes itself.
    /// </summary>
    /// <param name="random">The generator.</param>
    /// <returns>A body of a few hundred characters.</returns>
    public static string SearchBody(Random random) => Prose(random, 4);

    private static void Envelope(Utf8JsonWriter writer, string templateKey, int revision)
    {
        writer.WriteStartObject();
        writer.WriteNumber("schemaVersion", 1);
        writer.WriteString("templateKey", templateKey);
        writer.WriteNumber("templateRevision", revision);
        writer.WriteStartObject("zones");
    }

    private static void Scalar(Utf8JsonWriter writer, string type, Action<Utf8JsonWriter> value)
    {
        writer.WriteStartObject();
        writer.WriteString("type", type);
        value(writer);
        writer.WriteEndObject();
    }

    private static void Media(Utf8JsonWriter writer, int mediaId)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "media");
        writer.WriteNumber("mediaId", mediaId);
        writer.WriteNull("altOverride");
        writer.WriteNull("crop");
        writer.WriteEndObject();
    }

    /// <summary>
    /// A blocks zone of HTML blocks, built on the block type every database has.
    /// </summary>
    /// <remarks>
    /// <c>rawHtml</c> is seeded by the migration that creates the tables, so the seeder needs no
    /// block types of its own and the payload renders through a component that exists rather than
    /// through the unknown-type fallback.
    /// </remarks>
    private static void Blocks(Utf8JsonWriter writer, Random random, int count)
    {
        writer.WriteStartObject();
        writer.WriteString("type", "blocks");
        writer.WriteStartArray("items");

        for (var index = 0; index < count; index++)
        {
            writer.WriteStartObject();
            writer.WriteString("id", NewId(random).ToString());
            writer.WriteString("blockTypeKey", "rawHtml");
            writer.WriteNumber("blockTypeRevision", 1);
            writer.WriteStartObject("properties");
            writer.WritePropertyName("content");
            Scalar(writer, "html", value => value.WriteString(
                "value",
                $"<h2>{Title(random)}</h2><p>{Prose(random, 3)}</p>" +
                $"<ul><li>{Sentence(random)}</li><li>{Sentence(random)}</li></ul>"));
            writer.WriteEndObject();
            writer.WriteEndObject();
        }

        writer.WriteEndArray();
        writer.WriteEndObject();
    }

    /// <summary>
    /// A block id drawn from the seeded generator rather than from <see cref="Guid.NewGuid"/>, so
    /// that two runs of the same options produce byte-identical content.
    /// </summary>
    private static Guid NewId(Random random)
    {
        var bytes = new byte[16];

        random.NextBytes(bytes);

        return new Guid(bytes);
    }

    private static string Pick(Random random, string[] choices) => choices[random.Next(choices.Length)];

    private static string Write(Action<Utf8JsonWriter> body)
    {
        var buffer = new ArrayBufferWriter<byte>();

        using (var writer = new Utf8JsonWriter(buffer))
        {
            body(writer);
        }

        return Encoding.UTF8.GetString(buffer.WrittenSpan);
    }
}
