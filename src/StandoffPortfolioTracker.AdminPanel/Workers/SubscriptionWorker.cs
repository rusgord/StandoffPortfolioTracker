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
            _logger.LogInformation("SubscriptionWorker запущен.");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        // Передаем токен, чтобы операция БД тоже прерывалась при остановке
                        await ProcessSubscriptions(stoppingToken);
                    }
                    catch (Exception ex) when (ex is not OperationCanceledException)
                    {
                        // Логируем только реальные ошибки, а не отмену задачи
                        _logger.LogError(ex, "Ошибка при обработке подписок");
                    }

                    // Ждем 1 час или пока не поступит сигнал остановки
                    await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Это нормальное завершение работы при остановке приложения
                _logger.LogInformation("SubscriptionWorker остановлен (Task Canceled).");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Критическая ошибка в SubscriptionWorker");
            }
        }

        private async Task ProcessSubscriptions(CancellationToken ct)
        {
            using var scope = _serviceProvider.CreateScope();
            // Используем Factory или получаем контекст, который поддерживает Scoped
            // В BackgroundService лучше создавать Scope вручную, как у вас и сделано
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 1. Ищем тех, у кого подписка истекла + автопродление
            var expiredUsers = await context.Users
                .Where(u => u.ProExpirationDate != null
                            && u.ProExpirationDate < DateTime.UtcNow
                            && u.SubType != SubscriptionType.None
                            && u.IsAutoRenew)
                .ToListAsync(ct); // <-- Передаем токен

            foreach (var user in expiredUsers)
            {
                // Проверяем отмену перед каждой итерацией (если пользователей много)
                if (ct.IsCancellationRequested) return;

                decimal cost = user.SubType == SubscriptionType.Premium ? 250 : 150;

                if (user.Balance >= cost)
                {
                    user.Balance -= cost;
                    // Продлеваем от текущего момента, если просрочена, или добавляем к дате, если логика другая. 
                    // В вашем коде было DateTime.UtcNow.AddDays(30), оставим так.
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
                    user.SubType = SubscriptionType.None;
                    user.ProExpirationDate = null;
                    user.IsAutoRenew = false;

                    _logger.LogInformation($"Отмена подписки (нет средств) для {user.UserName}");
                }
            }

            // 2. Ищем тех, у кого истекла подписка и автопродление ВЫКЛЮЧЕНО
            var cancelledUsers = await context.Users
                .Where(u => u.ProExpirationDate != null
                            && u.ProExpirationDate < DateTime.UtcNow
                            && !u.IsAutoRenew)
                .ToListAsync(ct); // <-- Передаем токен

            foreach (var user in cancelledUsers)
            {
                if (ct.IsCancellationRequested) return;

                user.SubType = SubscriptionType.None;
                user.ProExpirationDate = null;
            }

            // Сохраняем все изменения разом
            await context.SaveChangesAsync(ct); // <-- Передаем токен
        }
    }
}