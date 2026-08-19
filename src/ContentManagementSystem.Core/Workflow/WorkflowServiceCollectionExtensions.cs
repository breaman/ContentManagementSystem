using ContentManagementSystem.Core.Notifications;
using ContentManagementSystem.Core.Scheduling;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ContentManagementSystem.Core.Workflow;

/// <summary>
/// Registration helper for review, comments, notifications, and scheduling (tasks P7-09 to P7-19).
/// </summary>
public static class WorkflowServiceCollectionExtensions
{
    /// <summary>
    /// Registers the workflow spine: review, comments, notifications, mail, and scheduling.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="configure">Optional mail settings, when a deployment sends mail in code.</param>
    /// <returns>The service collection, for chaining.</returns>
    /// <remarks>
    /// One call rather than five, because these are not independent: workflow raises notifications,
    /// notifications need a mail transport, and the scheduler runs a publish and then notifies its
    /// owner. A deployment that registered three of the five would fail to build the graph at
    /// startup, which is better than at midnight — but not registering them separately is better
    /// still.
    /// <para>
    /// The mail sender is chosen here rather than by configuration binding alone: with no SMTP host
    /// configured the deployment gets <see cref="LoggingCmsEmailSender"/>, which says what it would
    /// have sent instead of discarding it. See <see cref="CmsEmailOptions"/> for why SMTP is the
    /// answer to open question Q5.
    /// </para>
    /// </remarks>
    /// <example>
    /// <code>
    /// builder.Services.AddCmsWorkflow();
    /// builder.Services.Configure&lt;CmsEmailOptions&gt;(builder.Configuration.GetSection(CmsEmailOptions.SectionName));
    /// </code>
    /// </example>
    public static IServiceCollection AddCmsWorkflow(
        this IServiceCollection services,
        Action<CmsEmailOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped<IWorkflowService, WorkflowService>();
        services.TryAddScoped<ICommentService, CommentService>();
        services.TryAddScoped<INotificationService, NotificationService>();
        services.TryAddScoped<ISchedulingService, SchedulingService>();

        // Singleton: it holds two numbers read by the metrics collector and the health check, both
        // of which live outside any request.
        services.TryAddSingleton<SchedulerState>();
        services.TryAddSingleton<ScheduledJobRunner>();

        // The identity-free fallback. The web host replaces it with one that can reconstruct the
        // editor who scheduled a job — see IJobIdentityScopeFactory for why a job with no identity
        // is refused rather than merely unattributed.
        services.TryAddSingleton<IJobIdentityScopeFactory, ServiceScopeJobIdentityScopeFactory>();

        if (configure is not null) services.Configure(configure);

        services.AddOptions<CmsEmailOptions>();
        services.AddOptions<PublishSchedulerOptions>();

        // Chosen at resolution time rather than at registration, because the host binds
        // configuration after this call and a decision made now would always see an empty host.
        services.TryAddSingleton<ICmsEmailSender>(provider =>
        {
            var options = provider.GetRequiredService<IOptions<CmsEmailOptions>>();

            return string.IsNullOrWhiteSpace(options.Value.Host)
                ? ActivatorUtilities.CreateInstance<LoggingCmsEmailSender>(provider)
                : ActivatorUtilities.CreateInstance<SmtpCmsEmailSender>(provider);
        });

        return services;
    }
}
