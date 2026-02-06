using HtmlAgilityPack;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Core.Enums;
using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.Infrastructure;
using System.Text.RegularExpressions;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class WikiParserService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        public WikiParserService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<string> ParseCollectionFromWikiUrl(string url, string collectionName)
        {
            // 1. Качаем HTML страницы
            var web = new HtmlWeb();
            var doc = await web.LoadFromWebAsync(url);

            if (doc == null) return "Ошибка: Не удалось загрузить страницу.";

            using var context = await _factory.CreateDbContextAsync();

            // Проверяем или создаем коллекцию
            var collection = await context.GameCollections.FirstOrDefaultAsync(c => c.Name == collectionName);
            if (collection == null)
            {
                collection = new GameCollection { Name = collectionName };
                context.GameCollections.Add(collection);
                await context.SaveChangesAsync();
            }

            int addedCount = 0;

            // 2. Ищем галереи на странице (Fandom Wiki обычно хранит их в <div class="wikia-gallery"> или похожих)
            // Логика может меняться, но обычно картинки лежат в тегах <a> или <img> внутри gallery-item
            var imageNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'wikia-gallery-item')]//img");

            if (imageNodes == null) return "Не найдено изображений на странице. Возможно, структура Вики изменилась.";

            foreach (var imgNode in imageNodes)
            {
                // URL картинки (обычно data-src, если ленивая загрузка, или src)
                string imgUrl = imgNode.GetAttributeValue("data-src", null) ?? imgNode.GetAttributeValue("src", null);

                // Название скина (обычно в alt или title)
                string altText = imgNode.GetAttributeValue("alt", ""); // Пример: "AKR Necromancer"

                if (string.IsNullOrEmpty(imgUrl) || string.IsNullOrEmpty(altText)) continue;

                // Чистим URL от лишних параметров (всё что после .png)
                if (imgUrl.Contains(".png"))
                    imgUrl = imgUrl.Substring(0, imgUrl.IndexOf(".png") + 4);

                // Чистим название (Убираем "Skin ", "Sticker " и т.д. если есть, или парсим)
                // Ожидаем формат: "WeaponName SkinName"
                var (weapon, skin) = ParseWikiName(altText);

                // Проверяем, есть ли уже такой скин
                bool exists = await context.ItemBases.AnyAsync(i => i.Name == weapon && i.SkinName == skin);
                if (!exists)
                {
                    var newItem = new ItemBase
                    {
                        Name = weapon,
                        SkinName = skin,
                        ImageUrl = imgUrl,
                        CollectionId = collection.Id,
                        Type = DetermineType(weapon), // Метод определения типа (нож, оружие, стикер)
                        Rarity = ItemRarity.Common, // С Вики сложно вытянуть редкость автоматом, придется править вручную
                        CurrentMarketPrice = 0
                    };
                    context.ItemBases.Add(newItem);
                    addedCount++;
                }
            }

            await context.SaveChangesAsync();
            return $"Успешно! Добавлено {addedCount} предметов в коллекцию '{collectionName}'.";
        }

        // Хелпер для разделения "AKR 12 Railgun" -> "AKR 12", "Railgun"
        private (string Weapon, string Skin) ParseWikiName(string fullName)
        {
            // Это простая эвристика, возможно придется доработать под конкретные названия
            // Обычно первое слово - оружие, остальное скин. Но для "M4 A1" или "Desert Eagle" сложнее.

            string[] doubleNames = { "Desert Eagle", "M40 Pro", "M4 A1", "MP7", "P350", "Tec-9", "FabM", "SM1014", "SPAS", "MAC-10", "UMP-45", "MP5" };

            foreach (var dn in doubleNames)
            {
                if (fullName.StartsWith(dn, StringComparison.OrdinalIgnoreCase))
                {
                    return (dn, fullName.Substring(dn.Length).Trim());
                }
            }

            // Стандартно: Первое слово оружие
            var parts = fullName.Split(' ', 2);
            if (parts.Length > 1) return (parts[0], parts[1]);
            return (fullName, "Unknown");
        }

        private ItemType DetermineType(string weaponName)
        {
            weaponName = weaponName.ToLower();
            if (weaponName.Contains("sticker")) return ItemType.Sticker;
            if (weaponName.Contains("glove")) return ItemType.Glove;
            if (weaponName.Contains("knife") || weaponName.Contains("karambit") || weaponName.Contains("bayonet")) return ItemType.Knife;
            if (weaponName.Contains("charm")) return ItemType.Charm;
            return ItemType.Guns;
        }
    }
}