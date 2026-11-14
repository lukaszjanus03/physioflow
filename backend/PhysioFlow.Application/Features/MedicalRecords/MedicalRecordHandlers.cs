using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.MedicalRecords;

public record CreateMedicalRecordCommand(Guid PatientId, CreateMedicalRecordRequest Body) : IRequest<MedicalRecordDto>;
public record SaveMedicalRecordCommand(Guid Id, SaveMedicalRecordRequest Body) : IRequest<MedicalRecordDto>;
public record ApproveMedicalRecordCommand(Guid Id, Guid Version) : IRequest<MedicalRecordDto>;
public record CorrectMedicalRecordCommand(Guid Id, CorrectMedicalRecordRequest Body) : IRequest<MedicalRecordDto>;
public record GetMedicalRecordQuery(Guid Id) : IRequest<MedicalRecordDto>;
public record ListMedicalRecordsQuery(Guid PatientId, int PageNumber = 1, int PageSize = 20)
    : IRequest<PaginatedList<MedicalRecordListItemDto>>;
public record GetMedicalRecordHistoryQuery(Guid Id) : IRequest<IReadOnlyList<MedicalRecordVersionDto>>;

public class MedicalRecordHandlers(IApplicationDbContext db, ICurrentUserService user, IDateTimeProvider clock) :
    IRequestHandler<CreateMedicalRecordCommand, MedicalRecordDto>,
    IRequestHandler<SaveMedicalRecordCommand, MedicalRecordDto>,
    IRequestHandler<ApproveMedicalRecordCommand, MedicalRecordDto>,
    IRequestHandler<CorrectMedicalRecordCommand, MedicalRecordDto>,
    IRequestHandler<GetMedicalRecordQuery, MedicalRecordDto>,
    IRequestHandler<ListMedicalRecordsQuery, PaginatedList<MedicalRecordListItemDto>>,
    IRequestHandler<GetMedicalRecordHistoryQuery, IReadOnlyList<MedicalRecordVersionDto>>
{
    private async Task<Guid> TherapistId(CancellationToken ct)
    {
        if (!user.IsInRole("Therapist")) throw new ForbiddenAccessException();
        return (await db.RequireTherapistAsync(user, ct)).Id;
    }

    private async Task RequirePatient(Guid patientId, Guid therapistId, CancellationToken ct)
    {
        // Archived cards remain available to their owning therapist.
        if (!await db.Patients.AnyAsync(p => p.Id == patientId && p.TherapistId == therapistId, ct))
            throw new NotFoundException(nameof(Patient), patientId);
    }

    private async Task<MedicalRecord> Find(Guid id, CancellationToken ct)
    {
        var therapistId = await TherapistId(ct);
        return await db.MedicalRecords.FirstOrDefaultAsync(r => r.Id == id && r.TherapistId == therapistId
            && r.Patient.TherapistId == therapistId && !r.Patient.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(MedicalRecord), id);
    }

    private static void CheckVersion(MedicalRecord record, Guid version)
    {
        if (record.ConcurrencyStamp != version)
            throw new ConflictException("concurrent_modification", "Documentation changed. Reload it before saving.");
    }

    public async Task<MedicalRecordDto> Handle(CreateMedicalRecordCommand request, CancellationToken ct)
    {
        var therapistId = await TherapistId(ct);
        await RequirePatient(request.PatientId, therapistId, ct);
        if (!await db.Appointments.AnyAsync(a => a.Id == request.Body.AppointmentId
            && a.PatientId == request.PatientId && a.TherapistId == therapistId, ct))
            throw new NotFoundException(nameof(Appointment), request.Body.AppointmentId);
        // The unique appointment index also protects concurrent creates and soft-deleted legacy entries.
        if (await db.MedicalRecords.IgnoreQueryFilters().AnyAsync(r => r.AppointmentId == request.Body.AppointmentId, ct))
            throw new ConflictException("medical_record_already_exists", "Documentation for this appointment already exists.");
        var record = new MedicalRecord
        {
            PatientId = request.PatientId, TherapistId = therapistId, AppointmentId = request.Body.AppointmentId
        };
        Apply(record, request.Body);
        db.MedicalRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return ToDto(record);
    }

    public async Task<MedicalRecordDto> Handle(SaveMedicalRecordCommand request, CancellationToken ct)
    {
        var record = await Find(request.Id, ct);
        CheckVersion(record, request.Body.Version);
        if (record.Status != MedicalRecordStatus.Draft)
            throw new ConflictException("medical_record_correction_required", "Approved documentation requires a correction.");
        await RequireAppointment(record, ct);
        Apply(record, request.Body);
        record.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ToDto(record);
    }

    public async Task<MedicalRecordDto> Handle(ApproveMedicalRecordCommand request, CancellationToken ct)
    {
        var record = await Find(request.Id, ct);
        CheckVersion(record, request.Version);
        if (record.Status != MedicalRecordStatus.Draft)
            throw new ConflictException("medical_record_already_approved", "Documentation is already approved.");
        await RequireCompletedAppointment(record, ct);
        await new MedicalRecordContentValidator(requireComplete: true).ValidateAndThrowAsync(Content(record), ct);
        AddVersion(record, null);
        await db.SaveChangesAsync(ct);
        return ToDto(record);
    }

    public async Task<MedicalRecordDto> Handle(CorrectMedicalRecordCommand request, CancellationToken ct)
    {
        var record = await Find(request.Id, ct);
        CheckVersion(record, request.Body.Version);
        if (record.Status != MedicalRecordStatus.Approved)
            throw new ConflictException("medical_record_not_approved", "Only approved documentation can be corrected.");
        await RequireCompletedAppointment(record, ct);
        Apply(record, request.Body);
        AddVersion(record, request.Body.CorrectionReason!.Trim());
        // One SaveChanges commits the latest projection and its immutable snapshot atomically.
        await db.SaveChangesAsync(ct);
        return ToDto(record);
    }

    private async Task<Appointment> RequireAppointment(MedicalRecord record, CancellationToken ct)
    {
        return await db.Appointments.AsNoTracking().FirstOrDefaultAsync(a => a.Id == record.AppointmentId
            && a.PatientId == record.PatientId && a.TherapistId == record.TherapistId, ct)
            ?? throw new NotFoundException(nameof(Appointment), record.AppointmentId ?? Guid.Empty);
    }

    private async Task RequireCompletedAppointment(MedicalRecord record, CancellationToken ct)
    {
        var appointment = await RequireAppointment(record, ct);
        if (appointment.Status != AppointmentStatus.Completed)
            throw new ConflictException("medical_record_completed_appointment_required", "The appointment must be completed before approval.");
    }

    private void AddVersion(MedicalRecord record, string? reason)
    {
        record.CurrentVersion++;
        record.Status = MedicalRecordStatus.Approved;
        record.ConcurrencyStamp = Guid.NewGuid();
        db.MedicalRecordVersions.Add(new MedicalRecordVersion
        {
            MedicalRecordId = record.Id, VersionNumber = record.CurrentVersion,
            AuthorUserId = user.RequireUserId(), RecordedAt = clock.UtcNow, CorrectionReason = reason,
            Interview = record.Interview!, FunctionalAssessment = record.FunctionalAssessment!,
            PerformedActions = record.PerformedActions!, Recommendations = record.Recommendations!,
            PatientSummary = record.PatientSummary
        });
    }

    public async Task<MedicalRecordDto> Handle(GetMedicalRecordQuery request, CancellationToken ct) => ToDto(await Find(request.Id, ct));

    public async Task<PaginatedList<MedicalRecordListItemDto>> Handle(ListMedicalRecordsQuery request, CancellationToken ct)
    {
        var therapistId = await TherapistId(ct);
        await RequirePatient(request.PatientId, therapistId, ct);
        var query = db.MedicalRecords.AsNoTracking().Where(r => r.PatientId == request.PatientId && r.TherapistId == therapistId);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.Appointment == null ? r.CreatedAt : r.Appointment.StartsAt)
            .ThenByDescending(r => r.Id).Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize)
            .Select(r => new MedicalRecordListItemDto(r.Id, r.AppointmentId,
                r.Appointment == null ? null : r.Appointment.StartsAt, r.Status, r.CurrentVersion, r.CreatedAt, r.UpdatedAt))
            .ToListAsync(ct);
        return new(items, total, request.PageNumber, request.PageSize);
    }

    public async Task<IReadOnlyList<MedicalRecordVersionDto>> Handle(GetMedicalRecordHistoryQuery request, CancellationToken ct)
    {
        await Find(request.Id, ct);
        return await db.MedicalRecordVersions.AsNoTracking().Where(v => v.MedicalRecordId == request.Id)
            .OrderBy(v => v.VersionNumber)
            .Select(v => new MedicalRecordVersionDto(v.VersionNumber, v.AuthorUserId, v.RecordedAt,
                v.CorrectionReason, new MedicalRecordContent
                {
                    Interview = v.Interview, FunctionalAssessment = v.FunctionalAssessment,
                    PerformedActions = v.PerformedActions, Recommendations = v.Recommendations, PatientSummary = v.PatientSummary
                })).ToListAsync(ct);
    }

    private static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    private static void Apply(MedicalRecord record, MedicalRecordContent content)
    {
        record.Interview = Clean(content.Interview);
        record.FunctionalAssessment = Clean(content.FunctionalAssessment);
        record.PerformedActions = Clean(content.PerformedActions);
        record.Recommendations = Clean(content.Recommendations);
        record.PatientSummary = Clean(content.PatientSummary);
    }

    private static MedicalRecordContent Content(MedicalRecord record) => new()
    {
        Interview = record.Interview, FunctionalAssessment = record.FunctionalAssessment,
        PerformedActions = record.PerformedActions, Recommendations = record.Recommendations, PatientSummary = record.PatientSummary
    };

    private static MedicalRecordDto ToDto(MedicalRecord record) => new(record.Id, record.PatientId, record.AppointmentId,
        record.TherapistId, record.Status, record.CurrentVersion, record.ConcurrencyStamp,
        record.CreatedAt, record.UpdatedAt, Content(record));
}
