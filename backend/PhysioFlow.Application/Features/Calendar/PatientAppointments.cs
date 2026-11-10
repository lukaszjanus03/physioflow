using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar.Commands.CreateAppointment;
using PhysioFlow.Application.Features.Calendar.Queries.GetAppointments;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Calendar;

public record GetPatientAppointmentsQuery(DateOnly? From, DateOnly? To) : IRequest<IReadOnlyList<PatientAppointmentDto>>;
public record GetPatientAppointmentQuery(Guid Id) : IRequest<PatientAppointmentDto>;
public record GetPatientAppointmentSummaryQuery : IRequest<PatientAppointmentSummaryDto>;
public record GetTherapistAppointmentSummaryQuery : IRequest<TherapistAppointmentSummaryDto>;
public record GetPatientBookingSettingsQuery(Guid TherapistId) : IRequest<PatientBookingSettingsDto>;
public record GetPatientAvailableSlotsQuery(Guid TherapistId, DateOnly? Date, int? DurationMinutes) : IRequest<AvailableSlotsDto>;
public record BookPatientAppointmentCommand(Guid? TherapistId, DateTime? StartsAt, int? DurationMinutes,
    string? PatientMessage = null)
    : IRequest<PatientAppointmentDto>;
public record CancelPatientAppointmentCommand(Guid Id) : IRequest<PatientAppointmentDto>;

public class GetPatientAppointmentsValidator : AbstractValidator<GetPatientAppointmentsQuery>
{
    public GetPatientAppointmentsValidator() => RuleFor(x => new GetAppointmentsQuery(x.From, x.To))
        .SetValidator(new GetAppointmentsQueryValidator()).OverridePropertyName("");
}

public class GetPatientAvailableSlotsValidator : AbstractValidator<GetPatientAvailableSlotsQuery>
{
    public GetPatientAvailableSlotsValidator() => RuleFor(x =>
            new Queries.GetAvailableSlots.GetAvailableSlotsQuery(x.Date, x.DurationMinutes))
        .SetValidator(new Queries.GetAvailableSlots.GetAvailableSlotsQueryValidator()).OverridePropertyName("");
}

public class BookPatientAppointmentValidator : AbstractValidator<BookPatientAppointmentCommand>
{
    public BookPatientAppointmentValidator()
    {
        RuleFor(x => x.TherapistId).NotNull().NotEqual(Guid.Empty).OverridePropertyName("therapistId");
        RuleFor(x => x.StartsAt).NotNull().OverridePropertyName("startsAt");
        RuleFor(x => x.DurationMinutes).NotNull()
            .Must(x => x is null || CalendarDurations.Allowed.Contains(x.Value)).OverridePropertyName("durationMinutes");
        RuleFor(x => x.PatientMessage == null ? null : x.PatientMessage.Trim())
            .MaximumLength(Appointment.PatientMessageMaxLength)
            .WithMessage("Wiadomość pacjenta może mieć maksymalnie 500 znaków.")
            .OverridePropertyName("patientMessage");
    }
}

internal static class PatientAppointmentAccess
{
    public static PatientAppointmentDto ToPatientDto(this Appointment a, DateTime now) => new(
        a.Id, new AppointmentTherapistDto(a.TherapistId, a.Therapist.FirstName, a.Therapist.LastName,
            a.Therapist.PracticeName, a.Therapist.City, a.Therapist.PhoneNumber),
        DateTime.SpecifyKind(a.StartsAt, DateTimeKind.Utc), DateTime.SpecifyKind(a.EndsAt, DateTimeKind.Utc),
        (int)a.Duration, a.Status, a.Status == AppointmentStatus.Scheduled && a.StartsAt >= now.AddHours(24),
        DateTime.SpecifyKind(a.StartsAt.AddHours(-24), DateTimeKind.Utc),
        // Po przeniesieniu wizyty na inną kartę nie ujawniamy wiadomości poprzedniego pacjenta.
        a.CreatedById == a.PatientUserId ? a.PatientMessage : null);

    public static async Task<(CooperationRequest Cooperation, Patient Card)> RequireBookingAccessAsync(
        IApplicationDbContext db, Guid userId, Guid therapistId, CancellationToken ct)
    {
        var cooperation = await db.CooperationRequests.FirstOrDefaultAsync(r =>
            r.PatientUserId == userId && r.TherapistId == therapistId
            && r.Status == CooperationRequestStatus.Accepted, ct)
            ?? throw new ConflictException(CooperationErrorCodes.CooperationNotActive, "Brak aktywnej współpracy z terapeutą.");
        var card = await db.Patients.FirstOrDefaultAsync(p => p.Id == cooperation.PatientId
            && p.TherapistId == therapistId && p.UserId == userId, ct)
            ?? throw new ConflictException(CooperationErrorCodes.CooperationNotActive, "Konto nie jest powiązane z aktywną kartą.");
        if (card.IsArchived)
            throw new ConflictException(CalendarErrorCodes.CardArchived, "Karta pacjenta jest w archiwum.");
        if (!await db.Therapists.AnyAsync(t => t.Id == therapistId, ct))
            throw new ConflictException(CooperationErrorCodes.CooperationNotActive, "Terapeuta jest niedostępny.");
        return (cooperation, card);
    }
}

public class PatientAppointmentQueries :
    IRequestHandler<GetPatientAppointmentsQuery, IReadOnlyList<PatientAppointmentDto>>,
    IRequestHandler<GetPatientAppointmentQuery, PatientAppointmentDto>,
    IRequestHandler<GetPatientAppointmentSummaryQuery, PatientAppointmentSummaryDto>,
    IRequestHandler<GetTherapistAppointmentSummaryQuery, TherapistAppointmentSummaryDto>,
    IRequestHandler<GetPatientBookingSettingsQuery, PatientBookingSettingsDto>,
    IRequestHandler<GetPatientAvailableSlotsQuery, AvailableSlotsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IDateTimeProvider _clock;
    public PatientAppointmentQueries(IApplicationDbContext db, ICurrentUserService user, IDateTimeProvider clock)
        => (_db, _user, _clock) = (db, user, clock);

    private IQueryable<Appointment> OwnAppointments() => _db.Appointments.AsNoTracking()
        .Include(a => a.Therapist).Where(a => a.PatientUserId == _user.RequireUserId());

    public async Task<IReadOnlyList<PatientAppointmentDto>> Handle(GetPatientAppointmentsQuery request, CancellationToken ct)
    {
        var (start, _) = WarsawTime.DayRangeUtc(request.From!.Value);
        var (_, end) = WarsawTime.DayRangeUtc(request.To!.Value);
        var rows = await OwnAppointments().Where(a => a.StartsAt < end && a.EndsAt > start)
            .OrderBy(a => a.StartsAt).ThenBy(a => a.Id).ToListAsync(ct);
        var now = _clock.UtcNow;
        return rows.Select(a => a.ToPatientDto(now)).ToList();
    }

    public async Task<PatientAppointmentDto> Handle(GetPatientAppointmentQuery request, CancellationToken ct)
    {
        var a = await OwnAppointments().FirstOrDefaultAsync(a => a.Id == request.Id, ct)
            ?? throw new NotFoundException(nameof(Appointment), request.Id);
        return a.ToPatientDto(_clock.UtcNow);
    }

    public async Task<PatientAppointmentSummaryDto> Handle(GetPatientAppointmentSummaryQuery request, CancellationToken ct)
    {
        var now = _clock.UtcNow;
        var a = await OwnAppointments().Where(a => a.Status == AppointmentStatus.Scheduled && a.StartsAt > now)
            .OrderBy(a => a.StartsAt).ThenBy(a => a.Id).FirstOrDefaultAsync(ct);
        return new(a?.ToPatientDto(now));
    }

    public async Task<TherapistAppointmentSummaryDto> Handle(GetTherapistAppointmentSummaryQuery request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_user, ct);
        var now = _clock.UtcNow;
        var rows = await _db.Appointments.AsNoTracking().Include(a => a.Patient)
            .Where(a => a.TherapistId == therapist.Id && a.Status == AppointmentStatus.Scheduled && a.StartsAt > now)
            .OrderBy(a => a.StartsAt).ThenBy(a => a.Id).Take(5).ToListAsync(ct);
        return new(rows.Select(a => a.ToDto()).ToList());
    }

    public async Task<PatientBookingSettingsDto> Handle(GetPatientBookingSettingsQuery request, CancellationToken ct)
    {
        await PatientAppointmentAccess.RequireBookingAccessAsync(_db, _user.RequireUserId(), request.TherapistId, ct);
        var settings = await CalendarAccess.GetOrCreateSettingsAsync(_db, request.TherapistId, ct);
        return new(request.TherapistId, "Europe/Warsaw", CalendarDurations.Parse(settings.AllowedDurations));
    }

    public async Task<AvailableSlotsDto> Handle(GetPatientAvailableSlotsQuery request, CancellationToken ct)
    {
        await PatientAppointmentAccess.RequireBookingAccessAsync(_db, _user.RequireUserId(), request.TherapistId, ct);
        return await CalendarAvailability.GetAsync(_db, request.TherapistId, request.Date!.Value,
            request.DurationMinutes!.Value, _clock.UtcNow, ct);
    }
}

public class BookPatientAppointmentHandler : IRequestHandler<BookPatientAppointmentCommand, PatientAppointmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IDateTimeProvider _clock;
    public BookPatientAppointmentHandler(IApplicationDbContext db, ICurrentUserService user, IDateTimeProvider clock)
        => (_db, _user, _clock) = (db, user, clock);

    public async Task<PatientAppointmentDto> Handle(BookPatientAppointmentCommand request, CancellationToken ct)
    {
        var userId = _user.RequireUserId();
        var start = WarsawTime.RequireUtc(request.StartsAt!.Value, "startsAt");
        return await _db.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
        {
            await using var transaction = await _db.Database.BeginTransactionAsync(ct);
            var now = _clock.UtcNow;
            if (start <= now)
                throw new ConflictException(CalendarErrorCodes.AppointmentInPast, "Nie można zarezerwować wizyty w przeszłości.");
            var (cooperation, card) = await PatientAppointmentAccess.RequireBookingAccessAsync(
                _db, userId, request.TherapistId!.Value, ct);
            // Wspólny zapis z wizytą chroni przed równoczesnym zakończeniem współpracy / archiwizacją.
            cooperation.ConcurrencyStamp = Guid.NewGuid();
            card.ConcurrencyStamp = Guid.NewGuid();
            var appointment = await AppointmentScheduling.PlaceAsync(_db, request.TherapistId.Value,
                card, start, request.DurationMinutes!.Value, null, null, null, now, ct,
                patientMessage: request.PatientMessage);
            await transaction.CommitAsync(ct);
            appointment.Therapist = await _db.Therapists.FirstAsync(t => t.Id == appointment.TherapistId, ct);
            return appointment.ToPatientDto(now);
        });
    }
}

public class CancelPatientAppointmentHandler : IRequestHandler<CancelPatientAppointmentCommand, PatientAppointmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IDateTimeProvider _clock;
    public CancelPatientAppointmentHandler(IApplicationDbContext db, ICurrentUserService user, IDateTimeProvider clock)
        => (_db, _user, _clock) = (db, user, clock);

    public async Task<PatientAppointmentDto> Handle(CancelPatientAppointmentCommand request, CancellationToken ct)
    {
        var userId = _user.RequireUserId();
        var a = await _db.Appointments.Include(a => a.Therapist)
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.PatientUserId == userId, ct)
            ?? throw new NotFoundException(nameof(Appointment), request.Id);
        if (a.Status != AppointmentStatus.Scheduled)
            throw new ConflictException(CalendarErrorCodes.InvalidStatusTransition, "Wizyta nie jest zaplanowana.");
        var now = _clock.UtcNow;
        if (a.StartsAt < now.AddHours(24))
            throw new ConflictException(CalendarErrorCodes.CancellationTooLate, "Wizytę można odwołać najpóźniej 24 godziny przed rozpoczęciem.");
        var settings = await CalendarAccess.GetOrCreateSettingsAsync(_db, a.TherapistId, ct);
        a.Status = AppointmentStatus.Cancelled;
        a.ConcurrencyStamp = Guid.NewGuid();
        settings.ConcurrencyStamp = Guid.NewGuid();
        try { await _db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException)
        {
            throw new ConflictException(CalendarErrorCodes.InvalidStatusTransition, "Wizyta zmieniła się w międzyczasie. Odśwież widok.");
        }
        return a.ToPatientDto(now);
    }
}
