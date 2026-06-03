using System;
using System.Collections.Concurrent;
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
        private readonly IAiService _aiService;
        private readonly IExportService _exportService;
        private readonly ILogger<FileScannerService> _logger;
        private readonly ConcurrentDictionary<string, byte> _processingFiles = new();
        private FileSystemWatcher? _watcher;
        private bool _isMonitoring;

        public bool IsMonitoring => _isMonitoring;
        public event Action<Factura>? OnFileDetected;

        private static readonly string[] AllowedExtensions = { ".jpg", ".jpeg", ".png", ".pdf", ".doc", ".docx" };

        public FileScannerService(
            IConfigService configService,
            IDbContextFactory<AppDbContext> dbContextFactory,
            IAiService aiService,
            IExportService exportService,
            ILogger<FileScannerService> logger)
        {
            _configService = configService;
            _dbContextFactory = dbContextFactory;
            _aiService = aiService;
            _exportService = exportService;
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

            // Prevent concurrent runs for the exact same file path (e.g. concurrent Created and Changed events)
            if (!_processingFiles.TryAdd(e.FullPath, 0)) return;

            // Run in background task to avoid blocking the FileSystemWatcher thread
            Task.Run(async () =>
            {
                try
                {
                    // Wait a bit longer (1000ms) to ensure file writes are complete and file handles released
                    await Task.Delay(1000);
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
                finally
                {
                    _processingFiles.TryRemove(e.FullPath, out _);
                }
            });
        }

        private void OnFileRenamed(object sender, RenamedEventArgs e)
        {
            var ext = Path.GetExtension(e.FullPath).ToLowerInvariant();
            if (!AllowedExtensions.Contains(ext)) return;

            if (!_processingFiles.TryAdd(e.FullPath, 0)) return;

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
                finally
                {
                    _processingFiles.TryRemove(e.FullPath, out _);
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
                _logger.LogInformation("Analyzing file with AI before indexing: {FileName}", fileInfo.Name);

                var (clientName, totalAmount, comments) = await _aiService.AnalyzeInvoiceAsync(fileInfo.FullName);
                
                // Re-verify file existence after the long-running AI request to prevent FileNotFoundException
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("File was deleted or moved during AI analysis: {FilePath}", filePath);
                    return null;
                }
                
                var factura = new Factura
                {
                    FileName = fileInfo.Name,
                    FilePath = fileInfo.FullName,
                    FileExtension = fileInfo.Extension.ToLowerInvariant(),
                    FileSizeBytes = fileInfo.Length,
                    FileCreatedAt = fileInfo.CreationTime,
                    IndexedAt = DateTime.UtcNow,
                    ClientName = clientName ?? "Fallo IA",
                    TotalAmount = totalAmount,
                    Comments = comments ?? string.Empty
                };

                context.Facturas.Add(factura);
                await context.SaveChangesAsync();
                _logger.LogInformation("Indexed new invoice: {FileName} with client {ClientName} and total {TotalAmount}", 
                    factura.FileName, factura.ClientName, factura.TotalAmount);

                // Auto-sync to Excel immediately if configured
                var settings = _configService.GetSettings();
                var excelPath = settings.ScanningSettings.ExcelFilePath;
                if (string.IsNullOrWhiteSpace(excelPath))
                {
                    var folder = settings.ScanningSettings.WatchFolderPath;
                    if (!string.IsNullOrWhiteSpace(folder))
                    {
                        excelPath = Path.Combine(folder, "FacturasSincronizadas.xlsx");
                    }
                }

                if (!string.IsNullOrWhiteSpace(excelPath))
                {
                    try
                    {
                        await _exportService.ExportToExcelIncrementalAsync(new[] { factura }, excelPath);
                        _logger.LogInformation("Auto-synchronized new invoice {FileName} to Excel.", factura.FileName);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to auto-synchronize new invoice {FileName} to Excel", factura.FileName);
                    }
                }

                return factura;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to index file {FilePath}", filePath);
                return null;
            }
        }

        public void Dispose()
        {
            StopMonitoring();
        }
    }
}
