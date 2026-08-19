namespace ContentManagementSystem.Data.Models.Cms;

/// <summary>What a <see cref="ScheduledJob"/> will do when its moment arrives.</summary>
public enum ScheduledJobKind
{
    /// <summary>Publish a version, running the same validation a manual publish runs.</summary>
    Publish = 0,

    /// <summary>Retire the page from the public site and apply the configured redirect behaviour.</summary>
    Unpublish = 1,
}
