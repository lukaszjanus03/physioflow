using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.MedicalRecords;

public record ExportMedicalRecordsQuery(Guid PatientId, DateOnly? From = null, DateOnly? To = null,
    int PageNumber = 1, int PageSize = 20) : IRequest<PaginatedList<MedicalRecordExportItemDto>>;

public class ExportMedicalRecordsValidator : AbstractValidator<ExportMedicalRecordsQuery>
{
    public ExportMedicalRecordsValidator()
    {
        RuleFor(x => x.PatientId).NotEmpty().OverridePropertyName("patientId");
        RuleFor(x => x.From).Must(d => d is null || d >= new DateOnly(1900, 1, 1))
            .WithMessage("The date must be on or after 1900-01-01.").OverridePropertyName("from");
        RuleFor(x => x.To).Must(d => d is null || d >= new DateOnly(1900, 1, 1))
            .WithMessage("The date must be on or after 1900-01-01.").OverridePropertyName("to");
        RuleFor(x => x.To).Must((q, to) => q.From is null || to is null || q.From <= to)
            .WithMessage("The end date must be on or after the start date.").OverridePropertyName("to");
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).OverridePropertyName("pageSize");
        RuleFor(x => x.PageNumber).GreaterThan(0)
            .Must((q, page) => ((long)page - 1) * q.PageSize <= int.MaxValue).OverridePropertyName("pageNumber");
    }
}

public partial class MedicalRecordHandlers : IRequestHandler<ExportMedicalRecordsQuery, PaginatedList<MedicalRecordExportItemDto>>
{
    public async Task<PaginatedList<MedicalRecordExportItemDto>> Handle(ExportMedicalRecordsQuery request, CancellationToken ct)
    {
        var therapistId = await TherapistId(ct);
        await RequirePatient(request.PatientId, therapistId, ct);

        // Read content and approval time only from the immutable current approved snapshot.
        // Neither the editable projection nor the version published to the portal is an export source.
        var query = db.MedicalRecordVersions.AsNoTracking().Where(v =>
            v.MedicalRecord.PatientId == request.PatientId && v.MedicalRecord.TherapistId == therapistId
            && v.MedicalRecord.Patient.TherapistId == therapistId && !v.MedicalRecord.Patient.IsDeleted
            && v.MedicalRecord.Status == MedicalRecordStatus.Approved
            && v.VersionNumber == v.MedicalRecord.CurrentVersion
            && (v.MedicalRecord.AppointmentId == null || (v.MedicalRecord.Appointment != null
                && v.MedicalRecord.Appointment.PatientId == request.PatientId
                && v.MedicalRecord.Appointment.TherapistId == therapistId)));

        if (request.From.HasValue || request.To.HasValue)
            query = query.Where(v => v.MedicalRecord.AppointmentId != null);
        if (request.From is { } from)
        {
            WarsawTime.TryToUtc(from, TimeOnly.MinValue, out var start);
            query = query.Where(v => v.MedicalRecord.Appointment!.StartsAt >= start);
        }
        // MaxValue already includes every representable appointment; avoid AddDays overflow.
        if (request.To is { } to && to != DateOnly.MaxValue)
        {
            var (_, end) = WarsawTime.DayRangeUtc(to);
            query = query.Where(v => v.MedicalRecord.Appointment!.StartsAt < end);
        }

        var total = await query.CountAsync(ct);
        var items = await query.OrderBy(v => v.MedicalRecord.AppointmentId == null)
            .ThenBy(v => v.MedicalRecord.Appointment == null ? v.MedicalRecord.CreatedAt : v.MedicalRecord.Appointment.StartsAt)
            .ThenBy(v => v.MedicalRecordId)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize)
            .Select(v => new MedicalRecordExportItemDto(v.MedicalRecordId, v.MedicalRecord.AppointmentId,
                v.MedicalRecord.Appointment == null ? null : v.MedicalRecord.Appointment.StartsAt,
                v.VersionNumber, v.RecordedAt, new MedicalRecordContent
                {
                    Interview = v.Interview, FunctionalAssessment = v.FunctionalAssessment,
                    PerformedActions = v.PerformedActions, Recommendations = v.Recommendations, PatientSummary = v.PatientSummary
                })).ToListAsync(ct);
        return new(items, total, request.PageNumber, request.PageSize);
    }
}
