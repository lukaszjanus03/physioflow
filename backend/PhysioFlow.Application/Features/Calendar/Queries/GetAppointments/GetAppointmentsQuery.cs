using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Enums;

namespace PhysioFlow.Application.Features.Calendar.Queries.GetAppointments;

public record GetAppointmentsQuery(DateOnly? From, DateOnly? To) : IRequest<IReadOnlyList<AppointmentDto>>
{
    public const int MaxRangeDays = 366;
}

public record GetAppointmentByIdQuery(Guid Id) : IRequest<AppointmentDto>;

public class GetAppointmentsQueryValidator : AbstractValidator<GetAppointmentsQuery>
{
    public GetAppointmentsQueryValidator()
    {
        RuleFor(x => x.From)
            .NotNull().WithMessage("Podaj początek zakresu.")
            .OverridePropertyName("from");

        RuleFor(x => x.To)
            .NotNull().WithMessage("Podaj koniec zakresu.")
            .OverridePropertyName("to");

        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null || x.From <= x.To)
            .WithMessage("Data początkowa nie może być późniejsza niż końcowa.")
            .OverridePropertyName("to");

        RuleFor(x => x)
            .Must(x => x.From is null || x.To is null
                       || x.To.Value.DayNumber - x.From.Value.DayNumber <= GetAppointmentsQuery.MaxRangeDays)
            .WithMessage($"Zakres nie może przekraczać {GetAppointmentsQuery.MaxRangeDays} dni.")
            .OverridePropertyName("to");
    }
}

public class GetAppointmentsQueryHandler
    : IRequestHandler<GetAppointmentsQuery, IReadOnlyList<AppointmentDto>>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetAppointmentsQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<IReadOnlyList<AppointmentDto>> Handle(GetAppointmentsQuery request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);
        var (start, _) = WarsawTime.DayRangeUtc(request.From!.Value);
        var (_, end) = WarsawTime.DayRangeUtc(request.To!.Value);

        return await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .Where(a => a.TherapistId == therapist.Id && a.StartsAt < end && a.EndsAt > start)
            .OrderBy(a => a.StartsAt).ThenBy(a => a.Id)
            .Select(a => new AppointmentDto(
                a.Id,
                a.PatientId,
                a.Patient.FirstName + " " + a.Patient.LastName,
                a.StartsAt,
                a.EndsAt,
                (int)a.Duration,
                a.Status,
                a.Reason,
                a.Notes))
            .ToListAsync(ct);
    }
}

public class GetAppointmentByIdQueryHandler : IRequestHandler<GetAppointmentByIdQuery, AppointmentDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public GetAppointmentByIdQueryHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<AppointmentDto> Handle(GetAppointmentByIdQuery request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);
        var appointment = await _db.Appointments
            .AsNoTracking()
            .Include(a => a.Patient)
            .FirstOrDefaultAsync(a => a.Id == request.Id && a.TherapistId == therapist.Id, ct)
            ?? throw new NotFoundException("Appointment", request.Id);

        return appointment.ToDto();
    }
}
