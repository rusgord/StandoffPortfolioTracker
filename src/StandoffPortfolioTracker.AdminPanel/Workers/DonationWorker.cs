using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;
using StandoffPortfolioTracker.AdminPanel.Services; // Добавляем для GlobalNotifier (опционально)

namespace StandoffPortfolioTracker.AdminPanel.Workers
{
    public class DonationWorker : BackgroundService
    {
        private readonly IServiceProvider _serviceProvider;
        private readonly ILogger<DonationWorker> _logger;
        private readonly IConfiguration _configuration;
        private readonly HttpClient _httpClient;

        public DonationWorker(IServiceProvider serviceProvider, ILogger<DonationWorker> logger, IConfiguration configuration)
        {
            _serviceProvider = serviceProvider;
            _logger = logger;
            _configuration = configuration;
            _httpClient = new HttpClient();
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("💰 DonationWorker запущен.");

            try
            {
                while (!stoppingToken.IsCancellationRequested)
                {
                    try
                    {
                        await CheckDonationsAsync(stoppingToken);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Ошибка при проверке донатов");
                    }

                    // Ждем 1 минуту. Если сервер остановится, Task.Delay выбросит TaskCanceledException,
                    // который будет перехвачен внешним блоком catch.
                    await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
                }
            }
            catch (OperationCanceledException)
            {
                // Штатное завершение работы
                _logger.LogInformation("DonationWorker остановлен.");
            }
        }

        private async Task CheckDonationsAsync(CancellationToken stoppingToken)
        {
            var token = _configuration["DonationAlerts:AccessToken"];
            if (string.IsNullOrEmpty(token)) return;

            // 1. Запрос к API DonationAlerts (с токеном отмены!)
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Используем stoppingToken, чтобы не зависать при выключении
            var response = await _httpClient.GetAsync("https://www.donationalerts.com/api/v1/alerts/donations", stoppingToken);

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Ошибка API DA: {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync(stoppingToken);
            using var doc = JsonDocument.Parse(json);

            // Проверка на наличие свойства data, чтобы не падать при пустом ответе
            if (!doc.RootElement.TryGetProperty("data", out var donations))
            {
                return;
            }

            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // Опционально: можно получить GlobalNotifier, чтобы уведомить юзера в реальном времени
            var notifier = scope.ServiceProvider.GetService<GlobalNotificationService>();

            foreach (var donation in donations.EnumerateArray())
            {
                // Прерываем цикл, если сервер останавливается
                if (stoppingToken.IsCancellationRequested) break;

                var externalId = donation.GetProperty("id").GetInt32().ToString();
                var amount = donation.GetProperty("amount").GetDecimal();
                var currency = donation.GetProperty("currency").GetString();
                var message = donation.GetProperty("message").GetString() ?? "";
                var username = donation.GetProperty("username").GetString() ?? "Аноним";

                if (currency != "RUB") continue;

                // 3. Проверяем дубликаты (с токеном)
                var exists = await context.WalletTransactions
                    .AnyAsync(t => t.ExternalTransactionId == externalId && t.ExternalSystem == "DonationAlerts", stoppingToken);

                if (exists) continue;

                var targetUserId = FindUserIdInMessage(message);

                if (string.IsNullOrEmpty(targetUserId))
                {
                    // Логируем только новые необработанные, чтобы не спамить в лог при каждом цикле
                    // (лучше добавить проверку даты доната, чтобы не проверять старые)
                    continue;
                }

                var user = await context.Users.FindAsync(new object[] { targetUserId }, stoppingToken);
                if (user == null) continue;

                // 5. НАЧИСЛЕНИЕ
                decimal goldAmount = amount; // 1 к 1
                user.Balance += goldAmount;

                context.WalletTransactions.Add(new WalletTransaction
                {
                    UserId = targetUserId,
                    Amount = goldAmount,
                    Description = $"Пополнение через DonationAlerts от {username}",
                    Date = DateTime.UtcNow,
                    ExternalSystem = "DonationAlerts",
                    ExternalTransactionId = externalId
                });

                await context.SaveChangesAsync(stoppingToken);
                _logger.LogInformation($"✅ Зачислено {goldAmount}G пользователю {user.UserName} (Донат {externalId})");

                // Уведомляем пользователя онлайн!
                if (notifier != null)
                {
                    notifier.NotifyUser(targetUserId, $"Вам зачислено {goldAmount:N0} G!", ToastLevel.Success);
                }
            }
        }

        private string? FindUserIdInMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;
            var words = message.Split(new[] { ' ', '\n', ',', ':' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                if (Guid.TryParse(word, out _))
                {
                    return word;
                }
            }
            return null;
        }
    }
}