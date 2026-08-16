using System.Collections.Frozen;
using System.Text;

using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace ContentManagementSystem.Core.Media.Upload;

/// <summary>
/// What sanitizing an SVG produced.
/// </summary>
/// <param name="Svg">The rewritten document, or null when nothing usable survived.</param>
/// <param name="RemovedElements">Names of elements that were deleted, for the upload report.</param>
/// <param name="RemovedAttributes">Names of attributes that were deleted.</param>
public sealed record SvgSanitizationResult(
    string? Svg,
    IReadOnlyList<string> RemovedElements,
    IReadOnlyList<string> RemovedAttributes);

/// <summary>
/// The strict SVG profile (task P5-06, spec section 13.3 step 5).
/// </summary>
/// <remarks>
/// Only reached when a deployment sets <see cref="SvgUploadPolicy.Sanitize"/>; the default is to
/// refuse SVG entirely, and this exists so that a site which genuinely needs vector logos has one
/// implementation to trust rather than a rule someone works around.
/// <para>
/// <strong>An allowlist of drawing elements and drawing attributes, and nothing else.</strong> SVG
/// is executable XML served from the site's own origin, so the removals are not decoration:
/// <c>script</c> runs JavaScript; <c>foreignObject</c> embeds arbitrary HTML inside a file the
/// browser is treating as an image; <c>use</c> and <c>image</c> can pull in a remote document;
/// <c>animate</c> can rewrite another element's attributes, including a <c>href</c>, after load; and
/// every <c>on*</c> attribute is an event handler. Each of those is a documented, exploited XSS
/// route, which is why the profile is expressed as what may stay rather than as what must go.
/// </para>
/// <para>
/// Parsing is AngleSharp's — the same real DOM the HTML sanitizer uses, for the same reason
/// (ADR 0008). A regex over SVG source is defeated by entity encoding, CDATA sections, namespace
/// prefixes, and malformed markup that browsers nevertheless recover.
/// </para>
/// </remarks>
public static class SvgSanitizer
{
    /// <summary>Elements the strict profile keeps.</summary>
    /// <remarks>
    /// Shapes, paths, groups, gradients, and the structural elements those need. Text is included:
    /// a logo with a wordmark is the common case, and text nodes carry no behaviour. What is absent
    /// is everything that loads, scripts, animates, or embeds.
    /// </remarks>
    private static readonly FrozenSet<string> AllowedElements = new[]
    {
        "svg", "g", "defs", "symbol", "title", "desc", "metadata",
        "path", "rect", "circle", "ellipse", "line", "polyline", "polygon",
        "text", "tspan", "textpath",
        "lineargradient", "radialgradient", "stop", "pattern", "clippath", "mask",
        "marker", "switch",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Attributes the strict profile keeps, beyond the presentation ones below.</summary>
    private static readonly FrozenSet<string> AllowedAttributes = new[]
    {
        "id", "class", "d", "points", "x", "y", "x1", "y1", "x2", "y2",
        "cx", "cy", "r", "rx", "ry", "width", "height",
        "viewbox", "preserveaspectratio", "transform", "gradienttransform",
        "offset", "spreadmethod", "gradientunits", "patternunits", "clippathunits", "maskunits",
        "markerwidth", "markerheight", "refx", "refy", "orient",
        "version", "xmlns", "role", "aria-label", "aria-labelledby", "aria-hidden",
        "dx", "dy", "text-anchor", "font-family", "font-size", "font-weight", "font-style",
        "letter-spacing", "word-spacing", "textlength", "lengthadjust",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>Painting attributes, kept because an SVG without them draws nothing.</summary>
    private static readonly FrozenSet<string> AllowedPresentationAttributes = new[]
    {
        "fill", "fill-opacity", "fill-rule",
        "stroke", "stroke-width", "stroke-opacity", "stroke-linecap", "stroke-linejoin",
        "stroke-dasharray", "stroke-dashoffset", "stroke-miterlimit",
        "opacity", "color", "stop-color", "stop-opacity", "display", "visibility",
        "clip-path", "clip-rule", "mask", "vector-effect", "paint-order",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Elements whose subtree is deleted rather than unwrapped.
    /// </summary>
    /// <remarks>
    /// Unwrapping is right for an unknown container — the drawing inside it survives. It is wrong
    /// for these: unwrapping a <c>script</c> leaves its source as a text node, and unwrapping a
    /// <c>foreignObject</c> promotes the HTML it was carrying into the document.
    /// </remarks>
    private static readonly FrozenSet<string> DeletedOutright = new[]
    {
        "script", "foreignobject", "iframe", "embed", "object", "audio", "video",
        "animate", "animatemotion", "animatetransform", "set", "handler",
        "style", "image", "use", "a", "filter", "feimage",
    }.ToFrozenSet(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Rewrites an SVG document to the strict profile.
    /// </summary>
    /// <param name="svg">The uploaded SVG source.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>The rewritten document and what was taken out of it.</returns>
    /// <remarks>
    /// The output is what gets stored. Sanitizing on the way out instead would leave the raw upload
    /// on disk, one forgotten render path away from being served (ADR 0008 makes the same argument
    /// for HTML).
    /// </remarks>
    /// <example>
    /// <code>
    /// var result = await SvgSanitizer.SanitizeAsync(source, cancellationToken);
    /// if (result.Svg is null) return Rejected(MediaCodes.SvgUnsafe);
    /// </code>
    /// </example>
    public static async Task<SvgSanitizationResult> SanitizeAsync(
        string svg,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(svg);

        var removedElements = new List<string>();
        var removedAttributes = new List<string>();

        var parser = new HtmlParser();

        // Parsed as an HTML fragment rather than as XML: an HTML parser is what a browser applies to
        // an inline SVG, and it is the one whose recovery behaviour an attacker would be relying on.
        // Sanitizing against a stricter parser than the consumer uses is how markup that looks inert
        // to the sanitizer becomes markup the browser executes.
        var document = await parser.ParseDocumentAsync(
            $"<!DOCTYPE html><html><body>{svg}</body></html>", cancellationToken).ConfigureAwait(false);

        var root = document.Body?.QuerySelector("svg");

        if (root is null)
        {
            return new SvgSanitizationResult(null, removedElements, removedAttributes);
        }

        Clean(root, removedElements, removedAttributes);

        // Re-checked after cleaning: a document whose only content was a script is now an empty
        // <svg> element, and storing that as an image would be storing nothing.
        if (root.ChildElementCount is 0 && string.IsNullOrWhiteSpace(root.TextContent))
        {
            return new SvgSanitizationResult(null, removedElements, removedAttributes);
        }

        var builder = new StringBuilder();

        builder.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        builder.Append(root.OuterHtml);

        return new SvgSanitizationResult(builder.ToString(), removedElements, removedAttributes);
    }

    /// <summary>
    /// Applies the profile to one element and everything beneath it.
    /// </summary>
    /// <param name="element">The element to clean.</param>
    /// <param name="removedElements">Collected element removals.</param>
    /// <param name="removedAttributes">Collected attribute removals.</param>
    /// <remarks>
    /// Children are walked over a snapshot of the collection, because cleaning mutates it. Iterating
    /// the live collection while removing from it is how a sanitizer skips a sibling — and a skipped
    /// sibling is an element that was never checked.
    /// </remarks>
    private static void Clean(IElement element, List<string> removedElements, List<string> removedAttributes)
    {
        CleanAttributes(element, removedAttributes);

        foreach (var child in element.Children.ToArray())
        {
            var name = child.LocalName;

            if (DeletedOutright.Contains(name))
            {
                removedElements.Add(name);
                child.Remove();

                continue;
            }

            if (!AllowedElements.Contains(name))
            {
                removedElements.Add(name);

                // Unwrapped: an unrecognised container may still hold ordinary shapes. The promoted
                // children stay in this element's collection, and the snapshot above means they are
                // not visited on this pass — so each is cleaned explicitly as it is moved.
                while (child.FirstChild is { } grandchild)
                {
                    element.InsertBefore(grandchild, child);

                    if (grandchild is IElement promoted)
                    {
                        CleanPromoted(promoted, removedElements, removedAttributes);
                    }
                }

                child.Remove();

                continue;
            }

            Clean(child, removedElements, removedAttributes);
        }
    }

    /// <summary>
    /// Cleans an element promoted out of an unwrapped parent, applying the profile to the element
    /// itself as well as to its subtree.
    /// </summary>
    /// <param name="element">The promoted element.</param>
    /// <param name="removedElements">Collected element removals.</param>
    /// <param name="removedAttributes">Collected attribute removals.</param>
    /// <remarks>
    /// <see cref="Clean"/> checks an element's <em>children</em> against the allowlist, so a
    /// promoted element would otherwise have its attributes stripped and its own name never checked
    /// — which is how a <c>script</c> nested inside an unknown wrapper survives.
    /// </remarks>
    private static void CleanPromoted(
        IElement element,
        List<string> removedElements,
        List<string> removedAttributes)
    {
        var name = element.LocalName;

        if (DeletedOutright.Contains(name) || !AllowedElements.Contains(name))
        {
            removedElements.Add(name);
            element.Remove();

            return;
        }

        Clean(element, removedElements, removedAttributes);
    }

    /// <summary>Strips every attribute the profile does not name.</summary>
    /// <param name="element">The element to clean.</param>
    /// <param name="removedAttributes">Collected attribute removals.</param>
    private static void CleanAttributes(IElement element, List<string> removedAttributes)
    {
        foreach (var attribute in element.Attributes.ToArray())
        {
            // The prefix is dropped before the check. A namespaced spelling such as xlink:href is a
            // different attribute name and the same capability, and a check that only knew the
            // unprefixed name would let it through.
            var name = attribute.LocalName;

            if (AllowedAttributes.Contains(name) || AllowedPresentationAttributes.Contains(name))
            {
                // style is not on either list, so no value here can carry a url() or an expression;
                // what remains are literal geometry and paint values.
                continue;
            }

            removedAttributes.Add(attribute.Name);

            // Namespaced attributes have to be removed by namespace and local name; the single-
            // argument overload matches on the qualified name and misses them.
            if (attribute.NamespaceUri is { Length: > 0 } namespaceUri)
            {
                element.RemoveAttribute(namespaceUri, name);
            }
            else
            {
                element.RemoveAttribute(attribute.Name);
            }
        }
    }
}
