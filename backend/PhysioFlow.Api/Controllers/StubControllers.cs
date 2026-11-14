using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PhysioFlow.Api.Controllers;

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
