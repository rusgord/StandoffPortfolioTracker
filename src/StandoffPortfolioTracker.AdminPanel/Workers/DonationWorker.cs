using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;

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

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await CheckDonationsAsync();
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка при проверке донатов");
                }

                // Ждем 1 минуту перед следующей проверкой
                await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
            }
        }

        private async Task CheckDonationsAsync()
        {
            var token = _configuration["DonationAlerts:AccessToken"];
            if (string.IsNullOrEmpty(token)) return; // Если токен не настроен, пропускаем

            // 1. Запрос к API DonationAlerts
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var response = await _httpClient.GetAsync("https://www.donationalerts.com/api/v1/alerts/donations");

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning($"Ошибка API DA: {response.StatusCode}");
                return;
            }

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            var donations = doc.RootElement.GetProperty("data");

            // Создаем Scope, так как BackgroundService - Singleton, а DbContext - Scoped
            using var scope = _serviceProvider.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            // 2. Перебираем последние донаты
            foreach (var donation in donations.EnumerateArray())
            {
                var externalId = donation.GetProperty("id").GetInt32().ToString(); // ID доната в DA
                var amount = donation.GetProperty("amount").GetDecimal();
                var currency = donation.GetProperty("currency").GetString();
                var message = donation.GetProperty("message").GetString() ?? ""; // Сообщение пользователя
                var username = donation.GetProperty("username").GetString() ?? "Аноним";

                // Пропускаем, если валюта не RUB (для простоты, или добавьте конвертацию)
                if (currency != "RUB") continue;

                // 3. Проверяем, был ли этот донат уже обработан
                var exists = await context.WalletTransactions
                    .AnyAsync(t => t.ExternalTransactionId == externalId && t.ExternalSystem == "DonationAlerts");

                if (exists) continue; // Уже выдали, идем дальше

                // 4. Ищем ID пользователя в сообщении
                // Предполагаем, что юзер вставил свой GUID ID в сообщение
                var targetUserId = FindUserIdInMessage(message);

                if (string.IsNullOrEmpty(targetUserId))
                {
                    _logger.LogWarning($"Донат {externalId} от {username} на {amount}р не содержит ID пользователя. Сообщение: {message}");
                    continue;
                }

                var user = await context.Users.FindAsync(targetUserId);
                if (user == null) continue; // Юзер с таким ID не найден

                // 5. НАЧИСЛЕНИЕ
                // Курс 1 Рубль = 1 Голда (можно изменить)
                decimal goldAmount = amount;

                user.Balance += goldAmount;

                // 6. Сохраняем транзакцию, чтобы не начислить дважды
                context.WalletTransactions.Add(new WalletTransaction
                {
                    UserId = targetUserId,
                    Amount = goldAmount,
                    Description = $"Пополнение через DonationAlerts от {username}",
                    Date = DateTime.UtcNow,
                    ExternalSystem = "DonationAlerts",
                    ExternalTransactionId = externalId
                });

                await context.SaveChangesAsync();
                _logger.LogInformation($"✅ Зачислено {goldAmount}G пользователю {user.UserName} (Донат {externalId})");
            }
        }

        // Простая логика поиска ID (GUID) в тексте
        private string? FindUserIdInMessage(string message)
        {
            if (string.IsNullOrWhiteSpace(message)) return null;

            // Разбиваем текст на слова и ищем то, что похоже на GUID
            var words = message.Split(new[] { ' ', '\n', ',', ':' }, StringSplitOptions.RemoveEmptyEntries);
            foreach (var word in words)
            {
                // Простая проверка на GUID (36 символов, содержит дефисы)
                if (Guid.TryParse(word, out _))
                {
                    return word;
                }
            }
            return null;
        }
    }
}