using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;
using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.RegularExpressions;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class PriceParserService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly HttpClient _httpClient;
        private readonly PriceHistoryFileService _priceHistoryService;

        public PriceParserService(IDbContextFactory<AppDbContext> factory, HttpClient httpClient, PriceHistoryFileService priceHistoryService)
        {
            _factory = factory;
            _httpClient = httpClient;
            _priceHistoryService = priceHistoryService;

            // Притворяемся браузером
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
        }

        // ==========================================
        // 1. ИМПОРТ ВСЕХ СКИНОВ
        // ==========================================
        // ==========================================
        // 1. ИМПОРТ ВСЕХ СКИНОВ (ОБНОВЛЕННЫЙ)
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

            // Загружаем базу
            var existingItemsList = await context.ItemBases.ToListAsync();

            // 1. Словарь для поиска по УНИКАЛЬНОМУ КЛЮЧУ (Имя + Скин)
            var existingItemsDict = existingItemsList
                .GroupBy(i => GenerateUniqueKey(i.Name, i.SkinName, i.IsStatTrack))
                .ToDictionary(g => g.Key, g => g.First());

            // 2. НОВЫЙ СЛОВАРЬ: Поиск по ORIGINAL NAME (Точное имя с сайта)
            // Это спасет от дублей Граффити Packed и прочих переименований
            var existingByOriginalName = existingItemsList
                .Where(x => !string.IsNullOrEmpty(x.OriginalName))
                .GroupBy(x => x.OriginalName) // Группируем на случай, если дубли уже есть
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

                // ---------------------------------------------------------
                // ШАГ 1: Попытка найти предмет "в лоб" по OriginalName
                // ---------------------------------------------------------
                ItemBase? currentItem = null;

                if (existingByOriginalName.TryGetValue(originalName, out var foundByOrig))
                {
                    currentItem = foundByOrig;
                }

                // Подготовка данных для парсинга (нужны в любом случае для проверки/создания)
                bool isStatTrack = originalName.Contains("StatTrack", StringComparison.OrdinalIgnoreCase);
                string baseNameForInfo = Regex.Replace(originalName, "StatTrack", "", RegexOptions.IgnoreCase).Trim();

                // Ищем инфо
                SkinDto? info = null;
                if (!infoDict.TryGetValue(originalName, out info) && isStatTrack)
                {
                    infoDict.TryGetValue(baseNameForInfo, out info);
                }
                if (info == null) info = new SkinDto { FullName = originalName, Type = "unknown", Rarity = "Common" };

                // Парсим имя и скин
                var (name, skinName) = ParseNameParts(baseNameForInfo);

                // Определяем параметры
                var siteType = ParseType(info.Type, name);
                var siteRarity = ParseRarity(info.Rarity);

                // Коллекция
                GameCollection? siteCollection = null;
                if (!string.IsNullOrEmpty(info.Collection) && info.Collection != "unknown")
                {
                    if (!existingCollections.TryGetValue(info.Collection, out siteCollection))
                    {
                        siteCollection = new GameCollection { Name = info.Collection };
                        context.GameCollections.Add(siteCollection);
                        await context.SaveChangesAsync();
                        existingCollections[info.Collection] = siteCollection;
                    }
                }

                // ---------------------------------------------------------
                // ШАГ 2: Если не нашли по OriginalName, ищем по смыслу (Имя + Скин)
                // ---------------------------------------------------------
                if (currentItem == null)
                {
                    string uniqueKey = GenerateUniqueKey(name, skinName, isStatTrack);
                    if (existingItemsDict.TryGetValue(uniqueKey, out var foundByKey))
                    {
                        currentItem = foundByKey;
                    }
                }

                // ---------------------------------------------------------
                // ШАГ 3: Логика Обновления или Создания
                // ---------------------------------------------------------
                if (currentItem != null)
                {
                    // === ПРЕДМЕТ СУЩЕСТВУЕТ (Обновляем только если что-то не так) ===
                    bool changed = false;

                    // Обновляем OriginalName, если он был пустой или изменился (для привязки)
                    if (currentItem.OriginalName != originalName)
                    {
                        currentItem.OriginalName = originalName;
                        changed = true;
                    }

                    // Обновляем картинку (если пустая или изменилась)
                    if (!string.IsNullOrEmpty(info.ImageUrl) && (string.IsNullOrEmpty(currentItem.ImageUrl) || currentItem.ImageUrl != info.ImageUrl))
                    {
                        currentItem.ImageUrl = info.ImageUrl;
                        changed = true;
                    }

                    // Обновляем Тип (если стоит дефолтный Guns, а мы узнали точнее)
                    if (currentItem.Type == StandoffPortfolioTracker.Core.Enums.ItemType.Guns && siteType != StandoffPortfolioTracker.Core.Enums.ItemType.Guns)
                    {
                        currentItem.Type = siteType;
                        changed = true;
                    }

                    // Обновляем Коллекцию (если она есть на сайте, а у нас нет)
                    if (currentItem.CollectionId == 1 && siteCollection != null)
                    {
                        currentItem.CollectionId = siteCollection.Id;
                        changed = true;
                    }

                    if (changed) updatedCount++;
                    else skippedDuplicates++;
                }
                else
                {
                    // === НОВЫЙ ПРЕДМЕТ ===
                    var newItem = new ItemBase
                    {
                        Name = name,
                        SkinName = skinName,
                        OriginalName = originalName, // Важно сохранить для будущих проверок
                        IsStatTrack = isStatTrack,
                        Rarity = siteRarity,
                        Type = siteType,
                        CollectionId = siteCollection?.Id ?? 1,
                        ImageUrl = info.ImageUrl,
                        CurrentMarketPrice = 0
                    };

                    context.ItemBases.Add(newItem);

                    // Добавляем в кэши, чтобы в этом же цикле не создать дубль
                    string uniqueKey = GenerateUniqueKey(name, skinName, isStatTrack);
                    existingItemsDict[uniqueKey] = newItem;
                    existingByOriginalName[originalName] = newItem;

                    addedCount++;
                }
            }

            await context.SaveChangesAsync();
            return $"Импорт завершен! Новых: {addedCount}. Обновлено: {updatedCount}. Пропущено: {skippedDuplicates}";
        }


        // ==========================================
        // 🛠 ДИАГНОСТИКА: Сохранить ответ API в файлы
        // ==========================================
        public async Task<string> DebugDownloadApiDataAsync()
        {
            try
            {
                var namesUrl = "https://standoff-2.com/skins-new.php?command=getNames";
                var infoUrl = "https://standoff-2.com/skins-new.php?command=getModelInfo";

                // Скачиваем сырые строки JSON
                var namesJson = await _httpClient.GetStringAsync(namesUrl);
                var infoJson = await _httpClient.GetStringAsync(infoUrl);

                // Путь к файлам (сохраним в папку запуска приложения)
                string basePath = AppDomain.CurrentDomain.BaseDirectory;
                string namesPath = Path.Combine(basePath, "debug_names.json");
                string infoPath = Path.Combine(basePath, "debug_info.json");

                await File.WriteAllTextAsync(namesPath, namesJson);
                await File.WriteAllTextAsync(infoPath, infoJson);

                // Проверка наличия "Winter Tale" в скачанном
                bool containsNewCase = namesJson.Contains("Winter Tale", StringComparison.OrdinalIgnoreCase);

                return $"Данные сохранены в:\n{namesPath}\n\nНайдено ли 'Winter Tale': {(containsNewCase ? "✅ ДА" : "❌ НЕТ")}";
            }
            catch (Exception ex)
            {
                return $"Ошибка диагностики: {ex.Message}";
            }
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

            // РЕГУЛЯРКА: Захватывает хвост после кавычек (Group 3)
            var match = Regex.Match(fullName, "(.+?)\\s+\"(.+?)\"(.*)");

            if (match.Success)
            {
                name = match.Groups[1].Value.Trim();

                string skinPart = match.Groups[2].Value.Trim();
                string suffix = match.Groups[3].Value.Trim();

                // Если есть суффикс, добавляем его к названию скина
                skinName = string.IsNullOrEmpty(suffix) ? skinPart : $"{skinPart} {suffix}";
            }
            else if (fullName.Contains("\""))
            {
                name = fullName.Replace("\"", "").Trim();
            }
            return (name, skinName);
        }

        private StandoffPortfolioTracker.Core.Enums.ItemType ParseType(string typeStr, string nameStr)
        {
            string combined = (typeStr + " " + nameStr).ToLower();

            if (combined.Contains("sticker") || combined.Contains("стикер")) return StandoffPortfolioTracker.Core.Enums.ItemType.Sticker;
            if (combined.Contains("charm") || combined.Contains("брелок")) return StandoffPortfolioTracker.Core.Enums.ItemType.Charm;
            if (combined.Contains("gloves") || combined.Contains("перчатки")) return StandoffPortfolioTracker.Core.Enums.ItemType.Glove;

            if (combined.Contains("graffiti") || combined.Contains("граффити")) return StandoffPortfolioTracker.Core.Enums.ItemType.Graffiti;
            if (combined.Contains("fragment") || combined.Contains("фрагмент")) return StandoffPortfolioTracker.Core.Enums.ItemType.Fragment;

            if (combined.Contains("container") || combined.Contains("case") || combined.Contains("box") || combined.Contains("pack"))
                return StandoffPortfolioTracker.Core.Enums.ItemType.Container;

            if (combined.Contains("knife") || combined.Contains("kukri") || combined.Contains("daggers") || combined.Contains("karambit") || combined.Contains("bayonet") || combined.Contains("jkommando") || combined.Contains("scorpion") || combined.Contains("kunai") || combined.Contains("tanto") || combined.Contains("dual daggers") || combined.Contains("butterfly") || combined.Contains("flip") || combined.Contains("fang") || combined.Contains("stiletto"))
                return StandoffPortfolioTracker.Core.Enums.ItemType.Knife;

            if (combined.Contains("grenade") || combined.Contains("flashbang") || combined.Contains("smoke"))
                return StandoffPortfolioTracker.Core.Enums.ItemType.Grenade;

            return StandoffPortfolioTracker.Core.Enums.ItemType.Guns;
        }

        // ==========================================
        // 2. ОБНОВЛЕНИЕ ВСЕХ ЦЕН
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
                            item.LastUpdate = DateTime.UtcNow;
                            historyBag.Add(new MarketHistory { ItemBaseId = item.Id, Price = price, RecordedAt = DateTime.UtcNow });

                            // Сохраняем цену в файл
                            await _priceHistoryService.SavePriceHistoryAsync(item.Id, price, item);

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

            // Периодическая очистка старых данных
            await _priceHistoryService.CleanupOldHistoryAsync();

            return $"Готово! Обновлено: {successCount}. Ошибок: {errorCount}";
        }

        // ==========================================
        // 3. ОБНОВЛЕНИЕ ПОРТФЕЛЯ
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
        // 5. ИСПРАВЛЕНИЕ КАРТИНОК (Замена на PNG без фона)
        // ==========================================
        public async Task<string> UpdateImagesFromApiAsync()
        {
            var infoUrl = "https://standoff-2.com/skins-new.php?command=getModelInfo";

            List<SkinDto> modelInfos;
            try
            {
                modelInfos = await _httpClient.GetFromJsonAsync<List<SkinDto>>(infoUrl);
            }
            catch (Exception ex)
            {
                return $"Ошибка соединения с API: {ex.Message}";
            }

            if (modelInfos == null) return "API не вернуло данных.";

            // Превращаем список в словарь для мгновенного поиска
            // Ключ: FullName (например "AKR12 «Necromancer»")
            var infoDict = modelInfos
                .Where(x => !string.IsNullOrEmpty(x.FullName))
                .GroupBy(x => x.FullName.Trim().ToLower()) // Группируем, чтобы избежать дублей
                .ToDictionary(g => g.Key, g => g.First());

            using var context = await _factory.CreateDbContextAsync();
            var items = await context.ItemBases.ToListAsync();

            int updatedCount = 0;

            foreach (var item in items)
            {
                // Формируем имя для поиска, как на сайте
                // Если OriginalName есть - берем его, иначе собираем из частей
                string searchName = !string.IsNullOrEmpty(item.OriginalName)
                    ? item.OriginalName
                    : $"{item.Name} «{item.SkinName}»".Trim(); // Формат сайта обычно с кавычками

                // Если это StatTrack, сайт может хранить его как "StatTrack AKR..." или просто "AKR..."
                // Попробуем поискать точное совпадение
                if (infoDict.TryGetValue(searchName.ToLower(), out var info))
                {
                    // Нашли! Проверяем, есть ли картинка
                    if (!string.IsNullOrEmpty(info.ImageUrl) && item.ImageUrl != info.ImageUrl)
                    {
                        item.ImageUrl = info.ImageUrl;
                        updatedCount++;
                    }
                }
                else
                {
                    // Попытка №2: Если не нашли, попробуем без кавычек или в другом формате
                    // Например, у нас "M9 Bayonet", а там "M9 Bayonet «...»"
                    var altKey = $"{item.Name} {item.SkinName}".ToLower();
                    if (infoDict.TryGetValue(altKey, out var info2))
                    {
                        if (!string.IsNullOrEmpty(info2.ImageUrl) && item.ImageUrl != info2.ImageUrl)
                        {
                            item.ImageUrl = info2.ImageUrl;
                            updatedCount++;
                        }
                    }
                }
            }

            await context.SaveChangesAsync();
            return $"Картинки обновлены! Заменено на PNG без фона: {updatedCount} шт.";
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
    }

    // DTO классы
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