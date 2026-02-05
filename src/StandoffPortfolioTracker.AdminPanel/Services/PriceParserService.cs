using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;
using System.Globalization;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class PriceParserService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly HttpClient _httpClient;

        public PriceParserService(IDbContextFactory<AppDbContext> factory, HttpClient httpClient)
        {
            _factory = factory;
            _httpClient = httpClient;

            // Притворяемся обычным браузером, чтобы сайт нас не заблокировал
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        public async Task<string> UpdateAllPricesAsync()
        {
            using var context = await _factory.CreateDbContextAsync();
            var items = await context.ItemBases.ToListAsync();

            int successCount = 0;

            foreach (var item in items)
            {
                try
                {
                    // 1. Формируем полное имя для запроса
                    var fullName = $"{item.Name} {item.SkinName}".Trim();

                    // ВАЖНО: Кодируем название для URL (пробелы превращаются в %20 и т.д.)
                    var encodedName = Uri.EscapeDataString(fullName);

                    // 2. Стучимся напрямую в их API
                    var url = $"https://standoff-2.com/skins-new.php?command=getStat&name={encodedName}";

                    // 3. Получаем данные (массив объектов)
                    var history = await _httpClient.GetFromJsonAsync<List<PriceDataDto>>(url);

                    if (history != null && history.Any())
                    {
                        // Берем последнюю запись (самую свежую)
                        var lastEntry = history.Last();

                        // Парсим цену (она может прийти строкой или числом)
                        if (decimal.TryParse(lastEntry.PurchasePrice, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                        {
                            item.CurrentMarketPrice = price;
                            successCount++;
                        }
                    }

                    // Небольшая задержка, чтобы не забанили IP
                    await Task.Delay(500);
                }
                catch (Exception ex)
                {
                    // Логируем ошибку, но не останавливаемся
                    Console.WriteLine($"Ошибка с {item.Name}: {ex.Message}");
                }
            }

            await context.SaveChangesAsync();
            return $"Обновлено: {successCount}";
        }

        // Вспомогательный класс для их JSON ответа
        public class PriceDataDto
        {
            public string Date { get; set; } // "2025-02-04 12:00:00"

            [System.Text.Json.Serialization.JsonPropertyName("purchase_price")]
            public string PurchasePrice { get; set; }
        }
    }
}