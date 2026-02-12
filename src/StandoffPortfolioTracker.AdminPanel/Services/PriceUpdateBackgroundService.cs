using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using StandoffPortfolioTracker.Core.Entities;
using StandoffPortfolioTracker.Infrastructure;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace StandoffPortfolioTracker.AdminPanel.Services
{
    public class PriceUpdateBackgroundService : BackgroundService
    {
        private readonly IServiceProvider _services;
        // Интервал обновления (60 минут)
        private readonly TimeSpan _updateInterval = TimeSpan.FromMinutes(60);

        public PriceUpdateBackgroundService(IServiceProvider services)
        {
            _services = services;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Небольшая задержка на старте (15 сек), чтобы приложение успело полностью запуститься
            // и не тормозило загрузку страниц пользователю
            await Task.Delay(TimeSpan.FromSeconds(15), stoppingToken);

            while (!stoppingToken.IsCancellationRequested)
            {
                bool shouldUpdate = true;

                try
                {
                    using (var scope = _services.CreateScope())
                    {
                        // 1. Сначала проверяем, когда было последнее обновление в БД
                        var factory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
                        using var context = await factory.CreateDbContextAsync(stoppingToken);

                        // Берем дату самой свежей записи в истории цен
                        var lastRecordDate = await context.MarketHistory
                            .OrderByDescending(x => x.RecordedAt)
                            .Select(x => x.RecordedAt)
                            .FirstOrDefaultAsync(stoppingToken);

                        // Если база не пустая и прошло меньше времени, чем интервал обновления
                        if (lastRecordDate != DateTime.MinValue)
                        {
                            var timeSinceLastUpdate = DateTime.UtcNow - lastRecordDate;
                            if (timeSinceLastUpdate < _updateInterval)
                            {
                                shouldUpdate = false; // Пропускаем обновление

                                // Логируем или обновляем статус для UI
                                var status = scope.ServiceProvider.GetRequiredService<SystemStatusService>();
                                status.LastPriceUpdate = lastRecordDate.ToLocalTime();
                                status.LastError = $"Пропуск обновления. Следующее через {(int)(_updateInterval - timeSinceLastUpdate).TotalMinutes} мин.";
                            }
                        }

                        // 2. Если пришло время обновлять
                        if (shouldUpdate)
                        {
                            var parser = scope.ServiceProvider.GetRequiredService<PriceParserService>();
                            var boost = scope.ServiceProvider.GetRequiredService<BoostService>();
                            var status = scope.ServiceProvider.GetRequiredService<SystemStatusService>();

                            status.IsProcessing = true;

                            // Обновление цен
                            await parser.UpdateAllPricesAsync();
                            status.LastPriceUpdate = DateTime.Now;

                            // Проверка бустов
                            await boost.CheckForBoostsAsync();
                            status.LastBoostCheck = DateTime.Now;

                            status.IsProcessing = false;
                            status.LastError = "Ошибок нет";
                        }
                    }
                }
                catch (Exception ex)
                {
                    using var scope = _services.CreateScope();
                    var status = scope.ServiceProvider.GetRequiredService<SystemStatusService>();
                    status.LogError($"Ошибка фоновой задачи: {ex.Message}");
                    status.IsProcessing = false;
                }

                // Ждем интервал перед следующей проверкой
                await Task.Delay(_updateInterval, stoppingToken);
            }
        }
    }
}