using FluentValidation;
using MediatR;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;

namespace PhysioFlow.Application.Features.Calendar.Queries.GetAvailableSlots;

public record GetAvailableSlotsQuery(DateOnly? Date, int? DurationMinutes) : IRequest<AvailableSlotsDto>;

public class GetAvailableSlotsQueryValidator : AbstractValidator<GetAvailableSlotsQuery>
{
    public GetAvailableSlotsQueryValidator()
    {
        RuleFor(x => x.Date)
            .NotNull().WithMessage("Podaj datę.")
            .OverridePropertyName("date");

        RuleFor(x => x.DurationMinutes)
            .NotNull().WithMessage("Podaj długość wizyty.")
            .Must(v => v is null || CalendarDurations.Allowed.Contains(v.Value))
            .WithMessage("Dopuszczalne długości to wyłącznie 30, 45 i 60 minut.")
            .OverridePropertyName("durationMinutes");
    }
}

public class GetAvailableSlotsQueryHandler : IRequestHandler<GetAvailableSlotsQuery, AvailableSlotsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;
    private readonly IDateTimeProvider _clock;

    public GetAvailableSlotsQueryHandler(
        IApplicationDbContext db, ICurrentUserService currentUser, IDateTimeProvider clock)
    {
        _db = db;
        _currentUser = currentUser;
        _clock = clock;
    }

    public async Task<AvailableSlotsDto> Handle(GetAvailableSlotsQuery request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);
        return await CalendarAvailability.GetAsync(_db, therapist.Id, request.Date!.Value,
            request.DurationMinutes!.Value, _clock.UtcNow, ct);
    }
}
