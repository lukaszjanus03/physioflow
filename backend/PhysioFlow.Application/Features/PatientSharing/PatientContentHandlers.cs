using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Application.Features.Therapy;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.PatientSharing;

public record ListPatientTherapyPlansQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<PaginatedList<PatientTherapyPlanListItemDto>>;
public record GetPatientTherapyPlanQuery(Guid Id) : IRequest<PatientTherapyPlanDto>;
public record ListPatientVisitSummariesQuery(int PageNumber = 1, int PageSize = 20)
    : IRequest<PaginatedList<PatientVisitSummaryListItemDto>>;
public record GetPatientVisitSummaryQuery(Guid Id) : IRequest<PatientVisitSummaryDto>;
public record GetPatientPlanVideoPlaybackQuery(Guid PlanId, Guid ItemId) : IRequest<ExerciseVideoPlaybackDto>;

public class PatientContentHandlers(IApplicationDbContext db, ICurrentUserService user,
    IUserAccountService accounts, IExerciseVideoStorage storage, IDateTimeProvider clock) :
    IRequestHandler<ListPatientTherapyPlansQuery, PaginatedList<PatientTherapyPlanListItemDto>>,
    IRequestHandler<GetPatientTherapyPlanQuery, PatientTherapyPlanDto>,
    IRequestHandler<ListPatientVisitSummariesQuery, PaginatedList<PatientVisitSummaryListItemDto>>,
    IRequestHandler<GetPatientVisitSummaryQuery, PatientVisitSummaryDto>,
    IRequestHandler<GetPatientPlanVideoPlaybackQuery, ExerciseVideoPlaybackDto>
{
    private async Task<Guid> AccountId(CancellationToken ct)
    {
        if (!user.IsInRole("Patient")) throw new ForbiddenAccessException();
        var id = user.RequireUserId();
        if (!await accounts.IsConfirmedPatientAsync(id, ct)) throw new ForbiddenAccessException();
        return id;
    }

    private IQueryable<TherapyPlan> Plans(Guid account) => db.TherapyPlans.AsNoTracking()
        .Where(p => p.PublishedToUserId == account && p.Patient.UserId == account
            && p.PublishedForLinkVersion == p.Patient.AccountLinkVersion && p.PublishedAt != null
            && !p.Patient.IsDeleted && !p.Therapist.IsDeleted && p.Patient.TherapistId == p.TherapistId
            && p.PublishedVersion != null && p.PublishedVersion.TherapyPlanId == p.Id);

    private IQueryable<MedicalRecord> Summaries(Guid account) => db.MedicalRecords.AsNoTracking()
        .Where(r => r.PublishedToUserId == account && r.Patient.UserId == account
            && r.PublishedForLinkVersion == r.Patient.AccountLinkVersion && r.PublishedAt != null
            && !r.Patient.IsDeleted && !r.Therapist.IsDeleted && r.Patient.TherapistId == r.TherapistId
            && r.Appointment != null && !r.Appointment.IsDeleted
            && r.Appointment.PatientId == r.PatientId && r.Appointment.TherapistId == r.TherapistId
            && r.PublishedVersion != null && r.PublishedVersion.MedicalRecordId == r.Id);

    private static TherapyPlanSnapshot Snapshot(string json) => JsonSerializer.Deserialize<TherapyPlanSnapshot>(json)!;

    private async Task<(Guid TherapistId, int Number, DateTime PublishedAt, Guid Token, TherapyPlanSnapshot Content)> Plan(Guid id, CancellationToken ct)
    {
        var account = await AccountId(ct);
        var grant = await Plans(account).Where(p => p.Id == id).Select(p => new
        {
            p.TherapistId, p.PublishedAt, p.ConcurrencyStamp, p.PublishedVersion!.VersionNumber, p.PublishedVersion.ContentJson
        }).SingleOrDefaultAsync(ct) ?? throw new NotFoundException("SharedTherapyPlan", id);
        return (grant.TherapistId, grant.VersionNumber, grant.PublishedAt!.Value, grant.ConcurrencyStamp, Snapshot(grant.ContentJson));
    }

    public async Task<PaginatedList<PatientTherapyPlanListItemDto>> Handle(ListPatientTherapyPlansQuery request, CancellationToken ct)
    {
        var account = await AccountId(ct);
        TherapyValidation.Page(request.PageNumber, request.PageSize);
        var query = Plans(account);
        var total = await query.CountAsync(ct);
        var grants = await query.OrderByDescending(p => p.PublishedAt).ThenByDescending(p => p.Id)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize)
            .Select(p => new { p.Id, p.TherapistId, p.PublishedAt, p.PublishedVersion!.VersionNumber, p.PublishedVersion.ContentJson })
            .ToListAsync(ct);
        return new(grants.Select(g =>
        {
            var snapshot = Snapshot(g.ContentJson);
            return new PatientTherapyPlanListItemDto(g.Id, g.TherapistId, snapshot.Name, g.VersionNumber,
                snapshot.StartDate, snapshot.EndDate, g.PublishedAt!.Value);
        }).ToList(), total, request.PageNumber, request.PageSize);
    }

    public async Task<PatientTherapyPlanDto> Handle(GetPatientTherapyPlanQuery request, CancellationToken ct)
    {
        var grant = await Plan(request.Id, ct);
        var s = grant.Content;
        return new(request.Id, grant.TherapistId, grant.Number, grant.PublishedAt,
            new(s.Name, s.Goal, s.StartDate, s.EndDate, s.GeneralRecommendations,
                s.Exercises.Select(e => new PatientTherapyPlanItemDto(e.Id, e.Order, e.Name, e.Instructions,
                    e.Sets, e.Repetitions, e.DurationSeconds, e.Frequency, e.Notes,
                    e.Video is null ? null : new(e.Video.Source.ToString(), e.Video.YoutubeUrl))).ToList()));
    }

    public async Task<PaginatedList<PatientVisitSummaryListItemDto>> Handle(ListPatientVisitSummariesQuery request, CancellationToken ct)
    {
        var account = await AccountId(ct);
        TherapyValidation.Page(request.PageNumber, request.PageSize);
        var query = Summaries(account);
        var total = await query.CountAsync(ct);
        var items = await query.OrderByDescending(r => r.PublishedAt).ThenByDescending(r => r.Id)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize)
            .Select(r => new PatientVisitSummaryListItemDto(r.Id, r.TherapistId, r.AppointmentId!.Value,
                r.Appointment!.StartsAt, r.PublishedVersion!.VersionNumber, r.PublishedAt!.Value)).ToListAsync(ct);
        return new(items, total, request.PageNumber, request.PageSize);
    }

    public async Task<PatientVisitSummaryDto> Handle(GetPatientVisitSummaryQuery request, CancellationToken ct)
    {
        var account = await AccountId(ct);
        // Project only the approved patient summary; no clinical version or full record is materialized.
        return await Summaries(account).Where(r => r.Id == request.Id)
            .Select(r => new PatientVisitSummaryDto(r.Id, r.TherapistId, r.AppointmentId!.Value,
                r.Appointment!.StartsAt, r.PublishedVersion!.VersionNumber, r.PublishedAt!.Value,
                r.PublishedVersion.PatientSummary!)).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException("SharedVisitSummary", request.Id);
    }

    public async Task<ExerciseVideoPlaybackDto> Handle(GetPatientPlanVideoPlaybackQuery request, CancellationToken ct)
    {
        var grant = await Plan(request.PlanId, ct);
        var material = grant.Content.Exercises.SingleOrDefault(e => e.Id == request.ItemId)?.Video;
        if (material is null) throw new NotFoundException("SharedPlanVideo", request.ItemId);
        if (material.Source != ExerciseVideoSource.Upload)
            throw new ConflictException("video_not_uploaded", "Use the canonical YouTube URL for this material.");
        // Resolve only the immutable material referenced by this published plan position, never library state.
        var video = await db.ExerciseVideos.AsNoTracking().SingleOrDefaultAsync(v => v.Id == material.Id
            && v.TherapistId == grant.TherapistId && v.Source == ExerciseVideoSource.Upload, ct)
            ?? throw new NotFoundException("SharedPlanVideo", request.ItemId);
        var lifetime = ExerciseVideoHandlers.PlaybackLifetime;
        var expiresAt = clock.UtcNow.Add(lifetime);
        string url;
        try
        {
            url = await storage.GetPresignedUrlAsync(video.StorageKey!, lifetime, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new StorageUnavailableException(ex); }
        // Recheck after the provider call to catch revocation while signing.
        var latest = await Plan(request.PlanId, ct);
        if (latest.Token != grant.Token)
            throw new NotFoundException("SharedPlanVideo", request.ItemId);
        return new(url, expiresAt);
    }
}
