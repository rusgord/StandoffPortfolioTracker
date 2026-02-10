using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Core.Enums;
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

        // Возвращаем (Success, Message, NewBalance)
        public async Task<(bool Success, string Message, decimal? NewBalance)> BuySubscriptionAsync(string userId, int days, decimal cost, SubscriptionType type)
        {
            using var context = await _factory.CreateDbContextAsync();
            var user = await context.Users.FindAsync(userId);
            if (user == null) return (false, "Пользователь не найден", null);

            // Логика смены тарифа
            if (user.IsPro && user.SubType != type)
            {
                // Понижение (Premium -> Lite) ЗАПРЕЩЕНО
                if (user.SubType == SubscriptionType.Premium && type == SubscriptionType.Lite)
                {
                    return (false, "Сначала дождитесь окончания Premium подписки.", user.Balance);
                }
                // Повышение (Lite -> Premium) РАЗРЕШЕНО -> код идет дальше
            }

            if (user.Balance < cost)
            {
                return (false, "Недостаточно Gold на балансе", user.Balance);
            }

            // 1. Списываем средства
            user.Balance -= cost;

            // 2. Обновляем подписку
            if (user.IsPro && user.SubType == type)
            {
                // Продление того же тарифа
                user.ProExpirationDate = user.ProExpirationDate!.Value.AddDays(days);
            }
            else
            {
                // Новый тариф или Апгрейд (Lite -> Premium)
                // Срок считается от СЕГОДНЯ (старый остаток Lite сгорает при апгрейде)
                user.SubType = type;
                user.ProExpirationDate = DateTime.UtcNow.AddDays(days);
            }

            user.IsAutoRenew = true;

            context.WalletTransactions.Add(new WalletTransaction
            {
                UserId = userId,
                Amount = -cost,
                Description = $"Оплата {type} ({days} дн.)",
                Date = DateTime.UtcNow
            });

            await context.SaveChangesAsync();

            // Возвращаем новый баланс, чтобы обновить шапку мгновенно
            return (true, $"Тариф {type} активирован!", user.Balance);
        }

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
        public async Task SetAutoRenewAsync(string userId, bool isActive)
        {
            using var context = await _factory.CreateDbContextAsync();
            var user = await context.Users.FindAsync(userId);
            if (user != null)
            {
                user.IsAutoRenew = isActive;
                await context.SaveChangesAsync();
            }
        }

        public async Task<List<WalletTransaction>> GetHistoryAsync(string userId)
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.WalletTransactions.AsNoTracking()
                .Where(t => t.UserId == userId).OrderByDescending(t => t.Date).ToListAsync();
        }
    }
}