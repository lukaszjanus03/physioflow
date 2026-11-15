using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Application.Features.MedicalRecords;
using PhysioFlow.Application.Features.Therapy;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.PatientSharing;

public record PublishTherapyPlanCommand(Guid Id, PublishPatientContentRequest Body) : IRequest<TherapyPlanDto>;
public record RevokeTherapyPlanCommand(Guid Id, Guid Version) : IRequest<TherapyPlanDto>;
public record PublishVisitSummaryCommand(Guid Id, PublishPatientContentRequest Body) : IRequest<MedicalRecordDto>;
public record RevokeVisitSummaryCommand(Guid Id, Guid Version) : IRequest<MedicalRecordDto>;

public class PublishContentHandlers(IApplicationDbContext db, ICurrentUserService user,
    IUserAccountService accounts, IDateTimeProvider clock) :
    IRequestHandler<PublishTherapyPlanCommand, TherapyPlanDto>,
    IRequestHandler<RevokeTherapyPlanCommand, TherapyPlanDto>,
    IRequestHandler<PublishVisitSummaryCommand, MedicalRecordDto>,
    IRequestHandler<RevokeVisitSummaryCommand, MedicalRecordDto>
{
    private async Task<Guid> TherapistId(CancellationToken ct)
    {
        if (!user.IsInRole("Therapist")) throw new ForbiddenAccessException();
        return (await db.RequireTherapistAsync(user, ct)).Id;
    }

    private async Task<TherapyPlan> Plan(Guid id, CancellationToken ct)
    {
        var owner = await TherapistId(ct);
        return await db.TherapyPlans.Include(p => p.Patient).Include(p => p.PublishedVersion)
            .Include(p => p.Exercises).ThenInclude(e => e.Video)
            .SingleOrDefaultAsync(p => p.Id == id && p.TherapistId == owner
                && p.Patient.TherapistId == owner && !p.Patient.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(TherapyPlan), id);
    }

    private async Task<MedicalRecord> Record(Guid id, CancellationToken ct)
    {
        var owner = await TherapistId(ct);
        return await db.MedicalRecords.Include(r => r.Patient).Include(r => r.PublishedVersion)
            .SingleOrDefaultAsync(r => r.Id == id && r.TherapistId == owner
                && r.Patient.TherapistId == owner && !r.Patient.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(MedicalRecord), id);
    }

    public async Task<TherapyPlanDto> Handle(PublishTherapyPlanCommand request, CancellationToken ct)
    {
        var plan = await Plan(request.Id, ct);
        PublicationPolicy.CheckToken(plan.ConcurrencyStamp, request.Body.Version);
        TherapyValidation.Require(request.Body.VersionNumber > 0, "versionNumber", "An activated version number is required.");
        if (plan.Status is TherapyPlanStatus.Draft or TherapyPlanStatus.Archived)
            throw new ConflictException("plan_not_publishable", "Only activated, nonarchived plans can be published.");
        var snapshot = await db.TherapyPlanVersions.SingleOrDefaultAsync(v => v.TherapyPlanId == plan.Id
            && v.VersionNumber == request.Body.VersionNumber, ct)
            ?? throw new NotFoundException(nameof(TherapyPlanVersion), request.Body.VersionNumber);
        await PublicationPolicy.RequirePublishablePatient(db, accounts, plan.Patient, ct);
        PublicationPolicy.Publish(plan, plan.Patient, snapshot.Id, clock.UtcNow);
        plan.PublishedVersion = snapshot;
        plan.IsPublished = true;
        plan.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return TherapyPlanHandlers.ToDto(plan);
    }

    public async Task<TherapyPlanDto> Handle(RevokeTherapyPlanCommand request, CancellationToken ct)
    {
        var plan = await Plan(request.Id, ct);
        PublicationPolicy.CheckToken(plan.ConcurrencyStamp, request.Version);
        PublicationPolicy.Revoke(plan);
        plan.PublishedVersion = null;
        plan.IsPublished = false;
        plan.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return TherapyPlanHandlers.ToDto(plan);
    }

    public async Task<MedicalRecordDto> Handle(PublishVisitSummaryCommand request, CancellationToken ct)
    {
        var record = await Record(request.Id, ct);
        PublicationPolicy.CheckToken(record.ConcurrencyStamp, request.Body.Version);
        TherapyValidation.Require(request.Body.VersionNumber > 0, "versionNumber", "An approved version number is required.");
        if (record.Status != MedicalRecordStatus.Approved)
            throw new ConflictException("medical_record_not_approved", "Only approved documentation summaries can be published.");
        if (!await db.Appointments.AnyAsync(a => a.Id == record.AppointmentId && a.PatientId == record.PatientId
            && a.TherapistId == record.TherapistId && a.Status == AppointmentStatus.Completed, ct))
            throw new ConflictException("medical_record_completed_appointment_required", "Publishing requires a completed appointment.");
        var snapshot = await db.MedicalRecordVersions.SingleOrDefaultAsync(v => v.MedicalRecordId == record.Id
            && v.VersionNumber == request.Body.VersionNumber, ct)
            ?? throw new NotFoundException(nameof(MedicalRecordVersion), request.Body.VersionNumber);
        if (string.IsNullOrWhiteSpace(snapshot.PatientSummary))
            throw new ConflictException("patient_summary_empty", "The approved version has no patient summary.");
        await PublicationPolicy.RequirePublishablePatient(db, accounts, record.Patient, ct);
        PublicationPolicy.Publish(record, record.Patient, snapshot.Id, clock.UtcNow);
        record.PublishedVersion = snapshot;
        record.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return MedicalRecordHandlers.ToDto(record);
    }

    public async Task<MedicalRecordDto> Handle(RevokeVisitSummaryCommand request, CancellationToken ct)
    {
        var record = await Record(request.Id, ct);
        PublicationPolicy.CheckToken(record.ConcurrencyStamp, request.Version);
        PublicationPolicy.Revoke(record);
        record.PublishedVersion = null;
        record.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return MedicalRecordHandlers.ToDto(record);
    }
}
