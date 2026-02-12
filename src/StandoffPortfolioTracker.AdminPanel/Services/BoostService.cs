using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Core.Enums;
using StandoffPortfolioTracker.Infrastructure;
using System.Text.Json;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class BoostService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly IWebHostEnvironment _env;
        private readonly GlobalNotificationService _notifier;
        private const string CacheFile = "data/boosts_cache.json";

        public BoostService(IDbContextFactory<AppDbContext> factory, IWebHostEnvironment env, GlobalNotificationService notifier)
        {
            _factory = factory;
            _env = env;
            _notifier = notifier;
        }

        public class BoostItemDto
        {
            public int ItemId { get; set; }
            public string Name { get; set; }
            public string SkinName { get; set; }
            public string ImageUrl { get; set; }
            public decimal OldPrice { get; set; } // Теперь это средняя цена за период
            public decimal NewPrice { get; set; }
            public double PercentGrowth { get; set; }
            public DateTime DetectedAt { get; set; }

            // Item metadata
            public ItemRarity Rarity { get; set; }
            public string CollectionName { get; set; }
            public string CollectionImageUrl { get; set; }
            public bool IsStatTrack { get; set; }
            public bool IsPattern { get; set; }
        }

        public async Task CheckForBoostsAsync()
        {
            using var context = await _factory.CreateDbContextAsync();

            // Загружаем существующие бусты из кеша
            var path = Path.Combine(_env.WebRootPath, CacheFile);
            var existingBoosts = new Dictionary<int, BoostItemDto>();

            if (File.Exists(path))
            {
                try
                {
                    var cachedJson = await File.ReadAllTextAsync(path);
                    var cached = JsonSerializer.Deserialize<List<BoostItemDto>>(cachedJson) ?? new();
                    existingBoosts = cached.ToDictionary(b => b.ItemId, b => b);
                }
                catch { /* Игнорируем ошибки при чтении кеша */ }
            }

            // 1. Берем ВСЕ предметы (не только обновленные за 30 минут)
            // Так мы сохраним цены для уже найденных бустов
            var allItems = await context.ItemBases
                .Where(i => i.Rarity != ItemRarity.Nameless
                        && i.CurrentMarketPrice < 50000
                        && i.CurrentMarketPrice > 1)
                .ToListAsync();

            var boosts = new List<BoostItemDto>();
            var checkPeriod = DateTime.UtcNow.AddDays(-2);

            foreach (var item in allItems)
            {
                // 2. Проверяем, есть ли этот предмет в текущих бустах
                if (existingBoosts.TryGetValue(item.Id, out var existingBoost))
                {
                    // Обновляем цену для уже найденного буста
                    existingBoost.NewPrice = item.CurrentMarketPrice;
                    existingBoost.Rarity = item.Rarity;
                    existingBoost.CollectionName = item.Collection?.Name ?? "Unknown";
                    existingBoost.CollectionImageUrl = item.Collection?.ImageUrl;
                    existingBoost.IsStatTrack = item.IsStatTrack;
                    existingBoost.IsPattern = item.IsPattern;
                    boosts.Add(existingBoost);
                    continue;
                }

                // Для новых потенциальных бустов проверяем только недавно обновленные предметы
                if (item.LastUpdate < DateTime.UtcNow.AddMinutes(-30))
                    continue;

                // 3. Вычисляем среднюю цену из истории за указанный период
                var historyPrices = await context.MarketHistory
                    .Where(h => h.ItemBaseId == item.Id && h.RecordedAt >= checkPeriod)
                    .Select(h => h.Price)
                    .ToListAsync();

                if (historyPrices.Count < 5) continue;

                decimal avgPrice = historyPrices.Average();
                if (avgPrice <= 0) continue;

                // 4. Считаем процент отклонения текущей цены от средней
                var growth = (double)((item.CurrentMarketPrice - avgPrice) / avgPrice) * 100;

                // Порог буста: например, 20% выше среднего
                if (growth >= 20)
                {
                    var boostDto = new BoostItemDto
                    {
                        ItemId = item.Id,
                        Name = item.Name,
                        SkinName = item.SkinName,
                        ImageUrl = item.ImageUrl,
                        OldPrice = Math.Round(avgPrice, 2),
                        NewPrice = item.CurrentMarketPrice,
                        PercentGrowth = Math.Round(growth, 1),
                        DetectedAt = DateTime.UtcNow,
                        Rarity = item.Rarity,
                        CollectionName = item.Collection?.Name ?? "Unknown",
                        CollectionImageUrl = item.Collection?.ImageUrl,
                        IsStatTrack = item.IsStatTrack,
                        IsPattern = item.IsPattern
                    };
                    boosts.Add(boostDto);

                    // Уведомляем владельцев через GlobalNotificationService
                    await NotifyOwners(context, item, growth);
                }
            }

            // 5. Удаляем бусты, цена которых вернулась к норме (< 15% выше средней)
            boosts = boosts.Where(boost =>
            {
                var isStillBoost = (double)((boost.NewPrice - boost.OldPrice) / boost.OldPrice) * 100 >= 15;
                return isStillBoost;
            }).ToList();

            // 6. Сохраняем результат в JSON-файл
            var directory = Path.GetDirectoryName(path);
            if (!Directory.Exists(directory)) Directory.CreateDirectory(directory);

            var json = JsonSerializer.Serialize(boosts.OrderByDescending(x => x.PercentGrowth));
            await File.WriteAllTextAsync(path, json);
        }

        private async Task NotifyOwners(AppDbContext context, ItemBase item, double growth)
        {
            var owners = await context.InventoryItems
                .Include(i => i.PortfolioAccount)
                    .ThenInclude(p => p.User)
                .Where(i => i.ItemBaseId == item.Id
                            && i.PortfolioAccount.User.SubType == SubscriptionType.Premium)
                .Select(i => i.PortfolioAccount.User.Id)
                .Distinct()
                .ToListAsync();

            foreach (var userId in owners)
            {
                _notifier.NotifyUser(userId, $"🔥 Буст! {item.Name} {item.SkinName} вырос на {growth:N0}% выше средней цены!", ToastLevel.Warning);
            }
        }

        public async Task<List<BoostItemDto>> GetBoostsAsync()
        {
            var path = Path.Combine(_env.WebRootPath, CacheFile);
            if (!File.Exists(path)) return new List<BoostItemDto>();

            try
            {
                var json = await File.ReadAllTextAsync(path);
                return JsonSerializer.Deserialize<List<BoostItemDto>>(json) ?? new List<BoostItemDto>();
            }
            catch { return new List<BoostItemDto>(); }
        }
    }
}