using FluentValidation;
using MediatR;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Exceptions;
using PhysioFlow.Application.Common.Interfaces;
using PhysioFlow.Application.Common.Models;
using PhysioFlow.Application.DTOs;
using PhysioFlow.Application.Features.Cooperation;
using PhysioFlow.Domain.Entities;

namespace PhysioFlow.Application.Features.Notifications;

public record GetNotificationsQuery(int PageNumber = 1, int PageSize = 10) : IRequest<PaginatedList<NotificationDto>>;
public record GetUnreadNotificationCountQuery : IRequest<UnreadNotificationCountDto>;
public record ReadNotificationCommand(Guid Id) : IRequest;
public record ReadAllNotificationsCommand : IRequest;

public class GetNotificationsValidator : AbstractValidator<GetNotificationsQuery>
{
    public GetNotificationsValidator()
    {
        RuleFor(x => x.PageNumber).InclusiveBetween(1, 1_000_000);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}

public class NotificationRequests(IApplicationDbContext db, ICurrentUserService user, IDateTimeProvider clock) :
    IRequestHandler<GetNotificationsQuery, PaginatedList<NotificationDto>>,
    IRequestHandler<GetUnreadNotificationCountQuery, UnreadNotificationCountDto>,
    IRequestHandler<ReadNotificationCommand>, IRequestHandler<ReadAllNotificationsCommand>
{
    private IQueryable<Notification> Own() => db.Notifications.Where(n => n.RecipientId == user.RequireUserId());

    public async Task<PaginatedList<NotificationDto>> Handle(GetNotificationsQuery request, CancellationToken ct)
    {
        var query = Own().AsNoTracking();
        var count = await query.CountAsync(ct);
        var rows = await query.OrderByDescending(n => n.CreatedAt).ThenByDescending(n => n.Id)
            .Skip((request.PageNumber - 1) * request.PageSize).Take(request.PageSize).ToListAsync(ct);
        return new(rows.Select(n => n.ToDto()).ToList(), count, request.PageNumber, request.PageSize);
    }

    public async Task<UnreadNotificationCountDto> Handle(GetUnreadNotificationCountQuery request, CancellationToken ct)
        => new(await Own().CountAsync(n => n.ReadAt == null, ct));

    public async Task Handle(ReadNotificationCommand request, CancellationToken ct)
    {
        if (!await Own().AnyAsync(n => n.Id == request.Id, ct))
            throw new NotFoundException(nameof(Notification), request.Id);
        await MarkReadAsync(Own().Where(n => n.Id == request.Id), ct);
    }

    public Task Handle(ReadAllNotificationsCommand request, CancellationToken ct) => MarkReadAsync(Own(), ct);

    private async Task MarkReadAsync(IQueryable<Notification> query, CancellationToken ct)
    {
        var unread = query.Where(n => n.ReadAt == null);
        var now = clock.UtcNow;
        if (db.Database.IsRelational())
        {
            // Conditional update preserves the first read time, including concurrent requests.
            await unread.ExecuteUpdateAsync(s => s.SetProperty(n => n.ReadAt, (DateTime?)now), ct);
        }
        else
        {
            foreach (var row in await unread.ToListAsync(ct)) row.ReadAt = now;
            await db.SaveChangesAsync(ct);
        }
    }
}
