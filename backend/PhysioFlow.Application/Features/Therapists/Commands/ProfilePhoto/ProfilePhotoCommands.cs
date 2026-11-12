using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Therapists.Queries.GetMyTherapistProfile;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapists.Commands.ProfilePhoto;

public record UploadMyProfilePhotoCommand(Stream Content) : IRequest<TherapistOwnProfileDto>;
public record DeleteMyProfilePhotoCommand : IRequest<TherapistOwnProfileDto>;

public class ProfilePhotoCommandHandler :
    IRequestHandler<UploadMyProfilePhotoCommand, TherapistOwnProfileDto>,
    IRequestHandler<DeleteMyProfilePhotoCommand, TherapistOwnProfileDto>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IStorageService _storage;
    private readonly IProfilePhotoProcessor _processor;
    private readonly ILogger<ProfilePhotoCommandHandler> _logger;

    public ProfilePhotoCommandHandler(IApplicationDbContext db, ICurrentUserService user,
        IStorageService storage, IProfilePhotoProcessor processor, ILogger<ProfilePhotoCommandHandler> logger)
        => (_db, _user, _storage, _processor, _logger) = (db, user, storage, processor, logger);

    public async Task<TherapistOwnProfileDto> Handle(UploadMyProfilePhotoCommand request, CancellationToken ct)
    {
        var profile = await OwnProfileAsync(ct);
        var bytes = await _processor.ProcessAsync(request.Content, ct);
        var previousKey = profile.PhotoStorageKey;
        string newKey;
        try
        {
            using var content = new MemoryStream(bytes, writable: false);
            newKey = await _storage.UploadAsync(content, "profile.webp", "image/webp", ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex)
        {
            throw new StorageUnavailableException(ex);
        }

        // Publish the new pointer before removing anything. A failed upload/save preserves the old file.
        profile.PhotoStorageKey = newKey;
        await _db.SaveChangesAsync(ct);
        await TryDeleteAsync(previousKey);
        return profile.ToOwnProfileDto();
    }

    public async Task<TherapistOwnProfileDto> Handle(DeleteMyProfilePhotoCommand request, CancellationToken ct)
    {
        var profile = await OwnProfileAsync(ct);
        var previousKey = profile.PhotoStorageKey;
        if (previousKey != null)
        {
            profile.PhotoStorageKey = null;
            await _db.SaveChangesAsync(ct);
            await TryDeleteAsync(previousKey);
        }
        return profile.ToOwnProfileDto();
    }

    private async Task<TherapistProfile> OwnProfileAsync(CancellationToken ct)
    {
        var userId = _user.UserId ?? throw new ForbiddenAccessException();
        return await _db.Therapists.SingleOrDefaultAsync(t => t.UserId == userId, ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), userId);
    }

    private async Task TryDeleteAsync(string? key)
    {
        if (key == null) return;
        using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try { await _storage.DeleteAsync(key, cleanupTimeout.Token); }
        catch (Exception)
        {
            // A cleanup failure must never roll back a working replacement or reveal provider secrets.
            _logger.LogWarning("Profile photo storage cleanup failed; an unreferenced object remains in storage.");
        }
    }
}
