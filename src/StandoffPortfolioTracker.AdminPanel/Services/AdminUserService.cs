using Microsoft.AspNetCore.Identity;
using StandoffPortfolioTracker.Core.Entities;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class AdminUserService
    {
        private readonly UserManager<ApplicationUser> _userManager;

        public AdminUserService(UserManager<ApplicationUser> userManager)
        {
            _userManager = userManager;
        }

        // ⛔ БАН (Блокировка + Кик)
        public async Task<(bool Success, string Message)> BanUserAsync(string userId, int days = 36500)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return (false, "Пользователь не найден");

            // 1. Ставим блокировку (юзер не сможет войти снова)
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddDays(days));
            await _userManager.SetLockoutEnabledAsync(user, true);

            // 2. 🔥 ВАЖНО: Обновляем SecurityStamp. 
            // Это делает старые куки невалидными.
            await _userManager.UpdateSecurityStampAsync(user);

            return (true, "Пользователь заблокирован и будет выкинут из системы.");
        }

        // ✅ РАЗБАН
        public async Task<(bool Success, string Message)> UnbanUserAsync(string userId)
        {
            var user = await _userManager.FindByIdAsync(userId);
            if (user == null) return (false, "Пользователь не найден");

            await _userManager.SetLockoutEndDateAsync(user, null);
            return (true, "Пользователь разблокирован.");
        }
    }
}