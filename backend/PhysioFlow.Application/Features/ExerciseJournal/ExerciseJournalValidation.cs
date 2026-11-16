using FluentValidation;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.ExerciseJournal;

public class ExerciseJournalContentValidator : AbstractValidator<ExerciseJournalContent>
{
    public ExerciseJournalContentValidator(IDateTimeProvider clock)
    {
        RuleFor(x => x.PerformedOn).Must(d => d >= new DateOnly(1900, 1, 1) && d <= JournalDates.Today(clock))
            .WithMessage("Performance date must be a valid past or current date in Europe/Warsaw.").OverridePropertyName("performedOn");
        RuleFor(x => x.Status).NotNull().WithMessage("Completion status is required.")
            .IsInEnum().WithMessage("Invalid completion status.").OverridePropertyName("status");
        RuleFor(x => x.ActualSets).InclusiveBetween(1, 100).WithMessage("Actual sets must be between 1 and 100.")
            .When(x => x.ActualSets.HasValue).OverridePropertyName("actualSets");
        RuleFor(x => x.ActualRepetitions).InclusiveBetween(1, 1000).WithMessage("Actual repetitions must be between 1 and 1000.")
            .When(x => x.ActualRepetitions.HasValue).OverridePropertyName("actualRepetitions");
        RuleFor(x => x.ActualDurationSeconds).InclusiveBetween(1, 86400).WithMessage("Actual duration must be between 1 and 86400 seconds.")
            .When(x => x.ActualDurationSeconds.HasValue).OverridePropertyName("actualDurationSeconds");
        RuleFor(x => x).Must(x => !(x.ActualRepetitions.HasValue && x.ActualDurationSeconds.HasValue))
            .WithMessage("Use repetitions or durationSeconds, never both.").OverridePropertyName("actualRepetitions");
        RuleFor(x => x).Must(x => x.Status != ExerciseCompletionStatus.Skipped
            || (!x.ActualSets.HasValue && !x.ActualRepetitions.HasValue && !x.ActualDurationSeconds.HasValue))
            .WithMessage("Skipped exercises cannot have an actual dose.").OverridePropertyName("status");
        RuleFor(x => x.PainLevel).InclusiveBetween(0, 10).WithMessage("Pain level must be between 0 and 10.")
            .When(x => x.PainLevel.HasValue).OverridePropertyName("painLevel");
        RuleFor(x => x.Comment).MaximumLength(ExerciseJournalEntry.CommentMaxLength)
            .WithMessage("Comment must not exceed 2000 characters.").OverridePropertyName("comment");
    }
}

internal static class JournalDates
{
    private static readonly TimeZoneInfo Zone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw");
    public static DateOnly Date(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc, Zone));
    public static DateOnly Today(IDateTimeProvider clock) => Date(clock.UtcNow);
    // PostgreSQL stores microseconds; keep create/update responses identical to later reads/replays.
    public static DateTime Timestamp(IDateTimeProvider clock)
    {
        var utc = clock.UtcNow;
        return new(utc.Ticks - utc.Ticks % 10, DateTimeKind.Utc);
    }
}
