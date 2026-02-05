using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Net.Http.Json;
using System.Collections.Concurrent;

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
            // 1. Создаем контекст и загружаем все предметы
            using var context = await _factory.CreateDbContextAsync();
            var items = await context.ItemBases.ToListAsync();

            int successCount = 0;
            int errorCount = 0;

            // Потокобезопасная коллекция для сбора истории
            // (EF Core нельзя трогать из разных потоков одновременно, поэтому сначала соберем данные сюда)
            var historyBag = new ConcurrentBag<MarketHistory>();

            // Настройка параллелизма (10 запросов одновременно)
            var parallelOptions = new ParallelOptions { MaxDegreeOfParallelism = 10 };

            // 2. Запускаем параллельную обработку
            await Parallel.ForEachAsync(items, parallelOptions, async (item, token) =>
            {
                try
                {
                    // === ЛОГИКА ИМЕНИ ===
                    // Если у нас есть "Оригинальное имя" (с кавычками), берем его.
                    // Если нет — собираем по старинке.
                    var fullName = !string.IsNullOrEmpty(item.OriginalName)
                        ? item.OriginalName
                        : $"{item.Name} {item.SkinName}".Trim();

                    var encodedName = Uri.EscapeDataString(fullName);
                    var url = $"https://standoff-2.com/skins-new.php?command=getStat&name={encodedName}";

                    // 3. Запрос к API
                    var history = await _httpClient.GetFromJsonAsync<List<PriceDataDto>>(url, token);

                    if (history != null && history.Any())
                    {
                        var lastEntry = history.Last();

                        // Чистим цену (иногда приходит с запятой, иногда с точкой)
                        string cleanPrice = lastEntry.PurchasePrice.Replace(",", ".");

                        if (decimal.TryParse(cleanPrice, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out decimal price))
                        {
                            // А. Обновляем текущую цену (в памяти объекта)
                            item.CurrentMarketPrice = price;

                            // Б. Создаем запись для истории и кладем в "мешок"
                            historyBag.Add(new MarketHistory
                            {
                                ItemBaseId = item.Id,
                                Price = price,
                                RecordedAt = DateTime.UtcNow
                            });

                            // Безопасный счетчик
                            Interlocked.Increment(ref successCount);
                        }
                    }
                }
                catch (Exception ex)
                {
                    // Можно раскомментировать для отладки
                    // Console.WriteLine($"Ошибка с {item.Name}: {ex.Message}");
                    Interlocked.Increment(ref errorCount);
                }
            });

            // 4. После завершения всех потоков — сохраняем всё в базу разом
            // Добавляем всю накопленную историю
            context.MarketHistory.AddRange(historyBag);

            // Сохраняем изменения (и обновленные цены предметов, и новую историю)
            await context.SaveChangesAsync();

            return $"Готово! Обновлено: {successCount}. Ошибок: {errorCount}";
        }

        // Вспомогательный класс для их JSON ответа
        public class PriceDataDto
        {
            public string Date { get; set; } // "2025-02-04 12:00:00"

            [System.Text.Json.Serialization.JsonPropertyName("purchase_price")]
            public string PurchasePrice { get; set; }
        }

        public async Task<string> ImportAllSkinsAsync()
        {
            // 1. Качаем огромный JSON со всеми скинами
            var url = "https://standoff-2.com/skins-new.php?command=getModelInfo";
            var skinsFromSite = await _httpClient.GetFromJsonAsync<List<SkinDto>>(url);

            if (skinsFromSite == null) return "Ошибка: сайт вернул пустой список.";

            using var context = await _factory.CreateDbContextAsync();

            // 2. Кэшируем существующие коллекции, чтобы не долбить базу
            var existingCollections = await context.GameCollections.ToDictionaryAsync(c => c.Name, c => c);

            // Кэшируем существующие предметы (по полному имени), чтобы не создавать дубли
            // (Собираем ключ как "Name|SkinName")
            var existingItems = await context.ItemBases
                .Select(i => new { Key = i.Name + "|" + (i.SkinName ?? ""), Item = i })
                .ToDictionaryAsync(x => x.Key, x => x.Item);

            int addedCount = 0;
            int skippedCount = 0;

            foreach (var skinDto in skinsFromSite)
            {
                // === Логика парсинга имени ===
                // Пример: 'AKR "Necromancer"' -> Name="AKR", Skin="Necromancer"
                // Пример: '"Empire" Case' -> Name="Empire Case", Skin=null
                string name = skinDto.FullName;
                string? skinName = null;

                // Ищем текст в кавычках
                var match = Regex.Match(skinDto.FullName, "(.+?)\\s+\"(.+?)\"");
                if (match.Success)
                {
                    name = match.Groups[1].Value.Trim(); // AKR
                    skinName = match.Groups[2].Value.Trim(); // Necromancer
                }
                else if (skinDto.FullName.Contains("\""))
                {
                    // Случай для кейсов типа '"Empire" Case' - просто убираем кавычки
                    name = skinDto.FullName.Replace("\"", "").Trim();
                }

                // === Работа с Коллекцией ===
                GameCollection? collection = null;
                if (!string.IsNullOrEmpty(skinDto.Collection) && skinDto.Collection != "unknown")
                {
                    if (!existingCollections.TryGetValue(skinDto.Collection, out collection))
                    {
                        collection = new GameCollection { Name = skinDto.Collection };
                        context.GameCollections.Add(collection);
                        await context.SaveChangesAsync(); // Сохраняем сразу, чтобы получить ID
                        existingCollections[skinDto.Collection] = collection; // Добавляем в кэш
                    }
                }

                // === Создание Предмета ===
                var uniqueKey = name + "|" + (skinName ?? "");

                if (!existingItems.ContainsKey(uniqueKey))
                {
                    var newItem = new ItemBase
                    {
                        Name = name,
                        SkinName = skinName,
                        OriginalName = skinDto.FullName,
                        Rarity = ParseRarity(skinDto.Rarity), // Метод-помощник ниже
                        Type = ParseType(skinDto.Type),       // Метод-помощник ниже
                        CollectionId = collection?.Id ?? 1,   // 1 - это дефолтная, если нет коллекции
                        ImageUrl = skinDto.ImageUrl,
                        CurrentMarketPrice = 0 // Цену обновим отдельной кнопкой
                    };

                    context.ItemBases.Add(newItem);
                    existingItems[uniqueKey] = newItem; // Чтобы не дублировать внутри этого же цикла
                    addedCount++;
                }
                else
                {
                    skippedCount++;
                }
            }

            await context.SaveChangesAsync();
            return $"Готово! Добавлено новых: {addedCount}. Пропущено (уже были): {skippedCount}";
        }

        // === Помощники для конвертации строк в Enum ===

        private StandoffPortfolioTracker.Core.Enums.ItemRarity ParseRarity(string rarityStr)
        {
            // На сайте могут быть "Arcane", "Legendary" и т.д.
            if (Enum.TryParse(typeof(StandoffPortfolioTracker.Core.Enums.ItemRarity), rarityStr, true, out var result))
            {
                return (StandoffPortfolioTracker.Core.Enums.ItemRarity)result;
            }
            return StandoffPortfolioTracker.Core.Enums.ItemRarity.Common; // По умолчанию
        }

        private StandoffPortfolioTracker.Core.Enums.ItemType ParseType(string typeStr)
        {
            // Тут сложнее, типы на сайте могут отличаться от твоих Enum.
            // Можно дописать логику маппинга
            if (typeStr.Contains("Container") || typeStr.Contains("Case") || typeStr.Contains("Box"))
                return StandoffPortfolioTracker.Core.Enums.ItemType.Container;

            if (typeStr.Contains("Sticker")) return StandoffPortfolioTracker.Core.Enums.ItemType.Sticker;
            if (typeStr.Contains("Charm")) return StandoffPortfolioTracker.Core.Enums.ItemType.Charm;
            if (typeStr.Contains("Gloves")) return StandoffPortfolioTracker.Core.Enums.ItemType.Glove;

            return StandoffPortfolioTracker.Core.Enums.ItemType.Skin;
        }

        // Модель данных, как она приходит с сайта standoff-2.com
        public class SkinDto
        {
            [System.Text.Json.Serialization.JsonPropertyName("name")]
            public string FullName { get; set; } // Например: 'AKR "Necromancer"'

            [System.Text.Json.Serialization.JsonPropertyName("type")]
            public string Type { get; set; } // "Rifle"

            [System.Text.Json.Serialization.JsonPropertyName("rare")]
            public string Rarity { get; set; } // "Arcane"

            [System.Text.Json.Serialization.JsonPropertyName("Collection")]
            public string Collection { get; set; } // "Empire"

            [System.Text.Json.Serialization.JsonPropertyName("id_Img")]
            public string ImageUrl { get; set; }
        }
    }
}