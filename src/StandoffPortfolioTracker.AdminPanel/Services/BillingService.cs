using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class BillingService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public BillingService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        // 1. Покупка PRO подписки
        public async Task<(bool Success, string Message)> BuyProSubscriptionAsync(string userId, int days, decimal cost)
        {
            using var context = await _factory.CreateDbContextAsync();

            var user = await context.Users.FindAsync(userId);
            if (user == null) return (false, "Пользователь не найден");

            if (user.Balance < cost)
            {
                return (false, "Недостаточно Gold на балансе");
            }

            // Списываем баланс
            user.Balance -= cost;

            // Продлеваем подписку
            if (user.IsPro)
            {
                // Если уже есть - добавляем к текущей дате
                user.ProExpirationDate = user.ProExpirationDate!.Value.AddDays(days);
            }
            else
            {
                // Если нет - ставим от текущего момента
                user.ProExpirationDate = DateTime.UtcNow.AddDays(days);
            }

            // Записываем в историю
            context.WalletTransactions.Add(new WalletTransaction
            {
                UserId = userId,
                Amount = -cost,
                Description = $"Покупка PRO на {days} дней",
                Date = DateTime.UtcNow
            });

            await context.SaveChangesAsync();
            return (true, $"Успешно! PRO продлен до {user.ProExpirationDate.Value:d}");
        }

        // 2. Начисление баланса (админом или системой оплаты)
        public async Task AddBalanceAsync(string userId, decimal amount, string reason)
        {
            using var context = await _factory.CreateDbContextAsync();
            var user = await context.Users.FindAsync(userId);
            if (user != null)
            {
                user.Balance += amount;
                context.WalletTransactions.Add(new WalletTransaction
                {
                    UserId = userId,
                    Amount = amount,
                    Description = reason,
                    Date = DateTime.UtcNow
                });
                await context.SaveChangesAsync();
            }
        }

        // 3. Получить историю операций
        public async Task<List<WalletTransaction>> GetHistoryAsync(string userId)
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.WalletTransactions
                .AsNoTracking()
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.Date)
                .ToListAsync();
        }
    }
}