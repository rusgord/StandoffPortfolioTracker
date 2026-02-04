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

        // Получить все предметы
        public async Task<List<ItemBase>> GetAllItemsAsync()
        {
            using var context = _factory.CreateDbContext();
            return await context.ItemBases
                .Include(i => i.Collection) // Подгружаем название коллекции
                .OrderByDescending(i => i.Id)
                .ToListAsync();
        }

        // Добавить новый предмет
        public async Task AddItemAsync(ItemBase item)
        {
            using var context = _factory.CreateDbContext();
            context.ItemBases.Add(item);
            await context.SaveChangesAsync();
        }

        // Получить список коллекций (для выпадающего списка)
        public async Task<List<GameCollection>> GetCollectionsAsync()
        {
            using var context = _factory.CreateDbContext();
            return await context.GameCollections.ToListAsync();
        }

        // Быстро создать коллекцию (если её нет)
        public async Task AddCollectionAsync(GameCollection collection)
        {
            using var context = _factory.CreateDbContext();
            context.GameCollections.Add(collection);
            await context.SaveChangesAsync();
        }
        // Метод для обновления существующего предмета (например, изменения цены)
        public async Task UpdateItemAsync(ItemBase item)
        {
            using var context = _factory.CreateDbContext();
            context.ItemBases.Update(item);
            await context.SaveChangesAsync();
        }
    }
}