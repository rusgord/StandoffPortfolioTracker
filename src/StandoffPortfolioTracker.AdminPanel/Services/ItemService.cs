using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Core.Enums;
using StandoffPortfolioTracker.Core.Models;
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
    }
}