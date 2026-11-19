using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using PhysioFlow.Infrastructure.Identity;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;
using static PhysioFlow.Tests.Integration.TwoFactorTests;

namespace PhysioFlow.Tests.Integration;

public sealed class TwoFactorWriteBarrier : SaveChangesInterceptor
{
    private int _arrived;
    private bool _enabled;
    private TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Enable() { _arrived = 0; _ready = new(TaskCreationOptions.RunContinuationsAsynchronously); _enabled = true; }
    public void Disable() => _enabled = false;
    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
        InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (_enabled && eventData.Context!.ChangeTracker.Entries<ApplicationUser>().Any(e => e.State == EntityState.Modified))
        {
            if (Interlocked.Increment(ref _arrived) >= 2) _ready.TrySetResult();
            await _ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        return result;
    }
}

public sealed class PostgresTwoFactorFactory : PostgresCalendarFactory
{
    public TwoFactorClock TwoFactorClock { get; } = new();
    public TwoFactorWriteBarrier Writes { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.RemoveAll<TimeProvider>();
        services.AddSingleton<TimeProvider>(TwoFactorClock);
        services.AddDbContext<ApplicationDbContext>(o => o.AddInterceptors(Writes));
    }

    public async Task<HttpResponseMessage[]> Race(Func<Task<HttpResponseMessage>> first, Func<Task<HttpResponseMessage>> second)
    {
        Writes.Enable();
        try { return await Task.WhenAll(first(), second()); }
        finally { Writes.Disable(); }
    }
}

public class PostgresTwoFactorTests
{
    [PostgresFact]
    public async Task Both_roles_can_setup_login_recover_regenerate_and_disable_on_postgresql()
    {
        using var factory = new PostgresTwoFactorFactory();
        foreach (var role in new[] { "Patient", "Therapist" })
        {
            var (client, email) = await Account(factory, role);
            var enabled = await Enable(client, email, factory.TwoFactorClock);
            await Verify(client, await Challenge(client, email), recovery: enabled.Codes[0]);
            var regenerated = await Read<RecoveryCodesResponse>(await client.PostAsJsonAsync(Url + "/recovery-codes/regenerate",
                new { password = TestAccounts.Password, recoveryCode = enabled.Codes[1] }));
            var challenge = await Challenge(client, email);
            (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = challenge.ChallengeToken, recoveryCode = enabled.Codes[2] })))
                .Should().Be("two_factor_invalid_code");
            await Verify(client, challenge, recovery: regenerated.RecoveryCodes[0]);
            (await client.PostAsJsonAsync(Url + "/disable", new { password = TestAccounts.Password, recoveryCode = regenerated.RecoveryCodes[1] }))
                .StatusCode.Should().Be(HttpStatusCode.NoContent);
            (await Login(client, email)).ShouldPromptTwoFactor.Should().BeFalse();
        }
    }

    [PostgresFact]
    public async Task Concurrent_recovery_and_totp_verifications_issue_only_one_session()
    {
        using var factory = new PostgresTwoFactorFactory();
        var (client, email) = await Account(factory, "Patient");
        var enabled = await Enable(client, email, factory.TwoFactorClock);
        foreach (var useRecovery in new[] { true, false })
        {
            var challenge = await Challenge(client, email);
            factory.TwoFactorClock.Advance();
            var request = new { challengeToken = challenge.ChallengeToken,
                code = useRecovery ? null : Code(factory.TwoFactorClock, enabled.Key),
                recoveryCode = useRecovery ? enabled.Codes[0] : null };
            var responses = await factory.Race(() => client.PostAsJsonAsync(Url + "/verify", request),
                () => client.PostAsJsonAsync(Url + "/verify", request));
            responses.Select(r => r.StatusCode).Should().BeEquivalentTo([HttpStatusCode.OK, HttpStatusCode.Conflict]);
            (await Error(await client.PostAsJsonAsync(Url + "/verify", request))).Should().Be("two_factor_invalid_challenge");
        }
        var retry = await Challenge(client, email);
        (await Error(await client.PostAsJsonAsync(Url + "/verify", new { challengeToken = retry.ChallengeToken, recoveryCode = enabled.Codes[0] })))
            .Should().Be("two_factor_invalid_code");
    }

    [PostgresFact]
    public async Task Migration_preserves_existing_accounts_and_does_not_prompt_them()
    {
        using var factory = new PostgresTwoFactorFactory();
        var (client, email) = await Account(factory, "Patient");
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync("20261118164608_AddPrivateCooperationChat");
            await migrator.MigrateAsync();
            var user = await db.Users.AsNoTracking().SingleAsync(u => u.Email == email);
            user.TwoFactorEnabled.Should().BeFalse();
            user.TwoFactorPromptHandled.Should().BeTrue();
            user.AuthVersion.Should().Be(0);
            user.ProtectedTwoFactorState.Should().BeNull();
        }
        (await Login(client, email)).ShouldPromptTwoFactor.Should().BeFalse();
        // The backfill does not affect new registrations.
        var (newClient, newEmail) = await Account(factory, "Patient");
        (await Login(newClient, newEmail)).ShouldPromptTwoFactor.Should().BeTrue();
    }
}
