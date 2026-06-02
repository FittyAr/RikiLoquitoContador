using System;
using System.IO;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using RikiLoquitoContador.Core.Models;

namespace RikiLoquitoContador.Core.Services
{
    public interface IConfigService
    {
        AppSettings GetSettings();
        void UpdateSettings(AppSettings settings);
        bool VerifyPassword(string inputPassword);
        string HashPassword(string plainPassword);
    }

    public class ConfigService : IConfigService
    {
        private readonly IConfiguration _configuration;
        private AppSettings _cachedSettings;
        private readonly string _settingsFilePath;

        public ConfigService(IConfiguration configuration)
        {
            _configuration = configuration;
            _cachedSettings = new AppSettings();
            
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            
#if DEBUG
            // Find root containing solution (.sln or .slnx)
            string? dir = baseDir;
            while (dir != null && 
                   !File.Exists(Path.Combine(dir, "RikiLoquitoContador.sln")) && 
                   !File.Exists(Path.Combine(dir, "RikiLoquitoContador.slnx")))
            {
                dir = Path.GetDirectoryName(dir);
            }
            string workspaceDir = dir ?? baseDir;
            
            var pathsToTry = new[]
            {
                Path.Combine(workspaceDir, "Config", "appsettings.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "Config", "appsettings.json"),
                Path.Combine(baseDir, "appsettings.json"),
                Path.Combine(baseDir, "Config", "appsettings.json"),
                Path.Combine(Directory.GetCurrentDirectory(), "appsettings.json")
            };

            _settingsFilePath = pathsToTry[0];
            foreach (var path in pathsToTry)
            {
                if (File.Exists(path))
                {
                    _settingsFilePath = path;
                    break;
                }
            }
#else
            // In production, save in %programdata%/RikiLoquitoContador/appsettings.json
            string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
            _settingsFilePath = Path.Combine(programData, "RikiLoquitoContador", "appsettings.json");

            // Write default configuration if it does not exist
            if (!File.Exists(_settingsFilePath))
            {
                try
                {
                    var directory = Path.GetDirectoryName(_settingsFilePath);
                    if (directory != null && !Directory.Exists(directory))
                    {
                        Directory.CreateDirectory(directory);
                    }

                    var defaultSettings = new AppSettings();
                    // Default to 'contador123'
                    defaultSettings.SecuritySettings.PasswordHash = "$2a$11$9/X4yDqC3G3bYfCdfp/juef6u8bQ/bK1dM1oF3L0H5U1tT8rP/Cxe";
                    defaultSettings.ScanningSettings.WatchFolderPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "FacturasContador");
                    defaultSettings.ScanningSettings.ScanIntervalSeconds = 10;
                    defaultSettings.ConnectionStrings.DefaultConnection = $"Data Source={Path.Combine(directory ?? "", "facturas.db")}";

                    var json = JsonSerializer.Serialize(defaultSettings, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_settingsFilePath, json);
                }
                catch
                {
                    // Fallback silently if unable to write
                }
            }
#endif

            LoadSettings();
        }

        private void LoadSettings()
        {
            try
            {
                Console.WriteLine($"[ConfigService] Loading settings from: {_settingsFilePath}");
                if (File.Exists(_settingsFilePath))
                {
                    var json = File.ReadAllText(_settingsFilePath);
                    var settings = JsonSerializer.Deserialize<AppSettings>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (settings != null)
                    {
                        _cachedSettings = settings;
                        Console.WriteLine($"[ConfigService] Loaded settings. Hash: '{_cachedSettings.SecuritySettings.PasswordHash}'");
                        return;
                    }
                }
            }
            catch
            {
                // Fallback to IConfiguration if reading file fails
            }

            // Bind from DI Configuration
            _cachedSettings.ConnectionStrings.DefaultConnection = _configuration.GetConnectionString("DefaultConnection") ?? "Data Source=facturas.db";
            _cachedSettings.SecuritySettings.PasswordHash = _configuration["SecuritySettings:PasswordHash"] ?? string.Empty;
            _cachedSettings.ScanningSettings.WatchFolderPath = _configuration["ScanningSettings:WatchFolderPath"] ?? string.Empty;
            
            if (int.TryParse(_configuration["ScanningSettings:ScanIntervalSeconds"], out int interval))
            {
                _cachedSettings.ScanningSettings.ScanIntervalSeconds = interval;
            }
            else
            {
                _cachedSettings.ScanningSettings.ScanIntervalSeconds = 10;
            }

            _cachedSettings.AiSettings.Provider = _configuration["AiSettings:Provider"] ?? "OpenAI";
            _cachedSettings.AiSettings.Endpoint = _configuration["AiSettings:Endpoint"] ?? "https://api.openai.com/v1/chat/completions";
            _cachedSettings.AiSettings.ModelName = _configuration["AiSettings:ModelName"] ?? "gpt-4o-mini";
            _cachedSettings.AiSettings.ApiKey = _configuration["AiSettings:ApiKey"] ?? string.Empty;
        }

        public AppSettings GetSettings()
        {
            // Refresh settings before returning in case they were modified externally
            LoadSettings();
            return _cachedSettings;
        }

        public void UpdateSettings(AppSettings settings)
        {
            _cachedSettings = settings;
            try
            {
                var directory = Path.GetDirectoryName(_settingsFilePath);
                if (directory != null && !Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var json = JsonSerializer.Serialize(_cachedSettings, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                File.WriteAllText(_settingsFilePath, json);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"Error saving configuration: {ex.Message}", ex);
            }
        }

        public bool VerifyPassword(string inputPassword)
        {
            var hash = GetSettings().SecuritySettings.PasswordHash;
            Console.WriteLine($"[Auth] Verify. Input: '{inputPassword}', Hash: '{hash}'");
            if (string.IsNullOrEmpty(hash))
            {
                Console.WriteLine("[Auth] PasswordHash is empty!");
                return false;
            }
            try
            {
                bool result = BCrypt.Net.BCrypt.Verify(inputPassword, hash);
                Console.WriteLine($"[Auth] BCrypt verification result: {result}");
                return result;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Auth] BCrypt verification exception: {ex.Message}");
                return false;
            }
        }

        public string HashPassword(string plainPassword)
        {
            return BCrypt.Net.BCrypt.HashPassword(plainPassword, workFactor: 11);
        }
    }
}
