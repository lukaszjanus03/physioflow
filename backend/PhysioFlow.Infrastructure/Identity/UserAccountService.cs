using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using PhysioFlow.Application.Common.Interfaces;

namespace PhysioFlow.Infrastructure.Identity;

public class UserAccountService : IUserAccountService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserAccountService(UserManager<ApplicationUser> userManager) => _userManager = userManager;

    public async Task<UserAccountInfo?> FindAsync(Guid userId, CancellationToken ct)
    {
        var user = await _userManager.FindByIdAsync(userId.ToString());
        return user is null ? null : new UserAccountInfo(user.Id, user.FirstName, user.LastName, user.Email!);
    }

    public IQueryable<Guid> ActiveUserIds() =>
        _userManager.Users.Where(u => u.IsActive).Select(u => u.Id);

    public async Task<bool> IsConfirmedPatientAsync(Guid userId, CancellationToken ct)
    {
        var account = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        return account is { IsActive: true, EmailConfirmed: true }
            && await _userManager.IsInRoleAsync(account, "Patient");
    }

    public async Task<bool> IsReviewModeratorAsync(Guid userId, CancellationToken ct)
    {
        var account = await _userManager.Users.SingleOrDefaultAsync(u => u.Id == userId, ct);
        return account is { IsActive: true } && await _userManager.IsInRoleAsync(account, "Admin");
    }

    public IQueryable<ReviewAuthorName> ReviewAuthorNames() =>
        _userManager.Users.Select(u => new ReviewAuthorName { Id = u.Id, FirstName = u.FirstName, LastName = u.LastName });
}
