using ContentManagementSystem.Core.Media.Delivery;
using ContentManagementSystem.Server.Security;
using ContentManagementSystem.ServiceDefaults;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

using NSubstitute;

namespace ContentManagementSystem.Server.Tests.Security;

/// <summary>
/// The startup refusal of spec section 20.8 (task P9-05).
/// </summary>
/// <remarks>
/// Driven against a hand-built provider rather than the application, because what is under test is a
/// decision about configuration and every case worth asserting is one the real host is arranged never
/// to be in. A container would only be able to prove the passing case.
/// </remarks>
public class SecretsGuardTests
{
    private static readonly string GoodKey = Convert.ToBase64String(new byte[32]);

    [Test]
    public async Task DevelopmentIsExemptEvenWithNothingConfigured()
    {
        // The point is not to make a first run require a key vault. The unconfigured key logs a
        // warning there, which is the right volume for a machine where one instance signing and
        // another rejecting cannot happen.
        var act = () => Guard(Environments.Development, key: null, connectionString: Dev());

        act.Should().NotThrow();

        await Task.CompletedTask;
    }

    [Test]
    public async Task ProductionRefusesToStartWithNoSigningKey()
    {
        var act = () => Guard(Environments.Production, key: null, connectionString: "Server=prod;");

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MediaSigning:Key*");

        await Task.CompletedTask;
    }

    [Test]
    public async Task AKeyIsMeasuredInBytesRatherThanCharacters()
    {
        // A base64 string is a third longer than its bytes, so a character count accepts a 24-byte
        // key written as 32 characters — which is the mistake this is here to refuse.
        var short24Bytes = Convert.ToBase64String(new byte[24]);

        short24Bytes.Length.Should().BeGreaterThanOrEqualTo(MediaSigningOptions.MinimumKeyBytes);

        var act = () => Guard(Environments.Production, short24Bytes, "Server=prod;");

        act.Should().Throw<InvalidOperationException>();
    }

    [Test]
    public async Task ProductionRefusesTheAspireDevelopmentPassword()
    {
        var act = () => Guard(Environments.Production, GoodKey, Dev());

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*development password*");

        await Task.CompletedTask;
    }

    [Test]
    public async Task ARotationWithNoEndDateIsRefused()
    {
        // A previous key that never expires is a rotation that has not removed the old key from
        // anything, which is the state a half-finished rotation is left in.
        var act = () => Guard(
            Environments.Production,
            GoodKey,
            "Server=prod;",
            options => options.PreviousKey = Convert.ToBase64String(new byte[32]));

        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PreviousKeyExpiresOn*");

        await Task.CompletedTask;
    }

    [Test]
    public async Task AProperlyConfiguredProductionStartsUp()
    {
        var act = () => Guard(
            Environments.Production,
            GoodKey,
            "Server=prod;Password=something-else;",
            options =>
            {
                options.PreviousKey = Convert.ToBase64String(new byte[32]);
                options.PreviousKeyExpiresOn = DateTimeOffset.UtcNow.AddDays(7);
            });

        act.Should().NotThrow();

        await Task.CompletedTask;
    }

    private static string Dev() =>
        $"Server=localhost;User Id=sa;Password={CmsSecretsGuard.DevelopmentDatabasePassword};";

    private static void Guard(
        string environmentName,
        string? key,
        string connectionString,
        Action<MediaSigningOptions>? configure = null)
    {
        var signing = new MediaSigningOptions { Key = key };
        configure?.Invoke(signing);

        var services = new ServiceCollection()
            .AddSingleton<IOptions<MediaSigningOptions>>(Options.Create(signing))
            .BuildServiceProvider();

        var environment = Substitute.For<IHostEnvironment>();
        environment.EnvironmentName.Returns(environmentName);

        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [$"ConnectionStrings:{Constants.DatabaseConnectionString}"] = connectionString,
            })
            .Build();

        services.AssertCmsSecrets(environment, configuration);
    }
}
