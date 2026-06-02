using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using MudBlazor.Services;
using RikiLoquitoContador.Core.Data;
using RikiLoquitoContador.Core.Services;
using RikiLoquitoContador.RazorLib.Services;

namespace RikiLoquitoContador.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
			});

		builder.Services.AddMauiBlazorWebView();

		// Add MudBlazor Services
		builder.Services.AddMudServices();

		// Create standard configuration instance
		var config = new ConfigurationBuilder().Build();
		builder.Services.AddSingleton<IConfiguration>(config);

		// Core Services
		builder.Services.AddSingleton<IConfigService, ConfigService>();
		builder.Services.AddDbContextFactory<AppDbContext>((sp, options) =>
		{
			var configService = sp.GetRequiredService<IConfigService>();
			options.UseSqlite(configService.GetSettings().ConnectionStrings.DefaultConnection);
		});
		builder.Services.AddSingleton<IFileScannerService, FileScannerService>();
		builder.Services.AddSingleton<IExportService, ExportService>();
		builder.Services.AddSingleton<II18nService, I18nService>();
		builder.Services.AddScoped<ISessionService, SessionService>();

#if DEBUG
		builder.Services.AddBlazorWebViewDeveloperTools();
		builder.Logging.AddDebug();
#endif

		var app = builder.Build();

		// Auto-initialize the SQLite database schema
		using (var scope = app.Services.CreateScope())
		{
			var dbContextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<AppDbContext>>();
			using var db = dbContextFactory.CreateDbContext();
			db.Database.EnsureCreated();
		}

		return app;
	}
}
