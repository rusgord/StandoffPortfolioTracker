using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using StandoffPortfolioTracker.Core.Entities;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    /// <summary>
    /// Сервис для сохранения и чтения истории цен из файлов
    /// Вместо БД используем JSON файлы для оптимизации
    /// </summary>
    public class PriceHistoryFileService
    {
        private readonly string _priceHistoryPath;
        private readonly ILogger<PriceHistoryFileService> _logger;

        public record PriceHistoryEntry(DateTime Date, decimal Price, decimal Change, decimal ChangePercent);

        public PriceHistoryFileService(ILogger<PriceHistoryFileService> logger)
        {
            _logger = logger;
            _priceHistoryPath = Path.Combine(Directory.GetCurrentDirectory(), "wwwroot", "data", "price-history");
            
            // Создаём директорию если не существует
            Directory.CreateDirectory(_priceHistoryPath);
        }

        /// <summary>
        /// Сохраняет историю цен для предмета
        /// </summary>
        public async Task SavePriceHistoryAsync(int itemId, decimal currentPrice, ItemBase? item = null)
        {
            try
            {
                var filePath = GetHistoryFilePath(itemId);
                var history = await LoadHistoryAsync(itemId);

                // Получаем последнюю цену
                var lastEntry = history.LastOrDefault();
                var change = currentPrice - (lastEntry?.Price ?? currentPrice);
                var changePercent = lastEntry?.Price > 0 ? (change / lastEntry.Price) * 100 : 0;

                // Добавляем новую запись с сегодняшней датой (только если цена отличается)
                var today = DateTime.UtcNow.Date;
                var todayEntry = history.FirstOrDefault(h => h.Date.Date == today);

                if (todayEntry == null)
                {
                    // Новая запись за день
                    history.Add(new PriceHistoryEntry(
                        today,
                        currentPrice,
                        change,
                        changePercent
                    ));
                }
                else if (todayEntry.Price != currentPrice)
                {
                    // Обновляем запись сегодня
                    history.Remove(todayEntry);
                    history.Add(new PriceHistoryEntry(
                        today,
                        currentPrice,
                        change,
                        changePercent
                    ));
                }

                // Сохраняем в файл
                await WriteHistoryAsync(filePath, history);
                _logger.LogInformation($"Сохранена история цен для предмета {itemId}: {currentPrice}G");
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка при сохранении истории цен: {ex.Message}");
            }
        }

        /// <summary>
        /// Получает историю цен для предмета за указанный период
        /// </summary>
        public async Task<List<(DateTime Date, decimal Price)>> GetPriceHistoryAsync(int itemId, int days = 90)
        {
            try
            {
                var history = await LoadHistoryAsync(itemId);
                var cutoffDate = DateTime.UtcNow.AddDays(-days).Date;

                return history
                    .Where(h => h.Date.Date >= cutoffDate)
                    .Select(h => (h.Date, h.Price))
                    .ToList();
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка при чтении истории цен: {ex.Message}");
                return new List<(DateTime, decimal)>();
            }
        }

        /// <summary>
        /// Получает информацию о дневном изменении цены
        /// </summary>
        public async Task<(decimal Change, decimal ChangePercent)> GetDailyChangeAsync(int itemId)
        {
            try
            {
                var history = await LoadHistoryAsync(itemId);
                if (history.Count < 2) return (0, 0);

                var today = history.FirstOrDefault(h => h.Date.Date == DateTime.UtcNow.Date);
                var yesterday = history.Where(h => h.Date.Date < DateTime.UtcNow.Date).LastOrDefault();

                if (today == null || yesterday == null)
                    return (0, 0);

                var change = today.Price - yesterday.Price;
                var changePercent = yesterday.Price > 0 ? (change / yesterday.Price) * 100 : 0;

                return (change, changePercent);
            }
            catch
            {
                return (0, 0);
            }
        }

        /// <summary>
        /// Получает статистику по предмету
        /// </summary>
        public async Task<(decimal Min, decimal Max, decimal Avg)> GetStatsAsync(int itemId, int days = 90)
        {
            try
            {
                var history = await GetPriceHistoryAsync(itemId, days);
                if (!history.Any()) return (0, 0, 0);

                return (
                    history.Min(h => h.Price),
                    history.Max(h => h.Price),
                    (decimal)history.Average(h => (double)h.Price)
                );
            }
            catch
            {
                return (0, 0, 0);
            }
        }

        /// <summary>
        /// Очищает старую историю (старше 180 дней)
        /// </summary>
        public async Task CleanupOldHistoryAsync()
        {
            try
            {
                var cutoffDate = DateTime.UtcNow.AddDays(-180);
                var files = Directory.GetFiles(_priceHistoryPath, "*.json");

                foreach (var filePath in files)
                {
                    var history = await ReadHistoryAsync(filePath);
                    var filtered = history.Where(h => h.Date.Date >= cutoffDate.Date).ToList();

                    if (filtered.Count != history.Count)
                    {
                        await WriteHistoryAsync(filePath, filtered);
                        _logger.LogInformation($"Очищена история в файле: {Path.GetFileName(filePath)}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка при очистке истории: {ex.Message}");
            }
        }

        // === Private Helpers ===

        private string GetHistoryFilePath(int itemId) => 
            Path.Combine(_priceHistoryPath, $"item_{itemId}.json");

        private async Task<List<PriceHistoryEntry>> LoadHistoryAsync(int itemId)
        {
            var filePath = GetHistoryFilePath(itemId);
            
            if (!File.Exists(filePath))
                return new List<PriceHistoryEntry>();

            return await ReadHistoryAsync(filePath);
        }

        private async Task<List<PriceHistoryEntry>> ReadHistoryAsync(string filePath)
        {
            try
            {
                var json = await File.ReadAllTextAsync(filePath);
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                return JsonSerializer.Deserialize<List<PriceHistoryEntry>>(json) ?? new();
            }
            catch
            {
                return new List<PriceHistoryEntry>();
            }
        }

        private async Task WriteHistoryAsync(string filePath, List<PriceHistoryEntry> history)
        {
            try
            {
                var options = new JsonSerializerOptions { WriteIndented = false };
                var json = JsonSerializer.Serialize(history, options);
                await File.WriteAllTextAsync(filePath, json);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Ошибка при записи истории: {ex.Message}");
                throw;
            }
        }
    }
}
