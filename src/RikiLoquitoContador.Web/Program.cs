using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using RikiLoquitoContador.Core.Data;
using RikiLoquitoContador.Core.Services;
using RikiLoquitoContador.RazorLib.Services;
using RikiLoquitoContador.Web.Components;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

// Add MudBlazor Services
builder.Services.AddMudServices();

// Add Configuration & Core Services
builder.Services.AddSingleton<IConfigService, ConfigService>();
builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfigService>();
    options.UseSqlite(config.GetSettings().ConnectionStrings.DefaultConnection);
});

builder.Services.AddSingleton<IFileScannerService, FileScannerService>();
builder.Services.AddSingleton<IExportService, ExportService>();
builder.Services.AddSingleton<II18nService, I18nService>();
builder.Services.AddScoped<ISessionService, SessionService>();
builder.Services.AddSingleton<IAiService, AiService>();

var app = builder.Build();

// Auto-initialize the database schema
using (var scope = app.Services.CreateScope())
{
    var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
    using var db = dbContextFactory.CreateDbContext();
    db.Database.EnsureCreated();
    RikiLoquitoContador.Core.Data.DbInitializer.Seed(db);
}

// Configure the HTTP request pipeline.
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
    app.UseHsts();
}
app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
app.UseHttpsRedirection();

app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
