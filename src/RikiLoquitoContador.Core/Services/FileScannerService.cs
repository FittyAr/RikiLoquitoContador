using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RikiLoquitoContador.Core.Data;
using RikiLoquitoContador.Core.Models;

namespace RikiLoquitoContador.Core.Services
{
    public interface IFileScannerService : IDisposable
    {
        bool IsMonitoring { get; }
        event Action<Factura>? OnFileDetected;
        Task ScanFolderAsync();
        void StartMonitoring();
        void StopMonitoring();
    }

    public class FileScannerService : IFileScannerService
    {
        private readonly IConfigService _configService;
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly ILogger<FileScannerService> _logger;
        private FileSystemWatcher? _watcher;
        private bool _isMonitoring;

        public bool IsMonitoring => _isMonitoring;
        public event Action<Factura>? OnFileDetected;

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".pdf", ".doc", ".docx" };

        public FileScannerService(
            IConfigService configService,
            IDbContextFactory<AppDbContext> dbContextFactory,
            ILogger<FileScannerService> logger)
        {
            _configService = configService;
            _dbContextFactory = dbContextFactory;
            _logger = logger;
        }

        public async Task ScanFolderAsync()
        {
            var settings = _configService.GetSettings();
            var folderPath = settings.ScanningSettings.WatchFolderPath;

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                _logger.LogWarning("Watch folder '{FolderPath}' does not exist or is not configured.", folderPath);
                return;
            }

            try
            {
                var files = Directory.EnumerateFiles(folderPath, "*.*", SearchOption.TopDirectoryOnly)
                    .Where(file => AllowedExtensions.Contains(Path.GetExtension(file).ToLowerInvariant()));

                using var context = await _dbContextFactory.CreateDbContextAsync();

                foreach (var file in files)
                {
                    await ProcessAndIndexFileAsync(context, file);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error occurred while scanning folder '{FolderPath}'", folderPath);
            }
        }

        public void StartMonitoring()
        {
            if (_isMonitoring) return;

            var settings = _configService.GetSettings();
            var folderPath = settings.ScanningSettings.WatchFolderPath;

            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
            {
                _logger.LogWarning("Cannot start monitoring. Folder '{FolderPath}' does not exist.", folderPath);
                return;
            }

            try
            {
                _watcher = new FileSystemWatcher(folderPath)
                {
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.CreationTime,
                    Filter = "*.*",
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };

                _watcher.Created += OnFileCreatedOrChanged;
                _watcher.Changed += OnFileCreatedOrChanged;
                _watcher.Renamed += OnFileRenamed;

                _isMonitoring = true;
                _logger.LogInformation("Started monitoring folder '{FolderPath}'", folderPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to start monitoring folder '{FolderPath}'", folderPath);
            }
        }

        public void StopMonitoring()
        {
            if (!_isMonitoring) return;

            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Created -= OnFileCreatedOrChanged;
                _watcher.Changed -= OnFileCreatedOrChanged;
                _watcher.Renamed -= OnFileRenamed;
                _watcher.Dispose();
                _watcher = null;
            }

            _isMonitoring = false;
            _logger.LogInformation("Stopped folder monitoring.");
        }

        private void OnFileCreatedOrChanged(object sender, FileSystemEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext)) return;

            // Run in background task to avoid blocking the FileSystemWatcher thread
            Task.Run(async () =>
            {
                // Wait briefly for file write to complete
                await Task.Delay(500);
                try
                {
                    using var context = await _dbContextFactory.CreateDbContextAsync();
                    var factura = await ProcessAndIndexFileAsync(context, e.FullPath);
                    if (factura != null)
                    {
                        OnFileDetected?.Invoke(factura);
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing event for file {FilePath}", e.FullPath);
                }
            });
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext)) return;

            Task.Run(async () =>
            {
                try
                {
                    using var context = await _dbContextFactory.CreateDbContextAsync();
                    // Update matching old file path if it exists
                    var existing = await context.Facturas.FirstOrDefaultAsync(f => f.FilePath == e.OldFullPath);
                    if (existing != null)
                    {
                        existing.FilePath = e.FullPath;
                        existing.FileName = e.Name ?? Path.GetFileName(e.FullPath);
                        await context.SaveChangesAsync();
                        OnFileDetected?.Invoke(existing);
                    }
                    else
                    {
                        var factura = await ProcessAndIndexFileAsync(context, e.FullPath);
                        if (factura != null)
                        {
                            OnFileDetected?.Invoke(factura);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error processing rename event from {OldPath} to {NewPath}", e.OldFullPath, e.FullPath);
                }
            });
        }

        private async Task<Factura?> ProcessAndIndexFileAsync(AppDbContext context, string filePath)
        {
            try
            {
                if (!File.Exists(filePath)) return null;

                // Check if already indexed by path
                var existing = await context.Facturas.FirstOrDefaultAsync(f => f.FilePath == filePath);
                if (existing != null) return null;

                var fileInfo = new FileInfo(filePath);
                
                var factura = new Factura
                {
                    FileName = fileInfo.Name,
                    FilePath = fileInfo.FullName,
                    FileExtension = fileInfo.Extension.ToLowerInvariant(),
                    FileSizeBytes = fileInfo.Length,
                    FileCreatedAt = fileInfo.CreationTime,
                    IndexedAt = DateTime.UtcNow,
                    // Basic heuristic for clients: filename before first space or hyphen, or "Desconocido"
                    ClientName = ExtractClientNameHeuristic(fileInfo.Name),
                    TotalAmount = null,
                    Comments = string.Empty
                };

                context.Facturas.Add(factura);
                await context.SaveChangesAsync();
                _logger.LogInformation("Indexed new invoice: {FileName}", factura.FileName);
                return factura;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index file {FilePath}", filePath);
                return null;
            }
        }

        private string ExtractClientNameHeuristic(string fileName)
        {
            var nameWithoutExt = Path.GetFileNameWithoutExtension(fileName);
            var parts = nameWithoutExt.Split(new[] { ' ', '-', '_' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length > 0 && parts[0].Length > 2)
            {
                // Capitalize first part
                return char.ToUpper(parts[0][0]) + parts[0].Substring(1).ToLowerInvariant();
            }
            return "General";
        }

        public void Dispose()
        {
            StopMonitoring();
        }
    }
}
