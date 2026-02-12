using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class NewsService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public NewsService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<List<NewsPost>> GetAllNewsAsync(bool onlyPublished = true)
        {
            using var ctx = await _factory.CreateDbContextAsync();
            var q = ctx.NewsPosts.AsQueryable();
            if (onlyPublished) q = q.Where(x => x.IsPublished);
            return await q.OrderByDescending(x => x.CreatedAt).ToListAsync();
        }

        public async Task<NewsPost?> GetNewsBySlugAsync(string slug)
        {
            using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.NewsPosts
                .Include(x => x.Blocks.OrderBy(b => b.OrderIndex))
                .FirstOrDefaultAsync(x => x.Slug == slug);
        }

        public async Task<NewsPost?> GetNewsByIdAsync(int id)
        {
            using var ctx = await _factory.CreateDbContextAsync();
            return await ctx.NewsPosts
                .Include(x => x.Blocks.OrderBy(b => b.OrderIndex))
                .FirstOrDefaultAsync(x => x.Id == id);
        }

        public async Task SaveNewsAsync(NewsPost post)
        {
            using var ctx = await _factory.CreateDbContextAsync();

            if (string.IsNullOrEmpty(post.Slug))
                post.Slug = GenerateSlug(post.Title);

            if (post.Id == 0)
            {
                ctx.NewsPosts.Add(post);
            }
            else
            {
                // Обновляем саму новость
                ctx.NewsPosts.Update(post);

                // Удаляем старые блоки и добавляем новые (простой способ обновления списка)
                var oldBlocks = await ctx.NewsBlocks.Where(x => x.NewsPostId == post.Id).ToListAsync();
                ctx.NewsBlocks.RemoveRange(oldBlocks);

                foreach (var block in post.Blocks) block.Id = 0; // Сброс ID чтобы добавились как новые
                ctx.NewsBlocks.AddRange(post.Blocks);
            }
            await ctx.SaveChangesAsync();
        }

        public async Task DeleteNewsAsync(int id)
        {
            using var ctx = await _factory.CreateDbContextAsync();
            var post = await ctx.NewsPosts.FindAsync(id);
            if (post != null)
            {
                ctx.NewsPosts.Remove(post);
                await ctx.SaveChangesAsync();
            }
        }

        private string GenerateSlug(string title)
        {
            // Простой генератор slug (транслит или просто замена пробелов)
            return title.ToLower().Replace(" ", "-").Replace(".", "").Replace("?", "") + "-" + DateTime.Now.Ticks.ToString().Substring(10);
        }
    }
}