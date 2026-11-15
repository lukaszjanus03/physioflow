namespace PhysioFlow.Application.DTOs;

// Version is the current parent edit token, VersionNumber selects an immutable approved/activated snapshot.
public record PublishPatientContentRequest(Guid Version, int VersionNumber);
public record PatientPublicationDto(int VersionNumber, Guid PatientUserId, DateTime PublishedAt, bool IsAccessible);

public record PatientTherapyPlanListItemDto(Guid Id, Guid TherapistId, string? Name, int VersionNumber,
    DateOnly? StartDate, DateOnly? EndDate, DateTime PublishedAt);
public record PatientTherapyPlanDto(Guid Id, Guid TherapistId, int VersionNumber, DateTime PublishedAt,
    PatientTherapyPlanContentDto Content);
public record PatientTherapyPlanContentDto(string? Name, string? Goal, DateOnly? StartDate, DateOnly? EndDate,
    string? GeneralRecommendations, IReadOnlyList<PatientTherapyPlanItemDto> Exercises);
// Deliberately excludes library exercise/material IDs and every therapist-only library field.
public record PatientTherapyPlanItemDto(Guid Id, int Order, string Name, string? Instructions,
    int? Sets, int? Repetitions, int? DurationSeconds, string? Frequency, string? Notes,
    PatientExerciseVideoDto? Video);
public record PatientExerciseVideoDto(string Source, string? YoutubeUrl);

public record PatientVisitSummaryListItemDto(Guid Id, Guid TherapistId, Guid AppointmentId,
    DateTime AppointmentStartsAt, int VersionNumber, DateTime PublishedAt);
public record PatientVisitSummaryDto(Guid Id, Guid TherapistId, Guid AppointmentId,
    DateTime AppointmentStartsAt, int VersionNumber, DateTime PublishedAt, string Summary);
