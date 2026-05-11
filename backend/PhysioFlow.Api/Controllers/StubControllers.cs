using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace PhysioFlow.Api.Controllers;

[Authorize(Roles = "Therapist")]
public class AppointmentsController : ApiControllerBase
{
    // TODO: GetAppointmentsQuery (range)
    // TODO: CreateAppointmentCommand (z blokadą double-booking)
    // TODO: UpdateAppointmentCommand
    // TODO: CancelAppointmentCommand (+ email via Resend)
    // TODO: GetAvailabilityQuery (dla portalu pacjenta)

    [HttpGet]
    public IActionResult GetRange([FromQuery] DateTime from, [FromQuery] DateTime to)
        => throw new NotImplementedException();
}

[Authorize(Roles = "Therapist")]
public class MedicalRecordsController : ApiControllerBase
{
    // TODO: GetMedicalRecordsQuery (chronologiczna historia)
    // TODO: CreateMedicalRecordCommand
    // TODO: UploadAttachmentCommand (IStorageService -> R2)
    // TODO: GetAttachmentPresignedUrlQuery
}

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
    // TODO: GetMyUpcomingAppointmentsQuery
    // TODO: GetMyCurrentPlanQuery
    // TODO: CreateDiaryEntryCommand
    // TODO: CancelMyAppointmentCommand
}

[Authorize(Roles = "Therapist")]
public class AnalyticsController : ApiControllerBase
{
    // TODO: GetDashboardStatsQuery (aktywni pacjenci, wizyty/tydzień)
    // TODO: GetPatientPainTrendQuery (dla Recharts)
}
