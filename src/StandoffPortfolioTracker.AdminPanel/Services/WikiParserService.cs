using HtmlAgilityPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Playwright;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Core.Enums;
using StandoffPortfolioTracker.Infrastructure;
using System.Net;
using System.Text.RegularExpressions;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class WikiParserService
    {
        private readonly IDbContextFactory<AppDbContext> _factory;

        private readonly string[] _compoundWeapons = new[]
        {
            "M9 Bayonet", "Dual Daggers", "Desert Eagle", "M40 Pro", "M4 A1",
            "Tec-9", "FabM", "SM1014", "SPAS", "MAC-10", "UMP-45", "MP5", "FN FAL",
            "Akimbo Uzi", "G22", "USP", "P350", "Five Seven"
        };

        public WikiParserService(IDbContextFactory<AppDbContext> factory)
        {
            _factory = factory;
        }

        public async Task<string> ParseCollectionFromWikiUrl(string url, string collectionName)
        {
            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = false,
                SlowMo = 50
            });

            var contextBrowser = await browser.NewContextAsync(new BrowserNewContextOptions
            {
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/121.0.0.0 Safari/537.36",
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 }
            });

            var page = await contextBrowser.NewPageAsync();

            try
            {
                await page.GotoAsync(url, new PageGotoOptions { Timeout = 120000 });
                try
                {
                    await page.WaitForSelectorAsync(".item-box, .portable-infobox", new PageWaitForSelectorOptions { Timeout = 120000 });
                }
                catch { }

                string htmlContent = await page.ContentAsync();
                var doc = new HtmlDocument();
                doc.LoadHtml(htmlContent);

                using var context = await _factory.CreateDbContextAsync();

                var collection = await context.GameCollections.FirstOrDefaultAsync(c => c.Name == collectionName);
                if (collection == null) return $"Ошибка: Коллекция '{collectionName}' не найдена.";

                int updatedCount = 0;
                int skippedCount = 0;
                List<string> skippedNames = new();
                bool collectionImageUpdated = false;

                // 1. Картинка коллекции
                var colImgNode = doc.DocumentNode.SelectSingleNode("//figure[@data-source='Иконка']//img")
                                 ?? doc.DocumentNode.SelectSingleNode("//aside[contains(@class, 'portable-infobox')]//img");

                if (colImgNode != null)
                {
                    string colUrl = ExtractImageUrl(colImgNode);
                    if (!string.IsNullOrEmpty(colUrl) && collection.ImageUrl != colUrl)
                    {
                        collection.ImageUrl = colUrl;
                        collectionImageUpdated = true;
                    }
                }

                // 2. Парсинг предметов
                var itemNodes = doc.DocumentNode.SelectNodes("//div[contains(@class, 'item-box')]");

                if (itemNodes == null || itemNodes.Count == 0)
                {
                    var title = doc.DocumentNode.SelectSingleNode("//title")?.InnerText;
                    return $"Не найдено предметов. Заголовок: {title}";
                }

                foreach (var node in itemNodes)
                {
                    var nameNode = node.SelectSingleNode(".//div[contains(@class, 'item-name')]");
                    if (nameNode == null) continue;

                    string rawHtmlName = nameNode.InnerText.Trim();
                    string cssClasses = nameNode.GetAttributeValue("class", "").ToLower();

                    if (rawHtmlName.Contains("Medal", StringComparison.OrdinalIgnoreCase) ||
                        rawHtmlName.Contains("Медаль", StringComparison.OrdinalIgnoreCase)) continue;

                    var names = ParseWikiNameSmart(rawHtmlName);

                    var wikiType = DetermineType(names.Weapon, names.FullName);
                    if (wikiType == null) continue;

                    ItemRarity wikiRarity = DetermineRarityFromClass(cssClasses);
                    var imgNode = node.SelectSingleNode(".//div[contains(@class, 'item-preview')]//img");
                    string skinImgUrl = ExtractImageUrl(imgNode);

                    List<ItemBase> itemsToUpdate = new();

                    // === ПОПЫТКА 1: Строгое совпадение ===
                    var try1 = await context.ItemBases
                        .Where(i => i.Name == names.Weapon && i.SkinName == names.Skin)
                        .ToListAsync();
                    itemsToUpdate.AddRange(try1);

                    // === ПОПЫТКА 2: ГРАФФИТИ (Универсальный поиск по вхождению) ===
                    if (!itemsToUpdate.Any() && !string.IsNullOrEmpty(names.Weapon) && names.Weapon.Contains("Graffiti", StringComparison.OrdinalIgnoreCase))
                    {
                        // Берем ВСЕ предметы коллекции, у которых в имени есть Graffiti
                        var graffitiCandidates = await context.ItemBases
                            .Where(i => i.CollectionId == collection.Id && i.Name.Contains("Graffiti"))
                            .ToListAsync();

                        string wikiSkinLower = names.Skin.ToLower().Trim();

                        foreach (var g in graffitiCandidates)
                        {
                            string dbSkinLower = (g.SkinName ?? "").ToLower().Trim();

                            // ПРОВЕРКА: Содержит ли одна строка другую?
                            // Пример: "Victory Bubble Packed" содержит "Victory Bubble" -> TRUE
                            if (dbSkinLower.Contains(wikiSkinLower) || wikiSkinLower.Contains(dbSkinLower))
                            {
                                itemsToUpdate.Add(g);
                            }
                        }
                    }

                    // === ПОПЫТКА 3: Поиск по скину с ЗАЩИТОЙ ТИПОВ ===
                    if (!itemsToUpdate.Any() && !string.IsNullOrEmpty(names.Skin))
                    {
                        var candidates = await context.ItemBases
                           .Where(i => i.SkinName == names.Skin && i.CollectionId == collection.Id)
                           .ToListAsync();

                        foreach (var c in candidates)
                        {
                            // Защита от перепутывания типов
                            if (names.Weapon.Contains("Graffiti") && !c.Name.Contains("Graffiti")) continue;
                            if (names.Weapon.Contains("Sticker") && !c.Name.Contains("Sticker")) continue;
                            if (names.Weapon.Contains("Charm") && !c.Name.Contains("Charm")) continue;

                            // Защита: не путать пушки с ножами
                            if (wikiType == ItemType.Knife && c.Type != ItemType.Knife) continue;

                            itemsToUpdate.Add(c);
                        }
                    }

                    // Попытка 4: Полное имя (кейсы)
                    if (!itemsToUpdate.Any() && !string.IsNullOrEmpty(names.FullName))
                    {
                        var try2 = await context.ItemBases
                           .Where(i => i.Name == names.FullName || i.OriginalName == names.FullName)
                           .ToListAsync();
                        itemsToUpdate.AddRange(try2);
                    }

                    itemsToUpdate = itemsToUpdate.DistinctBy(x => x.Id).ToList();

                    if (itemsToUpdate.Any())
                    {
                        foreach (var item in itemsToUpdate)
                        {
                            bool changed = false;
                            if (item.CollectionId != collection.Id) { item.CollectionId = collection.Id; changed = true; }

                            if (!string.IsNullOrEmpty(skinImgUrl) && item.ImageUrl != skinImgUrl)
                            {
                                item.ImageUrl = skinImgUrl;
                                changed = true;
                            }

                            if (item.Rarity == ItemRarity.Common && wikiRarity != ItemRarity.Common) { item.Rarity = wikiRarity; changed = true; }

                            // Обновляем тип, если он "безопасный"
                            if (item.Type == ItemType.Guns && wikiType.Value != ItemType.Guns)
                            {
                                item.Type = wikiType.Value;
                                changed = true;
                            }

                            if (changed) updatedCount++;
                        }
                    }
                    else
                    {
                        skippedCount++;
                        skippedNames.Add(rawHtmlName);
                    }
                }

                await context.SaveChangesAsync();

                string msg = $"Готово! Обновлено: {updatedCount}.";
                if (skippedCount > 0) msg += $" Пропущено: {skippedCount} ({string.Join(", ", skippedNames.Take(5))}...).";

                return msg;
            }
            catch (Exception ex)
            {
                return $"Playwright Error: {ex.Message}";
            }
        }

        // --- ХЕЛПЕРЫ ---

        private string ExtractImageUrl(HtmlNode imgNode)
        {
            if (imgNode == null) return null;
            string url = imgNode.GetAttributeValue("data-src", null);
            if (string.IsNullOrEmpty(url)) url = imgNode.GetAttributeValue("src", null);
            if (string.IsNullOrEmpty(url)) return null;
            url = WebUtility.HtmlDecode(url);
            url = url.Replace("amp;", "");
            return url;
        }

        private (string Weapon, string Skin, string FullName) ParseWikiNameSmart(string rawName)
        {
            rawName = rawName.Trim();

            var match = Regex.Match(rawName, @"^(.*?)\s*«(.*?)»(.*)$");
            if (match.Success)
            {
                string weapon = match.Groups[1].Value.Trim();
                string skinInner = match.Groups[2].Value.Trim();
                string suffix = match.Groups[3].Value.Trim();

                string fullSkinName = string.IsNullOrEmpty(suffix) ? skinInner : $"{skinInner} {suffix}";
                string fullNameClean = rawName.Replace("«", "").Replace("»", "");

                return (weapon, fullSkinName, fullNameClean);
            }

            foreach (var compound in _compoundWeapons)
            {
                if (rawName.StartsWith(compound, StringComparison.OrdinalIgnoreCase))
                {
                    string skin = rawName.Substring(compound.Length).Trim();
                    return (compound, skin, rawName);
                }
            }

            var parts = rawName.Split(' ', 2);
            if (parts.Length > 1) return (parts[0], parts[1], rawName);

            return (rawName, "", rawName);
        }

        private ItemRarity DetermineRarityFromClass(string cssClass)
        {
            if (cssClass.Contains("common")) return ItemRarity.Common;
            if (cssClass.Contains("uncommon")) return ItemRarity.Uncommon;
            if (cssClass.Contains("rare")) return ItemRarity.Rare;
            if (cssClass.Contains("epic")) return ItemRarity.Epic;
            if (cssClass.Contains("legendary")) return ItemRarity.Legendary;
            if (cssClass.Contains("arcane")) return ItemRarity.Arcane;
            if (cssClass.Contains("nameless")) return ItemRarity.Nameless;
            if (cssClass.Contains("none")) return ItemRarity.Common;
            return ItemRarity.Common;
        }

        private ItemType? DetermineType(string weaponName, string fullName)
        {
            string n = (weaponName + " " + fullName).ToLower();
            string[] knives = { "karambit", "butterfly", "m9 bayonet", "kunai", "scorpion", "jkommando", "dual daggers", "flip", "tanto", "kukri", "stiletto", "fang", "mantis", "sting" };
            foreach (var k in knives) if (n.Contains(k)) return ItemType.Knife;

            if (n.Contains("case") || n.Contains("box") || n.Contains("pack")) return ItemType.Container;
            if (n.Contains("sticker") || n.Contains("shield")) return ItemType.Sticker;
            if (n.Contains("charm") || n.Contains("брелок")) return ItemType.Charm;
            if (n.Contains("glove") || n.Contains("перчатки")) return ItemType.Glove;
            if (n.Contains("graffiti") || n.Contains("граффити")) return ItemType.Graffiti;
            if (n.Contains("grenade") || n.Contains("flashbang") || n.Contains("smoke") || n.Contains("molotov")) return ItemType.Grenade;
            if (n.Contains("medal") || n.Contains("медаль")) return null;
            return ItemType.Guns;
        }
    }
}