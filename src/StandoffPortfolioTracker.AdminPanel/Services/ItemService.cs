using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
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

        // 1. Получить все предметы (для кэша и списков)
        public async Task<List<ItemBase>> GetAllItemsAsync()
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.ItemBases
                .Include(i => i.Collection) // Подгружаем коллекцию
                .OrderBy(i => i.Name)
                .ToListAsync();
        }

        // 2. Получить ОДИН предмет по ID (Тот самый метод!)
        public async Task<ItemBase?> GetItemByIdAsync(int id)
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.ItemBases
                .Include(i => i.Collection)
                .FirstOrDefaultAsync(i => i.Id == id);
        }

        // 3. Добавить новый предмет
        public async Task AddItemAsync(ItemBase item)
        {
            using var context = await _factory.CreateDbContextAsync();
            context.ItemBases.Add(item);
            await context.SaveChangesAsync();
        }

        // 4. Обновить предмет (цену, имя и т.д.)
        public async Task UpdateItemAsync(ItemBase item)
        {
            using var context = await _factory.CreateDbContextAsync();
            context.ItemBases.Update(item);
            await context.SaveChangesAsync();
        }

        // 5. Получить список коллекций (для фильтров)
        public async Task<List<GameCollection>> GetCollectionsAsync()
        {
            using var context = await _factory.CreateDbContextAsync();
            return await context.GameCollections
                .OrderBy(c => c.Name)
                .ToListAsync();
        }

        // 6. Добавить коллекцию (если её нет)
        public async Task AddCollectionAsync(GameCollection collection)
        {
            using var context = await _factory.CreateDbContextAsync();
            context.GameCollections.Add(collection);
            await context.SaveChangesAsync();
        }
    }
}