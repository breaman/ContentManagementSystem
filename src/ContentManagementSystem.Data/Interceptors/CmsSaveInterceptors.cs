using ContentManagementSystem.Data.Interfaces;

using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ContentManagementSystem.Data.Interceptors;

/// <summary>
/// The save-time interceptors every <c>ApplicationDbContext</c> must be built with, and the order
/// they run in.
/// </summary>
/// <remarks>
/// <b>The order is the behaviour, so it is declared once here rather than at each call site.</b>
/// Soft deletes are rewritten first so that fingerprinting and audit capture see the update the
/// delete became; fingerprints are stamped next so that the audit row records the stamped values;
/// audit capture runs last, over everything the other two left behind.
/// <para>
/// <b>Anything that builds a context by hand must add these.</b> Unlike a <c>SaveChanges</c>
/// override they are not part of the type — a context built from options that omit them saves
/// happily and silently records nothing, which is the one real cost of holding this behaviour in
/// interceptors. There are four places that build options: the host, the SQL Server test fixture,
/// and the two suites that re-register the context to inject a failing interceptor.
/// </para>
/// </remarks>
public static class CmsSaveInterceptors
{
    /// <summary>
    /// Registers the interceptors so <see cref="Resolve"/> can build them from a scope.
    /// </summary>
    /// <param name="services">The container being configured.</param>
    /// <returns>The same collection, for chaining.</returns>
    /// <remarks>
    /// Scoped because <see cref="IUserService"/> is: each one reads the caller from the scope its
    /// context belongs to. They hold no state of their own beyond those two dependencies, so
    /// several contexts opened in one scope — as the delivery readers do — can share them.
    /// </remarks>
    public static IServiceCollection AddCmsSaveInterceptors(this IServiceCollection services)
    {
        // Matches how every other area of the solution supplies the clock, so a host that registers
        // its own fake still has exactly one.
        services.TryAddSingleton(TimeProvider.System);

        services.TryAddScoped<SoftDeleteInterceptor>();
        services.TryAddScoped<FingerPrintInterceptor>();
        services.TryAddScoped<AuditLogInterceptor>();

        return services;
    }

    /// <summary>
    /// Builds the interceptors, in order, from a scope that has them registered.
    /// </summary>
    /// <param name="services">
    /// The scope the context belongs to. It must be a scoped provider: the interceptors read
    /// <see cref="IUserService"/>, which answers per request.
    /// </param>
    /// <returns>The interceptors, in the order they must run.</returns>
    /// <example>
    /// <code>
    /// services.AddCmsSaveInterceptors();
    /// services.AddDbContextFactory&lt;ApplicationDbContext&gt;(
    ///     (provider, options) => options
    ///         .UseSqlServer(connectionString)
    ///         .AddInterceptors(CmsSaveInterceptors.Resolve(provider)),
    ///     ServiceLifetime.Scoped);
    /// </code>
    /// </example>
    public static IInterceptor[] Resolve(IServiceProvider services) =>
    [
        services.GetRequiredService<SoftDeleteInterceptor>(),
        services.GetRequiredService<FingerPrintInterceptor>(),
        services.GetRequiredService<AuditLogInterceptor>(),
    ];

    /// <summary>
    /// Builds the interceptors, in order, without a container.
    /// </summary>
    /// <param name="users">
    /// Who the caller is, or <see langword="null"/> where there is nobody — a test fixture writing
    /// rows directly, or design-time tooling. Null attributes changes to user <c>0</c> and leaves
    /// <c>DeletedBy</c> unset, which is what a context built from bare options has always done.
    /// </param>
    /// <param name="clock">The clock every stamped timestamp is read from.</param>
    /// <returns>The interceptors, in the order they must run.</returns>
    public static IInterceptor[] Create(IUserService? users, TimeProvider clock) =>
    [
        new SoftDeleteInterceptor(users, clock),
        new FingerPrintInterceptor(users, clock),
        new AuditLogInterceptor(users, clock),
    ];
}
