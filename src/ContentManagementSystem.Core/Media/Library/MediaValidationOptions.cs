using ContentManagementSystem.Shared.Contracts.Fields;

namespace ContentManagementSystem.Core.Media.Library;

/// <summary>
/// The publish-time media policy (task P5-21, spec section 13.7).
/// </summary>
/// <remarks>
/// Separate from <c>MediaUploadOptions</c> because it governs a different moment and a different
/// audience: upload limits are refusals aimed at whoever is putting bytes on the server, and this is
/// an editorial rule aimed at whoever is putting a picture on a page. A deployment may reasonably
/// want the upload check relaxed and the publish check kept, which one options object could not
/// express without the two settings reading as a contradiction.
/// </remarks>
public sealed class MediaValidationOptions
{
    /// <summary>Configuration section these options bind from.</summary>
    public const string SectionName = "Cms:MediaValidation";

    /// <summary>
    /// How a placed image with neither alternative text nor a decorative flag is reported.
    /// </summary>
    /// <remarks>
    /// <see cref="ValidationSeverity.Error"/> by default, which is what spec section 13.7 asks for:
    /// alt text is enforced rather than suggested, because a missing description discovered by an
    /// accessibility audit after launch is a hundred pages to revisit and a hundred editors to ask
    /// what each picture was of.
    /// <para>
    /// <see cref="ValidationSeverity.Warning"/> exists for migration and nothing else. Importing a
    /// legacy site produces thousands of undescribed images at once, and a rule that made every one
    /// of those pages unpublishable would be turned off wholesale rather than worked through — which
    /// is a worse outcome than a warning somebody is burning down.
    /// </para>
    /// </remarks>
    public ValidationSeverity MissingAltTextSeverity { get; set; } = ValidationSeverity.Error;
}
