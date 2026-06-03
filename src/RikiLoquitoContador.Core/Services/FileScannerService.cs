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
        event Action? OnProcessingQueueChanged;
        System.Collections.Generic.IReadOnlyList<string> GetCurrentlyProcessingFiles();
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
        private readonly ConcurrentDictionary<string, byte> _currentlyProcessing = new();
        private readonly System.Threading.SemaphoreSlim _processingSemaphore = new(1, 1);
        private FileSystemWatcher? _watcher;
        private bool _isMonitoring;

        public bool IsMonitoring => _isMonitoring;
        public event Action<Factura>? OnFileDetected;
        public event Action? OnProcessingQueueChanged;

        public System.Collections.Generic.IReadOnlyList<string> GetCurrentlyProcessingFiles()
        {
            return _currentlyProcessing.Keys.Select(Path.GetFileName).ToList()!;
        }

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

        private string ComputeSha256(string filePath)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            using var stream = File.OpenRead(filePath);
            var hashBytes = sha256.ComputeHash(stream);
            return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
        }

        private string GetLogicalName(AiExtractionResult res, string originalName, string extension)
        {
            try
            {
                string dateStr = res.IssueDate?.ToString("yyyyMMdd") ?? DateTime.Now.ToString("yyyyMMdd");
                string type = CleanStringForFileName(res.InvoiceType ?? "Factura");
                string pos = CleanStringForFileName(res.PointOfSale ?? "0000");
                string number = CleanStringForFileName(res.InvoiceNumber ?? "00000000");
                string emisor = CleanStringForFileName(res.EmisorNombre ?? "SinEmisor");
                string receptor = CleanStringForFileName(res.ReceptorNombre ?? "SinReceptor");
                
                return $"{dateStr}_{type}_{pos}-{number}_E-{emisor}_R-{receptor}{extension}";
            }
            catch
            {
                return $"{Path.GetFileNameWithoutExtension(originalName)}_{DateTime.Now:yyyyMMddHHmmss}{extension}";
            }
        }

        private string GetClientFolderNameAndSubfolder(AiExtractionResult res, ScanningSettings settings, out string subfolder)
        {
            var cuits = (settings.ClientesContadorCuit ?? "")
                .Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => new string(c.Where(char.IsDigit).ToArray()))
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            string emisorCuitClean = new string((res.EmisorCuit ?? "").Where(char.IsDigit).ToArray());
            string receptorCuitClean = new string((res.ReceptorCuit ?? "").Where(char.IsDigit).ToArray());

            if (cuits.Count > 0)
            {
                if (!string.IsNullOrEmpty(emisorCuitClean) && cuits.Contains(emisorCuitClean))
                {
                    subfolder = "Emitidas";
                    return CleanStringForFileName(res.EmisorNombre ?? "SinNombre");
                }
                if (!string.IsNullOrEmpty(receptorCuitClean) && cuits.Contains(receptorCuitClean))
                {
                    subfolder = "Recibidas";
                    return CleanStringForFileName(res.ReceptorNombre ?? "SinNombre");
                }
            }

            // Fallback if none matches or CUIT list is empty
            subfolder = "General";
            return CleanStringForFileName(res.EmisorNombre ?? "SinNombre");
        }

        private string CleanStringForFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return "";
            var invalid = Path.GetInvalidFileNameChars();
            var clean = new string(name.Where(c => !invalid.Contains(c) && c != ' ' && c != '_').ToArray());
            return clean;
        }

        private void MoveFileToFolder(string sourcePath, string destFolder)
        {
            try
            {
                if (!Directory.Exists(destFolder))
                {
                    Directory.CreateDirectory(destFolder);
                }
                string fileName = Path.GetFileName(sourcePath);
                string destPath = Path.Combine(destFolder, fileName);
                if (File.Exists(destPath))
                {
                    destPath = Path.Combine(destFolder, $"{Path.GetFileNameWithoutExtension(fileName)}_{DateTime.Now:yyyyMMddHHmmss}{Path.GetExtension(fileName)}");
                }
                File.Move(sourcePath, destPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error moving file from {Source} to folder {Dest}", sourcePath, destFolder);
            }
        }

        private async Task<Factura?> ProcessAndIndexFileAsync(AppDbContext context, string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return null;

            _currentlyProcessing.TryAdd(filePath, 0);
            OnProcessingQueueChanged?.Invoke();

            string hash = "";
            FileInfo? fileInfo = null;
            var settings = _configService.GetSettings();
            var processedFolder = settings.ScanningSettings.ProcessedFolderPath;
            var conflictsFolder = settings.ScanningSettings.ConflictsFolderPath;

            await _processingSemaphore.WaitAsync();
            try
            {
                if (!File.Exists(filePath)) return null;
                fileInfo = new FileInfo(filePath);
                
                // Compute SHA256 hash
                try
                {
                    hash = ComputeSha256(filePath);
                }
                catch (Exception hashEx)
                {
                    _logger.LogError(hashEx, "Failed to compute hash for file {FilePath}", filePath);
                    MoveFileToFolder(filePath, conflictsFolder);
                    return null;
                }

                // Check if already exists in DB
                var existing = await context.Facturas.FirstOrDefaultAsync(f => f.FileHash == hash);
                if (existing != null)
                {
                    if (existing.Status == "Success")
                    {
                        _logger.LogWarning("Duplicate file detected by hash. Ignoring index: {FileName}", fileInfo.Name);
                        MoveFileToFolder(filePath, conflictsFolder);
                        return null;
                    }
                    else
                    {
                        // Delete old conflict record to reprocess
                        context.Facturas.Remove(existing);
                        await context.SaveChangesAsync();
                    }
                }

                _logger.LogInformation("Analyzing file with AI before indexing: {FileName}", fileInfo.Name);

                var aiResult = await _aiService.AnalyzeInvoiceAsync(fileInfo.FullName);
                
                // Re-verify file existence
                if (!File.Exists(filePath))
                {
                    _logger.LogWarning("File was deleted or moved during AI analysis: {FilePath}", filePath);
                    return null;
                }

                long fileLength = fileInfo.Length;
                DateTime fileCreatedAt = fileInfo.CreationTime;

                bool hasAiError = aiResult.Comments != null && 
                                 (aiResult.Comments.StartsWith("Error de API") || 
                                  aiResult.Comments.StartsWith("Fallo de conexión") ||
                                  aiResult.Comments.Contains("Error de conexión"));

                if (hasAiError || (string.IsNullOrEmpty(aiResult.EmisorNombre) && string.IsNullOrEmpty(aiResult.ReceptorNombre)))
                {
                    throw new InvalidOperationException(aiResult.Comments ?? "La IA no pudo extraer los datos de emisor y receptor.");
                }

                // Move file to client folder under processed
                string clientFolderName = GetClientFolderNameAndSubfolder(aiResult, settings.ScanningSettings, out string subfolder);
                string clientFolder = Path.Combine(processedFolder, clientFolderName, subfolder);
                if (!Directory.Exists(clientFolder))
                {
                    Directory.CreateDirectory(clientFolder);
                }

                string logicalName = GetLogicalName(aiResult, fileInfo.Name, fileInfo.Extension);
                string destinationPath = Path.Combine(clientFolder, logicalName);

                if (File.Exists(destinationPath))
                {
                    string nameWithoutExt = Path.GetFileNameWithoutExtension(logicalName);
                    destinationPath = Path.Combine(clientFolder, $"{nameWithoutExt}_{DateTime.Now:yyyyMMddHHmmss}{fileInfo.Extension}");
                }

                File.Move(filePath, destinationPath);

                var factura = new Factura
                {
                    FileName = Path.GetFileName(destinationPath),
                    FilePath = destinationPath,
                    FileExtension = fileInfo.Extension.ToLowerInvariant(),
                    FileSizeBytes = fileLength,
                    FileCreatedAt = fileCreatedAt,
                    IndexedAt = DateTime.UtcNow,
                    EmisorNombre = aiResult.EmisorNombre,
                    EmisorCuit = aiResult.EmisorCuit,
                    ReceptorNombre = aiResult.ReceptorNombre,
                    ReceptorCuit = aiResult.ReceptorCuit,
                    ReceptorVatType = aiResult.ReceptorVatType,
                    TotalAmount = aiResult.TotalAmount,
                    Comments = aiResult.Comments ?? string.Empty,
                    
                    FileHash = hash,
                    InvoiceType = aiResult.InvoiceType,
                    PointOfSale = aiResult.PointOfSale,
                    InvoiceNumber = aiResult.InvoiceNumber,
                    IssueDate = aiResult.IssueDate,
                    ProcessedPath = destinationPath,
                    Status = "Success",
                    ErrorMessage = null
                };

                // Add details
                if (aiResult.Items != null)
                {
                    foreach (var item in aiResult.Items)
                    {
                        factura.Detalles.Add(new FacturaDetalle
                        {
                            Description = item.Description,
                            Quantity = item.Quantity,
                            UnitPrice = item.UnitPrice,
                            Subtotal = item.Subtotal,
                            VatRate = item.VatRate
                        });
                    }
                }

                context.Facturas.Add(factura);
                await context.SaveChangesAsync();
                _logger.LogInformation("Indexed new invoice: {FileName} with emisor {EmisorName}, receptor {ReceptorName} and total {TotalAmount}", 
                    factura.FileName, factura.EmisorNombre, factura.ReceptorNombre, factura.TotalAmount);

                // Auto-sync to Excel
                var excelPath = settings.ScanningSettings.ExcelFilePath;
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

                try
                {
                    if (File.Exists(filePath) && fileInfo != null)
                    {
                        long fileLength = fileInfo.Length;
                        DateTime fileCreatedAt = fileInfo.CreationTime;

                        string conflictPath = Path.Combine(conflictsFolder, fileInfo.Name);
                        if (File.Exists(conflictPath))
                        {
                            conflictPath = Path.Combine(conflictsFolder, $"{Path.GetFileNameWithoutExtension(fileInfo.Name)}_{DateTime.Now:yyyyMMddHHmmss}{fileInfo.Extension}");
                        }
                        File.Move(filePath, conflictPath);

                        var factura = new Factura
                        {
                            FileName = Path.GetFileName(conflictPath),
                            FilePath = conflictPath,
                            FileExtension = fileInfo.Extension.ToLowerInvariant(),
                            FileSizeBytes = fileLength,
                            FileCreatedAt = fileCreatedAt,
                            IndexedAt = DateTime.UtcNow,
                            EmisorNombre = "Fallo IA",
                            TotalAmount = null,
                            Comments = ex.Message,
                            
                            FileHash = string.IsNullOrEmpty(hash) ? Guid.NewGuid().ToString() : hash,
                            ProcessedPath = null,
                            Status = "Conflict",
                            ErrorMessage = ex.Message
                        };

                        context.Facturas.Add(factura);
                        await context.SaveChangesAsync();

                        return factura;
                    }
                }
                catch (Exception moveEx)
                {
                    _logger.LogError(moveEx, "Failed to move file to conflicts folder: {FilePath}", filePath);
                }

                return null;
            }
            finally
            {
                _processingSemaphore.Release();
                _currentlyProcessing.TryRemove(filePath, out _);
                OnProcessingQueueChanged?.Invoke();
            }
        }

        public void Dispose()
        {
            StopMonitoring();
        }
    }
}
