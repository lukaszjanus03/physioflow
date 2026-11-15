using System.Text.Json;
using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Application.Features.PatientSharing;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapy;

public record CreateTherapyPlanCommand(Guid PatientId, TherapyPlanContent Body) : IRequest<TherapyPlanDto>;
public record SaveTherapyPlanCommand(Guid Id, SaveTherapyPlanRequest Body) : IRequest<TherapyPlanDto>;
public record ActivateTherapyPlanCommand(Guid Id, Guid Version) : IRequest<TherapyPlanDto>;
public record CorrectTherapyPlanCommand(Guid Id, CorrectTherapyPlanRequest Body) : IRequest<TherapyPlanDto>;
public record TransitionTherapyPlanCommand(Guid Id, Guid Version, TherapyPlanStatus Status) : IRequest<TherapyPlanDto>;
public record GetTherapyPlanQuery(Guid Id) : IRequest<TherapyPlanDto>;
public record ListTherapyPlansQuery(Guid PatientId, TherapyPlanStatus? Status = null, int PageNumber = 1, int PageSize = 20)
    : IRequest<PaginatedList<TherapyPlanListItemDto>>;
public record GetTherapyPlanHistoryQuery(Guid Id) : IRequest<IReadOnlyList<TherapyPlanVersionDto>>;

public class TherapyPlanHandlers(IApplicationDbContext db, ICurrentUserService user, IDateTimeProvider clock) :
    IRequestHandler<CreateTherapyPlanCommand, TherapyPlanDto>,
    IRequestHandler<SaveTherapyPlanCommand, TherapyPlanDto>,
    IRequestHandler<ActivateTherapyPlanCommand, TherapyPlanDto>,
    IRequestHandler<CorrectTherapyPlanCommand, TherapyPlanDto>,
    IRequestHandler<TransitionTherapyPlanCommand, TherapyPlanDto>,
    IRequestHandler<GetTherapyPlanQuery, TherapyPlanDto>,
    IRequestHandler<ListTherapyPlansQuery, PaginatedList<TherapyPlanListItemDto>>,
    IRequestHandler<GetTherapyPlanHistoryQuery, IReadOnlyList<TherapyPlanVersionDto>>
{
    private async Task<Guid> TherapistId(CancellationToken ct)
    {
        if (!user.IsInRole("Therapist")) throw new ForbiddenAccessException();
        return (await db.RequireTherapistAsync(user, ct)).Id;
    }

    private async Task<Patient> Patient(Guid id, Guid therapistId, CancellationToken ct) =>
        await db.Patients.SingleOrDefaultAsync(p => p.Id == id && p.TherapistId == therapistId, ct)
        ?? throw new NotFoundException(nameof(Domain.Entities.Patient), id);

    private async Task<TherapyPlan> Find(Guid id, CancellationToken ct)
    {
        var owner = await TherapistId(ct);
        return await db.TherapyPlans.Include(p => p.Exercises).ThenInclude(e => e.Video).Include(p => p.Patient)
            .Include(p => p.PublishedVersion)
            .SingleOrDefaultAsync(p => p.Id == id && p.TherapistId == owner
                && p.Patient.TherapistId == owner && !p.Patient.IsDeleted, ct)
            ?? throw new NotFoundException(nameof(TherapyPlan), id);
    }

    private static void Writable(Patient patient)
    {
        if (patient.IsArchived)
            throw new ConflictException("patient_archived", "Restore the patient card before changing therapy plans.");
    }

    private static void CheckVersion(TherapyPlan plan, Guid version)
    {
        TherapyValidation.Require(version != Guid.Empty, "version", "Edit token is required.");
        if (plan.ConcurrencyStamp != version)
            throw new ConflictException("concurrent_modification", "Therapy plan changed. Reload before saving.");
        Writable(plan.Patient);
    }

    private static void RequireStatus(TherapyPlan plan, TherapyPlanStatus expected)
    {
        if (plan.Status != expected)
            throw new ConflictException("invalid_plan_transition", "This operation is not allowed in the current plan status.");
    }

    public async Task<TherapyPlanDto> Handle(CreateTherapyPlanCommand request, CancellationToken ct)
    {
        var owner = await TherapistId(ct);
        var patient = await Patient(request.PatientId, owner, ct);
        Writable(patient);
        await new TherapyPlanContentValidator().ValidateAndThrowAsync(request.Body, ct);
        var plan = new TherapyPlan { PatientId = patient.Id, TherapistId = owner, Patient = patient };
        await Apply(plan, request.Body, ct);
        db.TherapyPlans.Add(plan);
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<TherapyPlanDto> Handle(SaveTherapyPlanCommand request, CancellationToken ct)
    {
        var plan = await Find(request.Id, ct);
        CheckVersion(plan, request.Body.Version);
        RequireStatus(plan, TherapyPlanStatus.Draft);
        await new TherapyPlanContentValidator().ValidateAndThrowAsync(request.Body, ct);
        await Apply(plan, request.Body, ct);
        plan.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<TherapyPlanDto> Handle(ActivateTherapyPlanCommand request, CancellationToken ct)
    {
        var plan = await Find(request.Id, ct);
        CheckVersion(plan, request.Version);
        RequireStatus(plan, TherapyPlanStatus.Draft);
        await ValidateComplete(plan, ct);
        AddVersion(plan, null);
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<TherapyPlanDto> Handle(CorrectTherapyPlanCommand request, CancellationToken ct)
    {
        var plan = await Find(request.Id, ct);
        CheckVersion(plan, request.Body.Version);
        RequireStatus(plan, TherapyPlanStatus.Active);
        TherapyValidation.Require(!string.IsNullOrWhiteSpace(request.Body.CorrectionReason)
            && request.Body.CorrectionReason.Length <= TherapyPlan.CorrectionReasonMaxLength,
            "correctionReason", "Correction reason is required (maximum 1000 characters).");
        await new TherapyPlanContentValidator(complete: true).ValidateAndThrowAsync(request.Body, ct);
        await Apply(plan, request.Body, ct);
        await ValidateComplete(plan, ct);
        AddVersion(plan, request.Body.CorrectionReason!.Trim());
        // Latest projection, positions, edit token and immutable version share one transaction.
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<TherapyPlanDto> Handle(TransitionTherapyPlanCommand request, CancellationToken ct)
    {
        var plan = await Find(request.Id, ct);
        CheckVersion(plan, request.Version);
        if (request.Status == TherapyPlanStatus.Completed) RequireStatus(plan, TherapyPlanStatus.Active);
        else if (request.Status != TherapyPlanStatus.Archived || plan.Status == TherapyPlanStatus.Archived)
            throw new ConflictException("invalid_plan_transition", "This status transition is not allowed.");
        plan.Status = request.Status;
        plan.ConcurrencyStamp = Guid.NewGuid();
        await db.SaveChangesAsync(ct);
        return ToDto(plan);
    }

    public async Task<TherapyPlanDto> Handle(GetTherapyPlanQuery request, CancellationToken ct) => ToDto(await Find(request.Id, ct));

    public async Task<PaginatedList<TherapyPlanListItemDto>> Handle(ListTherapyPlansQuery request, CancellationToken ct)
    {
        var owner = await TherapistId(ct);
        await Patient(request.PatientId, owner, ct);
        TherapyValidation.Page(request.PageNumber, request.PageSize);
        TherapyValidation.Require(request.Status == null || Enum.IsDefined(request.Status.Value), "status", "Invalid plan status.");
        var query = db.TherapyPlans.AsNoTracking().Where(p => p.PatientId == request.PatientId && p.TherapistId == owner);
        if (request.Status.HasValue) query = query.Where(p => p.Status == request.Status);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(p => p.CreatedAt).ThenByDescending(p => p.Id)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize)
            .Select(p => new TherapyPlanListItemDto(p.Id, p.Name, p.Status, p.CurrentVersion,
                p.StartDate, p.EndDate, p.CreatedAt, p.UpdatedAt,
                p.PublishedVersion == null ? null : new PatientPublicationDto(p.PublishedVersion.VersionNumber,
                    p.PublishedToUserId!.Value, p.PublishedAt!.Value, p.Patient.UserId == p.PublishedToUserId
                    && p.Patient.AccountLinkVersion == p.PublishedForLinkVersion))).ToListAsync(ct);
        return new(items, total, request.PageNumber, request.PageSize);
    }

    public async Task<IReadOnlyList<TherapyPlanVersionDto>> Handle(GetTherapyPlanHistoryQuery request, CancellationToken ct)
    {
        await Find(request.Id, ct);
        var versions = await db.TherapyPlanVersions.AsNoTracking().Where(v => v.TherapyPlanId == request.Id)
            .OrderBy(v => v.VersionNumber).ToListAsync(ct);
        return versions.Select(v => new TherapyPlanVersionDto(v.VersionNumber, v.AuthorUserId, v.ActivatedAt,
            v.CorrectionReason, JsonSerializer.Deserialize<TherapyPlanSnapshot>(v.ContentJson)!)).ToList();
    }

    private async Task Apply(TherapyPlan plan, TherapyPlanContent content, CancellationToken ct)
    {
        var existing = plan.Exercises.ToDictionary(e => e.Id);
        var newIds = content.Exercises.Where(i => i.Id == null).Select(i => i.ExerciseId).Distinct().ToArray();
        var library = await db.Exercises.Include(e => e.Video).Where(e => newIds.Contains(e.Id)
            && e.TherapistId == plan.TherapistId).ToDictionaryAsync(e => e.Id, ct);
        var positions = new List<TherapyPlanExercise>();
        foreach (var (item, order) in content.Exercises.Select((item, index) => (item, index)))
        {
            TherapyPlanExercise position;
            if (item.Id is { } id)
            {
                if (!existing.TryGetValue(id, out position!) || position.ExerciseId != item.ExerciseId)
                    throw new NotFoundException(nameof(TherapyPlanExercise), id);
            }
            else
            {
                if (!library.TryGetValue(item.ExerciseId, out var exercise))
                    throw new NotFoundException(nameof(Exercise), item.ExerciseId);
                if (exercise.IsArchived)
                    throw new ConflictException("exercise_archived", "Archived exercises cannot be added to plans.");
                position = new TherapyPlanExercise { TherapyPlanId = plan.Id, ExerciseId = exercise.Id,
                    ExerciseName = exercise.Name, Instructions = exercise.Instructions,
                    VideoId = exercise.VideoId, Video = exercise.Video };
                // Added explicitly: positions have client-generated Guid keys, including on tracked plans.
                db.TherapyPlanExercises.Add(position);
            }
            position.Order = order;
            position.Sets = item.Sets;
            position.Repetitions = item.Repetitions;
            position.DurationSeconds = item.DurationSeconds;
            position.Frequency = TherapyValidation.Clean(item.Frequency);
            position.Notes = TherapyValidation.Clean(item.Notes);
            positions.Add(position);
        }
        var retained = positions.Select(p => p.Id).ToHashSet();
        db.TherapyPlanExercises.RemoveRange(existing.Values.Where(p => !retained.Contains(p.Id)));
        plan.Exercises = positions;
        plan.Name = TherapyValidation.Clean(content.Name);
        plan.Goal = TherapyValidation.Clean(content.Goal);
        plan.StartDate = content.StartDate;
        plan.EndDate = content.EndDate;
        plan.GeneralRecommendations = TherapyValidation.Clean(content.GeneralRecommendations);
    }

    private static async Task ValidateComplete(TherapyPlan plan, CancellationToken ct)
    {
        await new TherapyPlanContentValidator(complete: true).ValidateAndThrowAsync(new TherapyPlanContent
        {
            Name = plan.Name, Goal = plan.Goal, StartDate = plan.StartDate, EndDate = plan.EndDate,
            GeneralRecommendations = plan.GeneralRecommendations,
            Exercises = plan.Exercises.OrderBy(e => e.Order).Select(e => new TherapyPlanItemRequest
            {
                Id = e.Id, ExerciseId = e.ExerciseId, Sets = e.Sets, Repetitions = e.Repetitions,
                DurationSeconds = e.DurationSeconds, Frequency = e.Frequency, Notes = e.Notes
            }).ToList()
        }, ct);
        TherapyValidation.Require(plan.Exercises.All(e => !string.IsNullOrWhiteSpace(e.ExerciseName)
            && !string.IsNullOrWhiteSpace(e.Instructions)), "exercises", "Every position needs a name and execution instructions.");
    }

    private void AddVersion(TherapyPlan plan, string? reason)
    {
        plan.Status = TherapyPlanStatus.Active;
        plan.CurrentVersion++;
        plan.ConcurrencyStamp = Guid.NewGuid();
        db.TherapyPlanVersions.Add(new TherapyPlanVersion
        {
            TherapyPlanId = plan.Id, VersionNumber = plan.CurrentVersion, AuthorUserId = user.RequireUserId(),
            ActivatedAt = clock.UtcNow, CorrectionReason = reason, ContentJson = JsonSerializer.Serialize(Snapshot(plan))
        });
    }

    private static TherapyPlanSnapshot Snapshot(TherapyPlan plan) => new(plan.Name, plan.Goal, plan.StartDate,
        plan.EndDate, plan.GeneralRecommendations, plan.Exercises.OrderBy(e => e.Order).Select(e => new TherapyPlanItemDto(
            e.Id, e.ExerciseId, e.Order, e.ExerciseName, e.Instructions, e.Sets, e.Repetitions,
            e.DurationSeconds, e.Frequency, e.Notes, ExerciseVideoMapping.ToDto(e.Video))).ToList());

    internal static TherapyPlanDto ToDto(TherapyPlan plan) => new(plan.Id, plan.PatientId, plan.Status,
        plan.CurrentVersion, plan.ConcurrencyStamp, plan.CreatedAt, plan.UpdatedAt, Snapshot(plan),
        PublicationPolicy.ToDto(plan, plan.PublishedVersion?.VersionNumber, plan.Patient));
}
