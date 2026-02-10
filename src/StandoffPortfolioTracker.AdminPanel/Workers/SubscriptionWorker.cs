using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Core.Enums;
using StandoffPortfolioTracker.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace StandoffPortfolioTracker.AdminPanel.Workers
{
    public class SubscriptionWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<SubscriptionWorker> _logger;

        public SubscriptionWorker(IServiceProvider serviceProvider, ILogger<SubscriptionWorker> logger)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await ProcessSubscriptions();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при обработке подписок");
                }

                // Проверяем раз в час
                await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            }
        }

        private async Task ProcessSubscriptions()
        {
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 1. Ищем тех, у кого подписка истекла (или истекает в течение часа) И включено автопродление
            var expiredUsers = await context.Users
                .Where(u => u.ProExpirationDate != null
                            && u.ProExpirationDate < DateTime.UtcNow
                            && u.SubType != SubscriptionType.None
                            && u.IsAutoRenew) // Только если включено автопродление
                .ToListAsync();

            foreach (var user in expiredUsers)
            {
                // Определяем цену продления (на 30 дней)
                decimal cost = user.SubType == SubscriptionType.Premium ? 250 : 150;

                if (user.Balance >= cost)
                {
                    // Списываем и продлеваем
                    user.Balance -= cost;
                    user.ProExpirationDate = DateTime.UtcNow.AddDays(30);

                    context.WalletTransactions.Add(new WalletTransaction
                    {
                        UserId = user.Id,
                        Amount = -cost,
                        Description = $"Автопродление {user.SubType} (30 дней)",
                        Date = DateTime.UtcNow
                    });

                    _logger.LogInformation($"Успешное автопродление для {user.UserName}");
                }
                else
                {
                    // Денег нет — снимаем подписку
                    user.SubType = SubscriptionType.None;
                    user.ProExpirationDate = null;
                    user.IsAutoRenew = false; // Выключаем попытки

                    _logger.LogInformation($"Отмена подписки (нет средств) для {user.UserName}");
                }
            }

            // 2. Ищем тех, у кого истекла подписка, но автопродление ВЫКЛЮЧЕНО
            var cancelledUsers = await context.Users
                .Where(u => u.ProExpirationDate != null
                            && u.ProExpirationDate < DateTime.UtcNow
                            && !u.IsAutoRenew)
                .ToListAsync();

            foreach (var user in cancelledUsers)
            {
                user.SubType = SubscriptionType.None;
                user.ProExpirationDate = null;
            }

            await context.SaveChangesAsync();
        }
    }
}