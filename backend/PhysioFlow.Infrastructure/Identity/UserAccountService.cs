using Microsoft.AspNetCore.Identity;
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
}
