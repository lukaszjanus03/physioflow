using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.DTOs;

public record MedicalRecordContent
{
    public string? Interview { get; init; }
    public string? FunctionalAssessment { get; init; }
    public string? PerformedActions { get; init; }
    public string? Recommendations { get; init; }
    public string? PatientSummary { get; init; }
}

public record CreateMedicalRecordRequest : MedicalRecordContent
{
    public Guid AppointmentId { get; init; }
}

public record SaveMedicalRecordRequest : MedicalRecordContent
{
    public Guid Version { get; init; }
}

public record CorrectMedicalRecordRequest : SaveMedicalRecordRequest
{
    public string? CorrectionReason { get; init; }
}

public record ApproveMedicalRecordRequest(Guid Version);

public record MedicalRecordDto(
    Guid Id, Guid PatientId, Guid? AppointmentId, Guid TherapistId,
    MedicalRecordStatus Status, int CurrentVersion, Guid Version,
    DateTime CreatedAt, DateTime? UpdatedAt, MedicalRecordContent Content);

public record MedicalRecordListItemDto(
    Guid Id, Guid? AppointmentId, DateTime? AppointmentStartsAt,
    MedicalRecordStatus Status, int CurrentVersion, DateTime CreatedAt, DateTime? UpdatedAt);

public record MedicalRecordVersionDto(
    int VersionNumber, Guid AuthorUserId, DateTime RecordedAt,
    string? CorrectionReason, MedicalRecordContent Content);
