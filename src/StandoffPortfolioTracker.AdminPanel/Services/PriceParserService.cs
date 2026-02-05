using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;
using System.Collections.Concurrent;
using System.Globalization;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

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

            // Притворяемся браузером
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        // ==========================================
        // 1. ИМПОРТ ВСЕХ СКИНОВ (С Созданием ST версий)
        // ==========================================
        public async Task<string> ImportAllSkinsAsync()
        {
            var url = "https://standoff-2.com/skins-new.php?command=getModelInfo";
            var skinsFromSite = await _httpClient.GetFromJsonAsync<List<SkinDto>>(url);

            if (skinsFromSite == null) return "Ошибка: сайт вернул пустой список.";

            using var context = await _factory.CreateDbContextAsync();

            var existingItemsList = await context.ItemBases.ToListAsync();
            var existingItemsDict = existingItemsList
                .GroupBy(i => $"{i.Name}|{i.SkinName ?? ""}|{i.IsStatTrack}")
                .ToDictionary(g => g.Key, g => g.First());

            var existingCollections = await context.GameCollections.ToDictionaryAsync(c => c.Name, c => c);

            int addedCount = 0;
            int updatedCount = 0;

            foreach (var skinDto in skinsFromSite)
            {
                // Чистим имя
                string cleanFullName = skinDto.FullName.Replace("StatTrack", "").Trim();
                string name = cleanFullName;
                string? skinName = null;

                var match = Regex.Match(cleanFullName, "(.+?)\\s+\"(.+?)\"");
                if (match.Success)
                {
                    name = match.Groups[1].Value.Trim();
                    skinName = match.Groups[2].Value.Trim();
                }
                else if (cleanFullName.Contains("\""))
                {
                    name = cleanFullName.Replace("\"", "").Trim();
                }

                var type = ParseType(skinDto.Type);
                var rarity = ParseRarity(skinDto.Rarity);

                // Коллекция
                GameCollection? collection = null;
                if (!string.IsNullOrEmpty(skinDto.Collection) && skinDto.Collection != "unknown")
                {
                    if (!existingCollections.TryGetValue(skinDto.Collection, out collection))
                    {
                        collection = new GameCollection { Name = skinDto.Collection };
                        context.GameCollections.Add(collection);
                        await context.SaveChangesAsync();
                        existingCollections[skinDto.Collection] = collection;
                    }
                }

                // Генерируем варианты (Обычный + ST)
                var variantsToAdd = new List<bool>();

                if (skinDto.FullName.Contains("StatTrack"))
                {
                    variantsToAdd.Add(true);
                }
                else
                {
                    variantsToAdd.Add(false);
                    if (type == StandoffPortfolioTracker.Core.Enums.ItemType.Skin ||
                        type == StandoffPortfolioTracker.Core.Enums.ItemType.Knife)
                    {
                        variantsToAdd.Add(true);
                    }
                }

                foreach (var isStatTrack in variantsToAdd)
                {
                    var uniqueKey = $"{name}|{skinName ?? ""}|{isStatTrack}";

                    string parserName;
                    if (isStatTrack)
                        parserName = skinDto.FullName.Contains("StatTrack") ? skinDto.FullName : $"StatTrack {skinDto.FullName}";
                    else
                        parserName = skinDto.FullName;

                    if (existingItemsDict.TryGetValue(uniqueKey, out var existingItem))
                    {
                        if (existingItem.OriginalName != parserName || existingItem.ImageUrl != skinDto.ImageUrl)
                        {
                            existingItem.OriginalName = parserName;
                            existingItem.ImageUrl = skinDto.ImageUrl;
                            updatedCount++;
                        }
                    }
                    else
                    {
                        var newItem = new ItemBase
                        {
                            Name = name,
                            SkinName = skinName,
                            OriginalName = parserName,
                            IsStatTrack = isStatTrack,
                            Rarity = rarity,
                            Type = type,
                            CollectionId = collection?.Id ?? 1,
                            ImageUrl = skinDto.ImageUrl,
                            CurrentMarketPrice = 0
                        };

                        context.ItemBases.Add(newItem);
                        existingItemsDict[uniqueKey] = newItem;
                        addedCount++;
                    }
                }
            }

            await context.SaveChangesAsync();
            return $"База обновлена! Создано: {addedCount}. Обновлено: {updatedCount}";
        }

        // ==========================================
        // 2. ОБНОВЛЕНИЕ ВСЕХ ЦЕН (Многопоточное)
        // ==========================================
        public async Task<string> UpdateAllPricesAsync()
        {
            using var context = await _factory.CreateDbContextAsync();
            var items = await context.ItemBases.ToListAsync();

            int successCount = 0;
            int errorCount = 0;
            var historyBag = new ConcurrentBag<MarketHistory>();
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };

            await Parallel.ForEachAsync(items, parallelOptions, async (item, token) =>
            {
                try
                {
                    var fullName = !string.IsNullOrEmpty(item.OriginalName) ? item.OriginalName : $"{item.Name} {item.SkinName}".Trim();
                    var url = $"https://standoff-2.com/skins-new.php?command=getStat&name={Uri.EscapeDataString(fullName)}";
                    var history = await _httpClient.GetFromJsonAsync<List<PriceDataDto>>(url, token);

                    if (history != null && history.Any())
                    {
                        var lastEntry = history.Last();
                        string cleanPrice = lastEntry.PurchasePrice.Replace(",", ".");

                        if (decimal.TryParse(cleanPrice, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                        {
                            item.CurrentMarketPrice = price;
                            historyBag.Add(new MarketHistory { ItemBaseId = item.Id, Price = price, RecordedAt = DateTime.UtcNow });
                            Interlocked.Increment(ref successCount);
                        }
                    }
                }
                catch
                {
                    Interlocked.Increment(ref errorCount);
                }
            });

            context.MarketHistory.AddRange(historyBag);
            await context.SaveChangesAsync();
            return $"Готово! Обновлено: {successCount}. Ошибок: {errorCount}";
        }

        // ==========================================
        // 3. ОБНОВЛЕНИЕ ПОРТФЕЛЯ (Только купленное)
        // ==========================================
        public async Task<string> UpdatePortfolioPricesAsync()
        {
            using var context = await _factory.CreateDbContextAsync();
            var myItemIds = await context.InventoryItems.Select(i => i.ItemBaseId).Distinct().ToListAsync();
            var myItems = await context.ItemBases.Where(i => myItemIds.Contains(i.Id)).ToListAsync();

            int successCount = 0;
            var historyBag = new ConcurrentBag<MarketHistory>();

            await Parallel.ForEachAsync(myItems, new ParallelOptions { MaxDegreeOfParallelism = 5 }, async (item, token) =>
            {
                try
                {
                    var fullName = !string.IsNullOrEmpty(item.OriginalName) ? item.OriginalName : $"{item.Name} {item.SkinName}".Trim();
                    var url = $"https://standoff-2.com/skins-new.php?command=getStat&name={Uri.EscapeDataString(fullName)}";
                    var history = await _httpClient.GetFromJsonAsync<List<PriceDataDto>>(url, token);

                    if (history != null && history.Any())
                    {
                        var priceStr = history.Last().PurchasePrice.Replace(",", ".");
                        if (decimal.TryParse(priceStr, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                        {
                            item.CurrentMarketPrice = price;
                            historyBag.Add(new MarketHistory { ItemBaseId = item.Id, Price = price, RecordedAt = DateTime.UtcNow });
                            Interlocked.Increment(ref successCount);
                        }
                    }
                }
                catch { }
            });

            context.MarketHistory.AddRange(historyBag);
            await context.SaveChangesAsync();
            return $"Портфель обновлен! ({successCount} поз.)";
        }

        // ==========================================
        // 4. ОБНОВЛЕНИЕ ОДНОГО ПРЕДМЕТА
        // ==========================================
        public async Task<bool> UpdateItemPriceAsync(int itemId)
        {
            using var context = await _factory.CreateDbContextAsync();
            var item = await context.ItemBases.FindAsync(itemId);
            if (item == null) return false;

            try
            {
                var fullName = !string.IsNullOrEmpty(item.OriginalName) ? item.OriginalName : $"{item.Name} {item.SkinName}".Trim();
                var url = $"https://standoff-2.com/skins-new.php?command=getStat&name={Uri.EscapeDataString(fullName)}";
                var history = await _httpClient.GetFromJsonAsync<List<PriceDataDto>>(url);

                if (history != null && history.Any())
                {
                    var lastEntry = history.Last();
                    string cleanPrice = lastEntry.PurchasePrice.Replace(",", ".");

                    if (decimal.TryParse(cleanPrice, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                    {
                        item.CurrentMarketPrice = price;
                        context.MarketHistory.Add(new MarketHistory { ItemBaseId = item.Id, Price = price, RecordedAt = DateTime.UtcNow });
                        await context.SaveChangesAsync();
                        return true;
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"Error: {ex.Message}"); }

            return false;
        }

        // ==========================================
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ (Helpers)
        // ==========================================
        private StandoffPortfolioTracker.Core.Enums.ItemRarity ParseRarity(string rarityStr)
        {
            if (Enum.TryParse(typeof(StandoffPortfolioTracker.Core.Enums.ItemRarity), rarityStr, true, out var result))
                return (StandoffPortfolioTracker.Core.Enums.ItemRarity)result;
            return StandoffPortfolioTracker.Core.Enums.ItemRarity.Common;
        }

        private StandoffPortfolioTracker.Core.Enums.ItemType ParseType(string typeStr)
        {
            if (typeStr.Contains("Container") || typeStr.Contains("Case") || typeStr.Contains("Box"))
                return StandoffPortfolioTracker.Core.Enums.ItemType.Container;
            if (typeStr.Contains("Sticker")) return StandoffPortfolioTracker.Core.Enums.ItemType.Sticker;
            if (typeStr.Contains("Charm")) return StandoffPortfolioTracker.Core.Enums.ItemType.Charm;
            if (typeStr.Contains("Gloves")) return StandoffPortfolioTracker.Core.Enums.ItemType.Glove;
            if (typeStr.Contains("Knife") || typeStr.Contains("Kukri") || typeStr.Contains("Daggers") || typeStr.Contains("Karambit")) return StandoffPortfolioTracker.Core.Enums.ItemType.Knife;
            return StandoffPortfolioTracker.Core.Enums.ItemType.Skin;
        }
    }

    // DTO классы для десериализации JSON
    public class SkinDto
    {
        [System.Text.Json.Serialization.JsonPropertyName("name")]
        public string FullName { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("type")]
        public string Type { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("rare")]
        public string Rarity { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("Collection")]
        public string Collection { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("id_Img")]
        public string ImageUrl { get; set; }
    }

    public class PriceDataDto
    {
        public string Date { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("purchase_price")]
        public string PurchasePrice { get; set; }
    }
}