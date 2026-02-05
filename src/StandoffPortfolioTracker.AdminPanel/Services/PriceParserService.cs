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
            var namesUrl = "https://standoff-2.com/skins-new.php?command=getNames";
            var infoUrl = "https://standoff-2.com/skins-new.php?command=getModelInfo";

            var namesTask = _httpClient.GetFromJsonAsync<List<List<string>>>(namesUrl);
            var infoTask = _httpClient.GetFromJsonAsync<List<SkinDto>>(infoUrl);

            await Task.WhenAll(namesTask, infoTask);

            var allNames = namesTask.Result;
            var modelInfos = infoTask.Result;

            if (allNames == null || modelInfos == null) return "Ошибка: сайт не вернул данные.";

            // Словарь инфо для быстрого поиска
            var infoDict = modelInfos
                .GroupBy(x => x.FullName)
                .ToDictionary(g => g.Key, g => g.First());

            using var context = await _factory.CreateDbContextAsync();
            
            // Загружаем базу и строим словарь по ЧИСТОМУ ключу (чтобы избежать дублей типа "Name" и "\"Name\"")
            // Ключ: "name|skinname|stat" (в нижнем регистре)
            var existingItemsList = await context.ItemBases.ToListAsync();
            var existingItemsDict = existingItemsList
                .GroupBy(i => GenerateUniqueKey(i.Name, i.SkinName, i.IsStatTrack))
                .ToDictionary(g => g.Key, g => g.First());

            var existingCollections = await context.GameCollections.ToDictionaryAsync(c => c.Name, c => c);

            int addedCount = 0;
            int updatedCount = 0;
            int skippedDuplicates = 0;

            foreach (var nameEntry in allNames)
            {
                if (nameEntry.Count == 0) continue;
                string originalName = nameEntry[0];
                if (string.IsNullOrWhiteSpace(originalName) || originalName == "sdk") continue;

                // 1. Чистим имя
                bool isStatTrack = originalName.Contains("StatTrack");
                string baseNameForInfo = originalName.Replace("StatTrack", "").Trim();
                
                // Ищем инфо
                SkinDto? info = null;
                if (!infoDict.TryGetValue(originalName, out info) && isStatTrack)
                {
                    infoDict.TryGetValue(baseNameForInfo, out info);
                }
                if (info == null) info = new SkinDto { FullName = originalName, Type = "unknown", Rarity = "Common" };

                // Парсим чистое имя и скин
                var (name, skinName) = ParseNameParts(baseNameForInfo);

                // 2. Генерируем УНИКАЛЬНЫЙ КЛЮЧ для проверки дублей
                // Это самое важное изменение: мы проверяем не по OriginalName, а по смыслу
                string uniqueKey = GenerateUniqueKey(name, skinName, isStatTrack);

                // 3. Определяем Тип (улучшенная версия, смотрит и в Имя тоже)
                var type = ParseType(info.Type, name);
                var rarity = ParseRarity(info.Rarity);

                // 4. Коллекция
                GameCollection? collection = null;
                if (!string.IsNullOrEmpty(info.Collection) && info.Collection != "unknown")
                {
                    if (!existingCollections.TryGetValue(info.Collection, out collection))
                    {
                        collection = new GameCollection { Name = info.Collection };
                        context.GameCollections.Add(collection);
                        await context.SaveChangesAsync();
                        existingCollections[info.Collection] = collection;
                    }
                }

                // 5. Логика добавления/обновления
                if (existingItemsDict.TryGetValue(uniqueKey, out var existingItem))
                {
                    // Предмет уже есть (по чистому имени).
                    // Обновляем OriginalName только если он стал "лучше" или поменялась картинка
                    // Но не создаем новый!
                    bool changed = false;
                    
                    // Если у нас в базе старое имя без кавычек, а пришло с кавычками (более точное для парсера) - обновим
                    if (existingItem.OriginalName != originalName && originalName.Contains("\"")) 
                    { 
                        existingItem.OriginalName = originalName; 
                        changed = true; 
                    }
                    
                    if (existingItem.ImageUrl != info.ImageUrl) 
                    { 
                        existingItem.ImageUrl = info.ImageUrl; 
                        changed = true; 
                    }

                    // Фикс типа (если раньше был Skin, а теперь мы поняли что это Charm)
                    if (existingItem.Type != type)
                    {
                        existingItem.Type = type;
                        changed = true;
                    }

                    if (changed) updatedCount++;
                    else skippedDuplicates++;
                }
                else
                {
                    // Новая запись
                    var newItem = new ItemBase
                    {
                        Name = name,
                        SkinName = skinName,
                        OriginalName = originalName,
                        IsStatTrack = isStatTrack,
                        Rarity = rarity,
                        Type = type,
                        CollectionId = collection?.Id ?? 1,
                        ImageUrl = info.ImageUrl,
                        CurrentMarketPrice = 0
                    };

                    context.ItemBases.Add(newItem);
                    existingItemsDict[uniqueKey] = newItem; // Добавляем в кэш, чтобы следующий дубль отсекся
                    addedCount++;
                }
            }

            await context.SaveChangesAsync();
            return $"Готово! Новых: {addedCount}. Обновлено: {updatedCount}. Дубликатов пропущено: {skippedDuplicates}";
        }

        // ==========================================
        // ВСПОМОГАТЕЛЬНЫЕ МЕТОДЫ
        // ==========================================
        
        private string GenerateUniqueKey(string? name, string? skin, bool st)
        {
            return $"{name?.ToLower().Trim()}|{skin?.ToLower().Trim()}|{st}";
        }

        private (string Name, string? Skin) ParseNameParts(string fullName)
        {
            string name = fullName;
            string? skinName = null;

            var match = Regex.Match(fullName, "(.+?)\\s+\"(.+?)\"");
            if (match.Success)
            {
                name = match.Groups[1].Value.Trim();
                skinName = match.Groups[2].Value.Trim();
            }
            else if (fullName.Contains("\""))
            {
                name = fullName.Replace("\"", "").Trim();
            }
            return (name, skinName);
        }



        // Улучшенный парсер типов (смотрит и в название)
        private StandoffPortfolioTracker.Core.Enums.ItemType ParseType(string typeStr, string nameStr)
        {
            string combined = (typeStr + " " + nameStr).ToLower();

            if (combined.Contains("sticker") || combined.Contains("стикер")) return StandoffPortfolioTracker.Core.Enums.ItemType.Sticker;
            if (combined.Contains("charm") || combined.Contains("брелок")) return StandoffPortfolioTracker.Core.Enums.ItemType.Charm;
            if (combined.Contains("gloves") || combined.Contains("перчатки")) return StandoffPortfolioTracker.Core.Enums.ItemType.Glove;
            if (combined.Contains("container") || combined.Contains("case") || combined.Contains("box") || combined.Contains("pack")) 
                return StandoffPortfolioTracker.Core.Enums.ItemType.Container;
            if (combined.Contains("knife") || combined.Contains("kukri") || combined.Contains("daggers") || combined.Contains("karambit") || combined.Contains("bayonet") || combined.Contains("jkommando") || combined.Contains("scorpion") || combined.Contains("kunai") || combined.Contains("tanto") || combined.Contains("dual daggers") || combined.Contains("butterfly") || combined.Contains("flip") || combined.Contains("fang")) 
                return StandoffPortfolioTracker.Core.Enums.ItemType.Knife;
            
            return StandoffPortfolioTracker.Core.Enums.ItemType.Skin;
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