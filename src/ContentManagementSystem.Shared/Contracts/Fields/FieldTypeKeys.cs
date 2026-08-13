namespace ContentManagementSystem.Shared.Contracts.Fields;

/// <summary>
/// Keys of the field types shipped with the CMS (spec section 7.1).
/// </summary>
/// <remarks>
/// These strings are written into stored payloads and into zone and block-property definitions, so
/// they are part of the database contract: changing one orphans every value already authored
/// against it. They live here rather than on the implementations so that the payload engine, the
/// renderers, and the seed data can name a field type without depending on the assembly that
/// implements it.
/// </remarks>
public static class FieldTypeKeys
{
    /// <summary>Single-line text with no markup.</summary>
    public const string PlainText = "plainText";

    /// <summary>Multi-line text with no markup; line breaks are preserved.</summary>
    public const string MultilineText = "multilineText";

    /// <summary>Markdown or HTML authored through the edit/preview editor.</summary>
    public const string RichText = "richText";

    /// <summary>Hand-written HTML, restricted to the <c>Developer</c> role.</summary>
    public const string Html = "html";

    /// <summary>A decimal number.</summary>
    public const string Number = "number";

    /// <summary>A true/false switch.</summary>
    public const string Boolean = "boolean";

    /// <summary>A calendar date with no time component.</summary>
    public const string Date = "date";

    /// <summary>An instant, stored with an explicit UTC offset.</summary>
    public const string DateTime = "dateTime";

    /// <summary>One or several values chosen from a configured option list.</summary>
    public const string Choice = "choice";

    /// <summary>An <c>#RRGGBB</c> colour, optionally constrained to a palette.</summary>
    public const string Color = "color";

    /// <summary>Arbitrary JSON, restricted to the <c>Developer</c> role.</summary>
    public const string Json = "json";

    /// <summary>An item from the media library.</summary>
    public const string Media = "media";

    /// <summary>An ordered list of media items.</summary>
    public const string MediaList = "mediaList";

    /// <summary>A link to a page, an external URL, a media item, an anchor, or an email address.</summary>
    public const string Link = "link";

    /// <summary>A reference to another page in the content tree.</summary>
    public const string PageReference = "pageReference";

    /// <summary>A reference to an independently published reusable content item.</summary>
    public const string Reusable = "reusable";

    /// <summary>An ordered list of block instances.</summary>
    public const string Blocks = "blocks";

    /// <summary>A free-form list of tags.</summary>
    public const string Tags = "tags";
}
