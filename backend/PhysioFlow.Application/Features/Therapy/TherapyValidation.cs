using FluentValidation;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapy;

public class ExerciseContentValidator : AbstractValidator<ExerciseContent>
{
    public ExerciseContentValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(Exercise.NameMaxLength).OverridePropertyName("name");
        RuleFor(x => x.Instructions).NotEmpty().MaximumLength(Exercise.InstructionsMaxLength).OverridePropertyName("instructions");
        RuleFor(x => x.Category).NotEmpty().MaximumLength(Exercise.CategoryMaxLength).OverridePropertyName("category");
        RuleFor(x => x.Notes).MaximumLength(Exercise.NotesMaxLength).OverridePropertyName("notes");
    }
}

public class TherapyPlanContentValidator : AbstractValidator<TherapyPlanContent>
{
    public TherapyPlanContentValidator(bool complete = false)
    {
        RuleFor(x => x.Name).MaximumLength(TherapyPlan.NameMaxLength).OverridePropertyName("name");
        RuleFor(x => x.Goal).MaximumLength(TherapyPlan.TextMaxLength).OverridePropertyName("goal");
        RuleFor(x => x.GeneralRecommendations).MaximumLength(TherapyPlan.TextMaxLength).OverridePropertyName("generalRecommendations");
        RuleFor(x => x.StartDate).Must(d => d != DateOnly.MinValue && d != DateOnly.MaxValue)
            .When(x => x.StartDate.HasValue).OverridePropertyName("startDate");
        RuleFor(x => x.EndDate).Must(d => d != DateOnly.MinValue && d != DateOnly.MaxValue)
            .When(x => x.EndDate.HasValue).OverridePropertyName("endDate");
        RuleFor(x => x.EndDate).Must((x, end) => !x.StartDate.HasValue || !end.HasValue || end >= x.StartDate)
            .WithMessage("End date must be on or after start date.").OverridePropertyName("endDate");
        RuleFor(x => x.Exercises).NotNull().OverridePropertyName("exercises");
        When(x => x.Exercises != null, () =>
        {
            RuleFor(x => x.Exercises.Count).LessThanOrEqualTo(TherapyPlan.MaxExercises).OverridePropertyName("exercises");
            RuleFor(x => x.Exercises).Must(items => items.Where(i => i?.Id != null).Select(i => i.Id).Distinct().Count()
                == items.Count(i => i?.Id != null)).WithMessage("Position IDs must be unique.").OverridePropertyName("exercises");
            RuleForEach(x => x.Exercises).NotNull().SetValidator(new TherapyPlanItemValidator(complete)).OverridePropertyName("exercises");
        });
        if (complete)
        {
            RuleFor(x => x.Name).NotEmpty().OverridePropertyName("name");
            RuleFor(x => x.StartDate).NotNull().OverridePropertyName("startDate");
            RuleFor(x => x.EndDate).NotNull().OverridePropertyName("endDate");
            RuleFor(x => x.Exercises).NotEmpty().OverridePropertyName("exercises");
        }
    }
}

public class TherapyPlanItemValidator : AbstractValidator<TherapyPlanItemRequest>
{
    public TherapyPlanItemValidator(bool complete = false)
    {
        RuleFor(x => x.Id).NotEqual(Guid.Empty).When(x => x.Id.HasValue).OverridePropertyName("id");
        RuleFor(x => x.ExerciseId).NotEmpty().OverridePropertyName("exerciseId");
        RuleFor(x => x.Sets).InclusiveBetween(1, 100).When(x => x.Sets.HasValue).OverridePropertyName("sets");
        RuleFor(x => x.Repetitions).InclusiveBetween(1, 1000).When(x => x.Repetitions.HasValue).OverridePropertyName("repetitions");
        RuleFor(x => x.DurationSeconds).InclusiveBetween(1, 86400).When(x => x.DurationSeconds.HasValue).OverridePropertyName("durationSeconds");
        RuleFor(x => x).Must(x => !(x.Repetitions.HasValue && x.DurationSeconds.HasValue))
            .WithMessage("Use repetitions or durationSeconds, never both.").OverridePropertyName("repetitions");
        RuleFor(x => x.Frequency).MaximumLength(200).OverridePropertyName("frequency");
        RuleFor(x => x.Notes).MaximumLength(Exercise.NotesMaxLength).OverridePropertyName("notes");
        if (complete)
        {
            RuleFor(x => x.Sets).NotNull().OverridePropertyName("sets");
            RuleFor(x => x).Must(x => x.Repetitions.HasValue ^ x.DurationSeconds.HasValue)
                .WithMessage("Exactly one of repetitions or durationSeconds is required.").OverridePropertyName("repetitions");
            RuleFor(x => x.Frequency).NotEmpty().OverridePropertyName("frequency");
        }
    }
}

internal static class TherapyValidation
{
    public static void Require(bool condition, string property, string message)
    {
        if (!condition) throw new ValidationException([new(property, message)]);
    }

    public static void Page(int page, int size)
    {
        Require(size is >= 1 and <= 100, "pageSize", "Page size must be between 1 and 100.");
        Require(page > 0 && ((long)page - 1) * size <= int.MaxValue, "pageNumber", "Invalid page number.");
    }

    public static string? Clean(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();
}
