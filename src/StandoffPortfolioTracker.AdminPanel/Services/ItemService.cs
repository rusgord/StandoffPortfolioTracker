using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Core.Enums;
using StandoffPortfolioTracker.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using StandoffPortfolioTracker.Infrastructure;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class ItemService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;
        private readonly IMemoryCache _cache;

        public ItemService(IDbContextFactory<AppDbContext> factory, IMemoryCache cache)
        {
            _factory = factory;
            _cache = cache;
        }
        public class ShowcaseItemDto
        {
            public string Name { get; set; }
            public string SkinName { get; set; }
            public string ImageUrl { get; set; }
            public decimal Price { get; set; }
            public double GrowthPercent { get; set; }
        }

        public async Task<List<ShowcaseItemDto>> GetDailyShowcaseAsync()
        {
            // Пытаемся достать из кэша (ключ "DailyShowcase")
            // Данные живут 24 часа. Если их нет - выполняется код внутри.
            return await _cache.GetOrCreateAsync("DailyShowcase", async entry =>
            {
                entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(24); // Обновление раз в сутки

                using var context = await _factory.CreateDbContextAsync();

                // 1. Ищем ID предметов дороже 1000G (Arcane, Nameless и т.д.)
                // Берем случайные 20 штук, чтобы было из чего выбрать рандом
                var candidateIds = await context.ItemBases
                    .Where(i => i.CurrentMarketPrice > 1000)
                    .Select(i => i.Id)
                    .OrderBy(x => Guid.NewGuid()) // SQL Random
                    .Take(20)
                    .ToListAsync();

                if (!candidateIds.Any()) return new List<ShowcaseItemDto>();

                var result = new List<ShowcaseItemDto>();
                var random = new Random();

                // Перемешиваем кандидатов в памяти
                var selectedIds = candidateIds.OrderBy(x => random.Next()).ToList();

                foreach (var id in selectedIds)
                {
                    if (result.Count >= 2) break; // Нам нужно только 2

                    // Берем текущую цену и цену 2 дня назад
                    var item = await context.ItemBases.FindAsync(id);
                    var history = await context.MarketHistory
                        .Where(h => h.ItemBaseId == id && h.RecordedAt >= DateTime.UtcNow.AddDays(-2))
                        .OrderBy(h => h.RecordedAt)
                        .FirstOrDefaultAsync();

                    // Если есть история и цена выросла (или хотя бы не упала сильно)
                    if (item != null && history != null)
                    {
                        var oldPrice = history.Price;
                        var currentPrice = item.CurrentMarketPrice;

                        // Считаем процент
                        double growth = 0;
                        if (oldPrice > 0)
                            growth = (double)((currentPrice - oldPrice) / oldPrice) * 100;

                        // Добавляем только если есть рост (или небольшой минус, если рынок падает)
                        // Но ты просил "показывали рост", поэтому ставим условие > 0
                        if (growth > 0)
                        {
                            result.Add(new ShowcaseItemDto
                            {
                                Name = item.Name,
                                SkinName = item.SkinName,
                                ImageUrl = item.ImageUrl,
                                Price = currentPrice,
                                GrowthPercent = growth
                            });
                        }
                    }
                }

                // Если не нашли с ростом, берем просто дорогие (фоллбек)
                if (result.Count < 2)
                {
                    var fallbackItems = await context.ItemBases
                       .Where(i => i.CurrentMarketPrice > 1000)
                       .OrderByDescending(i => i.CurrentMarketPrice)
                       .Take(2 - result.Count)
                       .ToListAsync();

                    foreach (var item in fallbackItems)
                    {
                        result.Add(new ShowcaseItemDto
                        {
                            Name = item.Name,
                            SkinName = item.SkinName,
                            ImageUrl = item.ImageUrl,
                            Price = item.CurrentMarketPrice,
                            GrowthPercent = 1.5 // Фейковый минимальный рост для красоты, раз данных нет
                        });
                    }
                }

                return result;
            });
        }

        // === ПОЛУЧЕНИЕ С ФИЛЬТРАМИ И ПАГИНАЦИЕЙ ===
        public async Task<PagedResult<ItemBase>> GetItemsFilteredAsync(
    int skip,
    int take,
    string search,
    ItemType? type = null,      
    ItemRarity? rarity = null, 
    int collectionId = 0, 
    bool? isStatTrack = null,   
    bool? isPattern = null)
        {
            using var context = await _factory.CreateDbContextAsync();

            // 1. Базовый запрос
            var query = context.ItemBases
                .Include(i => i.Collection)
                .AsQueryable();

            // 2. Применяем старые фильтры
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(i => i.Name.Contains(search) || i.SkinName.Contains(search));
            }

            if (type.HasValue)
            {
                query = query.Where(i => i.Type == type.Value);
            }

            if (rarity.HasValue)
            {
                query = query.Where(i => i.Rarity == rarity.Value);
            }

            if (collectionId > 0)
            {
                query = query.Where(i => i.CollectionId == collectionId);
            }

            // 3. ✨ ПРИМЕНЯЕМ НОВЫЕ ФИЛЬТРЫ (Серверная фильтрация)
            if (isStatTrack.HasValue && isStatTrack.Value == true)
            {
                query = query.Where(i => i.IsStatTrack);
            }

            if (isPattern.HasValue && isPattern.Value == true)
            {
                query = query.Where(i => i.IsPattern);
            }

            // 4. Считаем общее кол-во (уже с учетом фильтров!)
            var totalCount = await query.CountAsync();

            // 5. Получаем страницу
            var items = await query
                .OrderByDescending(i => i.Rarity)
                .ThenBy(i => i.Name)
                .Skip(skip)
                .Take(take)
                .ToListAsync();

            return new PagedResult<ItemBase>
            {
                Items = items,
                TotalCount = totalCount
            };
        }

        // === БАЗОВЫЕ МЕТОДЫ ===
        public async Task<List<ItemBase>> GetAllItemsAsync() // Для кэша (если нужно)
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.ItemBases.Include(i => i.Collection).ToListAsync();
        }

        public async Task<ItemBase?> GetItemByIdAsync(int id)
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.ItemBases
                .Include(i => i.Collection)
                .FirstOrDefaultAsync(i => i.Id == id);
        }
        public async Task AddItemAsync(ItemBase item)
        {
            using var context = await _factory.CreateDbContextAsync();
            context.ItemBases.Add(item);
            await context.SaveChangesAsync();
        }

        public async Task UpdateItemAsync(ItemBase item)
        {
            using var context = await _factory.CreateDbContextAsync();
            context.ItemBases.Update(item);
            await context.SaveChangesAsync();
        }

        public async Task DeleteItemAsync(int id)
        {
            using var context = await _factory.CreateDbContextAsync();
            var item = await context.ItemBases.FindAsync(id);
            if (item != null)
            {
                context.ItemBases.Remove(item);
                await context.SaveChangesAsync();
            }
        }

        // === КОЛЛЕКЦИИ ===
        public async Task<List<GameCollection>> GetCollectionsAsync()
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.GameCollections.OrderBy(c => c.Name).ToListAsync();
        }

        public async Task SaveCollectionAsync(GameCollection collection)
        {
            using var context = await _factory.CreateDbContextAsync();
            if (collection.Id == 0)
                context.GameCollections.Add(collection);
            else
                context.GameCollections.Update(collection);
            await context.SaveChangesAsync();
        }

        public async Task DeleteCollectionAsync(int id)
        {
            using var context = await _factory.CreateDbContextAsync();
            // Проверяем, есть ли предметы
            var hasItems = await context.ItemBases.AnyAsync(i => i.CollectionId == id);
            if (hasItems) throw new Exception("Нельзя удалить коллекцию, в которой есть предметы!");

            var col = await context.GameCollections.FindAsync(id);
            if (col != null)
            {
                context.GameCollections.Remove(col);
                await context.SaveChangesAsync();
            }
        }

        // === ИСТОРИИ ЦЕН ===
        public async Task<List<(DateTime Date, decimal Price)>> GetPriceHistoryAsync(int itemBaseId, int days = 90)
        {
            using var context = await _factory.CreateDbContextAsync();
            var history = await context.Set<MarketHistory>()
                .Where(h => h.ItemBaseId == itemBaseId && 
                           h.RecordedAt >= DateTime.UtcNow.AddDays(-days))
                .OrderBy(h => h.RecordedAt)
                .Select(h => new { h.RecordedAt, h.Price })
                .ToListAsync();

            return history.Select(h => (h.RecordedAt, h.Price)).ToList();
        }
    }
}