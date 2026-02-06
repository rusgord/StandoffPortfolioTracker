using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Core.Enums;
using StandoffPortfolioTracker.Infrastructure;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class ItemService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public ItemService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        // === ПОЛУЧЕНИЕ С ФИЛЬТРАМИ И ПАГИНАЦИЕЙ ===
        public async Task<(List<ItemBase> Items, int TotalCount)> GetItemsFilteredAsync(
            int skip, int take,
            string? search = null,
            ItemType? type = null,
            ItemRarity? rarity = null,
            int? collectionId = null)
        {
            using var context = await _factory.CreateDbContextAsync();
            var query = context.ItemBases.Include(i => i.Collection).AsQueryable();

            if (!string.IsNullOrWhiteSpace(search))
            {
                search = search.ToLower();
                query = query.Where(i => i.Name.ToLower().Contains(search) ||
                                         (i.SkinName != null && i.SkinName.ToLower().Contains(search)));
            }

            if (type.HasValue) query = query.Where(i => i.Type == type);
            if (rarity.HasValue) query = query.Where(i => i.Rarity == rarity);
            if (collectionId.HasValue && collectionId > 0) query = query.Where(i => i.CollectionId == collectionId);

            var totalCount = await query.CountAsync();

            // ИСПРАВЛЕННАЯ СОРТИРОВКА
            var items = await query
                .OrderByDescending(i => i.Rarity) // 1. Сначала дорогие (Arcane)
                .ThenBy(i => i.Name)            // 2. Группируем по оружию (Akimbo Uzi)
                .ThenBy(i => i.SkinName)        // 3. Группируем по скину (Ravage, Skull...)
                .ThenBy(i => i.IsStatTrack)     // 4. Сначала обычный, следом StatTrack
                .Skip(skip)
                .Take(take)
                .ToListAsync();

            return (items, totalCount);
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
    }
}