namespace PhysioFlow.Application.Common.Interfaces;

/// <summary>
/// Powiadomienia e-mail o zmianach we współpracy. Wywoływane po zapisaniu zmiany,
/// więc implementacja nie rzuca wyjątków - błąd wysyłki tylko loguje.
/// </summary>
public interface ICooperationNotifier
{
    Task RequestReceivedAsync(Guid requestId, Guid therapistUserId, string? patientFirstName, string? patientLastName, CancellationToken ct = default);
    Task RequestAcceptedAsync(Guid requestId, Guid patientUserId, string therapistName, CancellationToken ct = default);
    Task RequestRejectedAsync(Guid requestId, Guid patientUserId, string therapistName, CancellationToken ct = default);
    Task CooperationEndedByTherapistAsync(Guid requestId, Guid patientUserId, string therapistName, CancellationToken ct = default);
    Task CooperationEndedByPatientAsync(Guid requestId, Guid therapistUserId, string? patientFirstName, string? patientLastName, CancellationToken ct = default);
}
