using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace StandoffPortfolioTracker.Core.Entities
{
    public enum NewsCategory
    {
        SiteNews,       // Новости сайта
        GameUpdate,     // Обновление игры
        Collection,     // Новая коллекция
        Event,          // Ивент/Турнир
        MarketAnalytics // Аналитика рынка
    }

    public enum NewsBlockType
    {
        Paragraph,  // Просто текст
        Header,     // Заголовок H2/H3
        Image,      // Ссылка на картинку
        Video,      // Ссылка на YouTube
        Quote       // Цитата
    }

    public class NewsPost
    {
        public int Id { get; set; }

        [Required]
        public string Title { get; set; } = "";

        public string Slug { get; set; } = ""; // Для красивой ссылки /news/new-case-out

        public string ShortDescription { get; set; } = ""; // Для превью в ленте
        public string CoverImageUrl { get; set; } = "";    // Картинка в ленте

        public NewsCategory Category { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public bool IsPublished { get; set; } = false;

        // Связь с блоками контента
        public List<NewsBlock> Blocks { get; set; } = new();
    }

    public class NewsBlock
    {
        public int Id { get; set; }
        public int NewsPostId { get; set; }

        public NewsBlockType Type { get; set; }
        public string Content { get; set; } = ""; // Текст или URL
        public int OrderIndex { get; set; }       // Порядок блока в статье
    }
}