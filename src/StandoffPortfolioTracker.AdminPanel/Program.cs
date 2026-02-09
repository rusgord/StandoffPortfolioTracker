using Microsoft.AspNetCore.Components.Authorization; // Нужно для Blazor Auth
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using StandoffPortfolioTracker.AdminPanel.Components;
using StandoffPortfolioTracker.Infrastructure;
using StandoffPortfolioTracker.AdminPanel.Services;
using StandoffPortfolioTracker.AdminPanel.Workers;
using ApexCharts;
using StandoffPortfolioTracker.Core.Entities;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Authentication;

var builder = WebApplication.CreateBuilder(args);

// Строка подключения
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// Контекст БД
builder.Services.AddDbContextFactory<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

// 1. ВАЖНО: Добавляем поддержку каскадной аутентификации для Blazor
builder.Services.AddCascadingAuthenticationState();

// 2. НАСТРОЙКА IDENTITY
builder.Services.AddDefaultIdentity<ApplicationUser>(options =>
{
    options.SignIn.RequireConfirmedAccount = false;
    options.Password.RequireDigit = false; // Для удобства тестов
    options.Password.RequiredLength = 4;
})
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>()
    .AddClaimsPrincipalFactory<CustomUserClaimsPrincipalFactory>();

// 3. НАСТРОЙКА GOOGLE AUTH
builder.Services.AddAuthentication()
    .AddGoogle(googleOptions =>
    {
        googleOptions.ClientId = builder.Configuration["Authentication:Google:ClientId"];
        googleOptions.ClientSecret = builder.Configuration["Authentication:Google:ClientSecret"];
        googleOptions.ClaimActions.MapJsonKey("picture", "picture");
    });

// Твои сервисы
builder.Services.AddScoped<ItemService>();
builder.Services.AddScoped<PortfolioService>();
builder.Services.AddHttpClient<PriceParserService>();
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<WikiParserService>();
builder.Services.AddScoped<BillingService>();
builder.Services.AddApexCharts();
builder.Services.AddScoped<ToastService>();
builder.Services.AddSingleton<GlobalNotificationService>();
builder.Services.AddHostedService<DonationWorker>();

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();
app.MapStaticAssets();
app.UseStaticFiles();
app.UseAntiforgery();

// 4. ВАЖНО: Подключаем Middleware авторизации (порядок важен!)
app.UseAuthentication(); // <-- Обязательно
app.UseAuthorization();  // <-- Обязательно

// 5. ВАЖНО: Добавляем маппинг Razor Pages (нужно для Identity UI: Логин/Регистрация)
app.MapRazorPages();

app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();


using (var scope = app.Services.CreateScope())
{
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();

    // 1. Создаем роль Admin, если её нет
    if (!await roleManager.RoleExistsAsync("Admin"))
    {
        await roleManager.CreateAsync(new IdentityRole("Admin"));
    }

    // 2. Ищем пользователя и выдаем роль
    var myEmail = "rusgord59@gmail.com";
    var adminUser = await userManager.FindByEmailAsync(myEmail);

    if (adminUser != null && !await userManager.IsInRoleAsync(adminUser, "Admin"))
    {
        await userManager.AddToRoleAsync(adminUser, "Admin");
    }
}


app.Run();