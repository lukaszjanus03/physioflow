using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Calendar.Queries.GetMyCalendarSettings;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Calendar.Commands.UpdateMyCalendarSettings;

public record UpdateMyCalendarSettingsCommand(
    int? BufferMinutes,
    IReadOnlyList<int>? AllowedDurations,
    IReadOnlyList<WeeklyHoursDto>? WeeklyHours,
    IReadOnlyList<CalendarExceptionDto>? Exceptions
) : IRequest<TherapistCalendarSettingsDto>;

public class UpdateMyCalendarSettingsCommandValidator : AbstractValidator<UpdateMyCalendarSettingsCommand>
{
    public UpdateMyCalendarSettingsCommandValidator()
    {
        RuleFor(x => x.BufferMinutes)
            .NotNull().WithMessage("Podaj bufor po wizycie.")
            .InclusiveBetween(0, TherapistCalendarSettings.MaxBufferMinutes)
            .WithMessage($"Bufor musi być z zakresu 0-{TherapistCalendarSettings.MaxBufferMinutes} minut.")
            .OverridePropertyName("bufferMinutes");

        RuleFor(x => x.AllowedDurations)
            .NotNull().WithMessage("Wybierz dozwolone długości wizyt.")
            .Must(list => list is { Count: > 0 })
            .WithMessage("Wymagany jest przynajmniej jeden wariant długości wizyty.")
            .Must(list => list is null || list.All(d => CalendarDurations.Allowed.Contains(d)))
            .WithMessage("Dopuszczalne długości to wyłącznie 30, 45 i 60 minut.")
            .OverridePropertyName("allowedDurations");

        RuleFor(x => x.WeeklyHours)
            .NotNull().WithMessage("Podaj godziny pracy w tygodniu.")
            .Must(list => list is null || list.Select(d => d.DayOfWeek).Distinct().Count() == list.Count)
            .WithMessage("Każdy dzień tygodnia może wystąpić tylko raz.")
            .OverridePropertyName("weeklyHours");

        RuleFor(x => x.Exceptions)
            .NotNull().WithMessage("Podaj wyjątki dat (może być pusta lista).")
            .Must(list => list is null || list.Count <= TherapistCalendarSettings.MaxExceptions)
            .WithMessage($"Można zapisać maksymalnie {TherapistCalendarSettings.MaxExceptions} wyjątków.")
            .Must(list => list is null || list.Select(e => e.Date).Distinct().Count() == list.Count)
            .WithMessage("Każda data wyjątku może wystąpić tylko raz.")
            .OverridePropertyName("exceptions");
    }
}

public class UpdateMyCalendarSettingsCommandHandler
    : IRequestHandler<UpdateMyCalendarSettingsCommand, TherapistCalendarSettingsDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _currentUser;

    public UpdateMyCalendarSettingsCommandHandler(IApplicationDbContext db, ICurrentUserService currentUser)
    {
        _db = db;
        _currentUser = currentUser;
    }

    public async Task<TherapistCalendarSettingsDto> Handle(UpdateMyCalendarSettingsCommand request, CancellationToken ct)
    {
        var therapist = await _db.RequireTherapistAsync(_currentUser, ct);
        var settings = await CalendarAccess.GetOrCreateSettingsAsync(_db, therapist.Id, ct);

        var weekly = NormalizeWeekly(request.WeeklyHours!);
        var exceptions = NormalizeExceptions(request.Exceptions!);

        settings.BufferMinutes = request.BufferMinutes!.Value;
        settings.AllowedDurations = CalendarDurations.Format(request.AllowedDurations!);
        settings.ConcurrencyStamp = Guid.NewGuid();

        var existing = await _db.AvailabilitySlots.Where(s => s.TherapistId == therapist.Id).ToListAsync(ct);
        _db.AvailabilitySlots.RemoveRange(existing);

        foreach (var day in weekly)
        foreach (var interval in day.Intervals)
        {
            _db.AvailabilitySlots.Add(new AvailabilitySlot
            {
                TherapistId = therapist.Id,
                DayOfWeek = day.DayOfWeek,
                SpecificDate = null,
                StartTime = interval.Start,
                EndTime = interval.End,
                IsBlocked = false,
            });
        }

        foreach (var exception in exceptions)
        {
            if (exception.IsDayOff)
            {
                _db.AvailabilitySlots.Add(new AvailabilitySlot
                {
                    TherapistId = therapist.Id,
                    DayOfWeek = null,
                    SpecificDate = exception.Date,
                    StartTime = TimeOnly.MinValue,
                    EndTime = TimeOnly.MinValue,
                    IsBlocked = true,
                });
                continue;
            }

            foreach (var interval in exception.Intervals ?? [])
            {
                _db.AvailabilitySlots.Add(new AvailabilitySlot
                {
                    TherapistId = therapist.Id,
                    DayOfWeek = null,
                    SpecificDate = exception.Date,
                    StartTime = interval.Start,
                    EndTime = interval.End,
                    IsBlocked = false,
                });
            }
        }

        await _db.SaveChangesAsync(ct);

        var slots = await _db.AvailabilitySlots.Where(s => s.TherapistId == therapist.Id).ToListAsync(ct);
        return CalendarSettingsMapper.ToDto(settings, slots);
    }

    private static IReadOnlyList<WeeklyHoursDto> NormalizeWeekly(IReadOnlyList<WeeklyHoursDto> weekly) =>
        weekly.Select((day, index) => new WeeklyHoursDto(
            day.DayOfWeek,
            CalendarSchedule.SortAndRequireNoOverlap(day.Intervals, $"weeklyHours[{index}].intervals")))
            .ToList();

    private static IReadOnlyList<CalendarExceptionDto> NormalizeExceptions(IReadOnlyList<CalendarExceptionDto> exceptions)
    {
        return exceptions.Select((item, index) =>
        {
            var field = $"exceptions[{index}]";
            if (item.IsDayOff)
                return item with { Intervals = [] };

            var intervals = CalendarSchedule.SortAndRequireNoOverlap(item.Intervals, $"{field}.intervals");
            if (intervals.Count == 0)
                throw CalendarValidation.Field(field, "Wyjątek z zastępczymi godzinami musi mieć przynajmniej jeden przedział.");
            return item with { Intervals = intervals };
        }).ToList();
    }
}
