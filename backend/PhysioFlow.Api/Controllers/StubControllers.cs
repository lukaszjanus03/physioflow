using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Therapist")]
public class ExercisesController : ApiControllerBase
{
    // TODO: GetExerciseLibraryQuery (filtrowanie po MuscleGroup)
    // TODO: CreateExerciseCommand
    // TODO: UpdateExerciseCommand
    // TODO: DeleteExerciseCommand
}

[Authorize(Roles = "Therapist")]
public class TherapyPlansController : ApiControllerBase
{
    // TODO: GetPatientPlansQuery
    // TODO: CreateTherapyPlanCommand
    // TODO: PublishPlanCommand (IsPublished = true -> widoczny w portalu)
}

[Authorize(Roles = "Patient")]
public class PatientPortalController : ApiControllerBase
{
    // TODO: GetMyCurrentPlanQuery
    // TODO: CreateDiaryEntryCommand
}

[Authorize(Roles = "Therapist")]
public class AnalyticsController : ApiControllerBase
{
    // TODO: GetDashboardStatsQuery (aktywni pacjenci, wizyty/tydzień)
    // TODO: GetPatientPainTrendQuery (dla Recharts)
}
