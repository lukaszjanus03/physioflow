using Microsoft.AspNetCore.Authorization;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Patient")]
public class PatientPortalController : ApiControllerBase
{
    // TODO: GetMyCurrentPlanQuery
    // TODO: CreateDiaryEntryCommand
}
