using Xunit;

namespace PhysioFlow.Tests.Integration;

public class PostgresTherapistExerciseJournalTests
{
    [PostgresFact]
    public async Task Therapist_journal_queries_filters_aggregates_and_access_contract_run_on_postgres()
    {
        using var factory = new PostgresCalendarFactory();
        var tests = new TherapistExerciseJournalTests(factory);
        await tests.Only_owning_therapist_can_read_and_no_therapist_route_can_modify_reports();
        await tests.Ending_and_resuming_same_account_start_new_therapist_history_without_losing_author_history();
        await tests.New_account_on_same_card_cannot_inherit_previous_authors_reports_even_after_republication();
        await tests.Archive_blocks_all_reads_and_unarchive_restores_current_link_history();
        await tests.Access_requires_existing_profile_card_confirmed_account_and_matching_active_cooperation();
        await tests.Historical_snapshots_survive_library_plan_corrections_revocation_and_plan_deletion();
        await tests.All_filters_apply_to_full_summary_and_stable_pages_count_independent_daily_reports();
        await tests.Patient_edits_and_soft_deletes_update_details_filters_and_summary_without_coalescing_null_pain();
        await tests.Invalid_filters_and_pagination_return_validation_errors_on_the_applicable_endpoints();
    }
}
