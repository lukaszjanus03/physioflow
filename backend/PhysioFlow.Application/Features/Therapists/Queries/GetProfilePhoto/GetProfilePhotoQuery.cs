using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Therapists.Queries.GetProfilePhoto;

public record GetProfilePhotoQuery(Guid TherapistId) : IRequest<Stream>;

public class GetProfilePhotoQueryHandler : IRequestHandler<GetProfilePhotoQuery, Stream>
{
    private readonly IApplicationDbContext _db;
    private readonly ICurrentUserService _user;
    private readonly IUserAccountService _accounts;
    private readonly IStorageService _storage;

    public GetProfilePhotoQueryHandler(IApplicationDbContext db, ICurrentUserService user,
        IUserAccountService accounts, IStorageService storage)
        => (_db, _user, _accounts, _storage) = (db, user, accounts, storage);

    public async Task<Stream> Handle(GetProfilePhotoQuery request, CancellationToken ct)
    {
        var key = await _db.Therapists.AsNoTracking()
            .Where(t => t.Id == request.TherapistId && t.PhotoStorageKey != null
                && (t.UserId == _user.UserId || t.IsPublished && t.City != null && t.PracticeName != null)
                && _accounts.ActiveUserIds().Contains(t.UserId))
            .Select(t => t.PhotoStorageKey).SingleOrDefaultAsync(ct)
            ?? throw new NotFoundException(nameof(TherapistProfile), request.TherapistId);
        try { return await _storage.DownloadAsync(key, ct); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception ex) { throw new StorageUnavailableException(ex); }
    }
}
