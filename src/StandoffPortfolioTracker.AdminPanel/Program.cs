using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.AdminPanel.Components;
using StandoffPortfolioTracker.Infrastructure;
using StandoffPortfolioTracker.AdminPanel.Services;

var builder = WebApplication.CreateBuilder(args);

// Получаем строку подключения из appsettings.json
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Регистрируем фабрику контекста
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddScoped<StandoffPortfolioTracker.AdminPanel.Services.ItemService>();
builder.Services.AddScoped<StandoffPortfolioTracker.AdminPanel.Services.PortfolioService>();
builder.Services.AddHttpClient<StandoffPortfolioTracker.AdminPanel.Services.PriceParserService>();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run(); 
