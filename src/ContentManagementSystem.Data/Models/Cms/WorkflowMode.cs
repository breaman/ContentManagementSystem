namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>
/// How much ceremony stands between an editor finishing a page and that page going live.
/// Configured once per site in v1; per-template workflow is v2 (spec section 11.9).
/// </summary>
public enum WorkflowMode
{
    /// <summary>Anyone holding <c>Content.Publish</c> publishes directly. No approval step.</summary>
    None = 0,

    /// <summary>
    /// Users without <c>Content.Publish</c> submit for approval; any approver may approve and
    /// publish in one action.
    /// </summary>
    Simple = 1,

    /// <summary>
    /// Submit, approve, and publish are three distinct actions, and the approver may not be the
    /// author. Use where separation of duties has to be demonstrable.
    /// </summary>
    TwoStep = 2,
}
