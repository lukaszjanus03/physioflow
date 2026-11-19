using System.Collections.Concurrent;
using System.Data.Common;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using PhysioFlow.Infrastructure.Persistence;
using Xunit;

namespace PhysioFlow.Tests.Integration;

public sealed class AnalyticsSqlCapture : DbCommandInterceptor
{
    public ConcurrentQueue<string> Commands { get; } = new();
    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
        CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        Commands.Enqueue(command.CommandText);
        return ValueTask.FromResult(result);
    }
}

public sealed class PostgresAnalyticsFactory : PostgresCalendarFactory
{
    public AnalyticsSqlCapture Sql { get; } = new();
    protected override void ConfigureTestServices(IServiceCollection services)
    {
        base.ConfigureTestServices(services);
        services.AddDbContext<ApplicationDbContext>(o => o.AddInterceptors(Sql));
    }
}

public class PostgresAnalyticsTests
{
    [PostgresFact]
    public async Task Analytics_contract_access_ranges_and_daily_aggregates_run_on_PostgreSQL()
    {
        using var factory = new PostgresAnalyticsFactory();
        var tests = new AnalyticsTests(factory);
        await tests.Dashboard_isolates_therapists_counts_current_cards_and_all_visit_statuses_by_start();
        await tests.Default_range_is_current_Warsaw_week_including_Monday_and_Sunday("2026-11-22T23:30:00Z", "2026-11-23");
        await tests.Default_range_is_current_Warsaw_week_including_Monday_and_Sunday("2026-07-05T22:30:00Z", "2026-07-06");
        await tests.Default_range_is_current_Warsaw_week_including_Monday_and_Sunday("2026-11-22T22:30:00Z", "2026-11-16");
        await tests.Visit_day_respects_both_DST_changes_and_exclusive_next_midnight("2026-03-29", "2026-03-28T23:00:00Z", "2026-03-29T22:00:00Z");
        await tests.Visit_day_respects_both_DST_changes_and_exclusive_next_midnight("2026-10-25", "2026-10-24T22:00:00Z", "2026-10-25T23:00:00Z");
        await tests.Pain_groups_all_daily_reports_preserves_zero_null_and_gaps_and_returns_only_aggregates();
        await tests.Analytics_requires_authentication_and_therapist_role_and_hides_foreign_patients();
        await tests.Pain_uses_journal_access_for_archive_confirmation_deletion_and_cooperation();
        await tests.Renewed_cooperation_or_new_account_cannot_inherit_previous_link_pain_history();
        await tests.Range_validation_rejects_partial_reversed_malformed_and_overlong_ranges_and_accepts_366_days();

        var queries = factory.Sql.Commands.ToList();
        var painSql = queries.Where(sql => sql.Contains("avg(", StringComparison.OrdinalIgnoreCase)
            && sql.Contains("exercise_journal_entries")).ToList();
        painSql.Should().NotBeEmpty();
        painSql.Should().OnlyContain(sql => sql.Contains("GROUP BY") && sql.Contains("count(")
            && !sql.Contains("comment") && !sql.Contains("exercise_snapshot_json") && !sql.Contains("plan_name"));
        var visitSql = queries.Where(sql => sql.Contains("appointments") && sql.Contains("GROUP BY")).ToList();
        visitSql.Should().NotBeEmpty();
        visitSql.Should().OnlyContain(sql => sql.Contains("count(") && !sql.Contains("patient_message")
            && !sql.Contains("notes") && !sql.Contains("reason"));
    }
}
