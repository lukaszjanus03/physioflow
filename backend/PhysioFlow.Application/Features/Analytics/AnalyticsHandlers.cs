using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Application.Features.ExerciseJournal;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Analytics;

public record GetDashboardStatsQuery(DateOnly? From = null, DateOnly? To = null) : IRequest<DashboardStatsDto>;
public record GetPatientPainTrendQuery(Guid PatientId, DateOnly? From = null, DateOnly? To = null)
    : IRequest<PatientPainTrendDto>;

public class AnalyticsHandlers(IApplicationDbContext db, ICurrentUserService user,
    IUserAccountService accounts, IDateTimeProvider clock) :
    IRequestHandler<GetDashboardStatsQuery, DashboardStatsDto>,
    IRequestHandler<GetPatientPainTrendQuery, PatientPainTrendDto>
{
    public async Task<DashboardStatsDto> Handle(GetDashboardStatsQuery request, CancellationToken ct)
    {
        if (!user.IsInRole("Therapist")) throw new ForbiddenAccessException();
        var userId = user.RequireUserId();
        var therapistId = await db.Therapists.AsNoTracking().Where(t => t.UserId == userId && !t.IsDeleted)
            .Select(t => (Guid?)t.Id).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), userId);
        var range = AnalyticsDateRange.Resolve(request.From, request.To, clock);
        var startUtc = WarsawTime.DayRangeUtc(range.From).StartUtc;
        var endUtc = WarsawTime.DayRangeUtc(range.To).EndUtc;

        // Current card state, independent of the appointment date filter or portal cooperation.
        var activePatients = await db.Patients.AsNoTracking().CountAsync(p => p.TherapistId == therapistId
            && !p.IsDeleted && !p.IsArchived, ct);
        var appointments = await db.Appointments.AsNoTracking().Where(a => a.TherapistId == therapistId
                && !a.IsDeleted && a.StartsAt >= startUtc && a.StartsAt < endUtc)
            .GroupBy(a => 1).Select(g => new AppointmentStatsDto(
                g.Count(a => a.Status == AppointmentStatus.Scheduled),
                g.Count(a => a.Status == AppointmentStatus.Completed),
                g.Count(a => a.Status == AppointmentStatus.Cancelled),
                g.Count(a => a.Status == AppointmentStatus.NoShow)))
            .SingleOrDefaultAsync(ct) ?? new(0, 0, 0, 0);
        return new(range.From, range.To, AnalyticsDateRange.TimeZone, activePatients, appointments);
    }

    public async Task<PatientPainTrendDto> Handle(GetPatientPainTrendQuery request, CancellationToken ct)
    {
        var accessible = await TherapistJournalAccess.Accessible(db, user, accounts, request.PatientId, ct);
        var range = AnalyticsDateRange.Resolve(request.From, request.To, clock);
        // SQL AVG ignores null, but includes zero. A null-only day remains a gap with zero ratings.
        // No clinical text, snapshots or individual reports are loaded into memory.
        var points = await accessible.Where(e => e.PerformedOn >= range.From && e.PerformedOn <= range.To)
            .GroupBy(e => e.PerformedOn).OrderBy(g => g.Key)
            .Select(g => new DailyPainPointDto(g.Key, g.Average(e => (double?)e.PainLevel),
                g.Count(e => e.PainLevel.HasValue))).ToListAsync(ct);
        return new(range.From, range.To, AnalyticsDateRange.TimeZone, points);
    }
}
