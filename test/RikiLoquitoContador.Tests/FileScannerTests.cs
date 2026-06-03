using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using RikiLoquitoContador.Core.Data;
using RikiLoquitoContador.Core.Models;
using RikiLoquitoContador.Core.Services;
using Xunit;

namespace RikiLoquitoContador.Tests
{
    public class FileScannerTests : IDisposable
    {
        private readonly string _testFolder;
        private readonly SqliteConnection _connection;
        private readonly DbContextOptions<AppDbContext> _dbOptions;
        private readonly TestDbContextFactory _dbFactory;

        public FileScannerTests()
        {
            // Set up a temporary folder
            _testFolder = Path.Combine(Path.GetTempPath(), "RikiLoquitoTest_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_testFolder);

            // Set up an in-memory SQLite database
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();

            _dbOptions = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlite(_connection)
                .Options;

            using (var context = new AppDbContext(_dbOptions))
            {
                context.Database.EnsureCreated();
            }

            _dbFactory = new TestDbContextFactory(_dbOptions);
        }

        [Fact]
        public async Task ScanFolderAsync_ShouldIndexAllowedFilesAndIgnoreTxt()
        {
            // Arrange
            // Create dummy files
            File.WriteAllText(Path.Combine(_testFolder, "factura_cliente_a.pdf"), "Dummy PDF content");
            File.WriteAllText(Path.Combine(_testFolder, "factura_cliente_b.png"), "Dummy PNG content");
            File.WriteAllText(Path.Combine(_testFolder, "documento_ignorado.txt"), "This should be ignored");

            var inMemorySettings = new Dictionary<string, string?> {
                {"ScanningSettings:WatchFolderPath", _testFolder},
                {"ScanningSettings:ScanIntervalSeconds", "10"}
            };
            IConfiguration configuration = new ConfigurationBuilder()
                .AddInMemoryCollection(inMemorySettings)
                .Build();

            var configService = new ConfigService(configuration);
            var scanner = new FileScannerService(
                configService, 
                _dbFactory, 
                new FakeAiService(), 
                new FakeExportService(), 
                NullLogger<FileScannerService>.Instance);

            // Act
            await scanner.ScanFolderAsync();

            // Assert
            using var context = new AppDbContext(_dbOptions);
            var facturas = await context.Facturas.ToListAsync();

            Assert.Equal(2, facturas.Count);
            
            var pdfFactura = facturas.FirstOrDefault(f => f.FileExtension == ".pdf");
            Assert.NotNull(pdfFactura);
            Assert.Equal(".pdf", pdfFactura.FileExtension);
            Assert.Equal("Factura", pdfFactura.ClientName); // By client name extraction heuristic "factura"

            var txtFactura = facturas.FirstOrDefault(f => f.FileName == "documento_ignorado.txt");
            Assert.Null(txtFactura); // TXT should be ignored
        }

        public void Dispose()
        {
            _connection.Close();
            _connection.Dispose();

            if (Directory.Exists(_testFolder))
            {
                Directory.Delete(_testFolder, recursive: true);
            }
        }
    }

    public class TestDbContextFactory : IDbContextFactory<AppDbContext>
    {
        private readonly DbContextOptions<AppDbContext> _options;

        public TestDbContextFactory(DbContextOptions<AppDbContext> options)
        {
            _options = options;
        }

        public AppDbContext CreateDbContext()
        {
            return new AppDbContext(_options);
        }
    }

    public class FakeAiService : IAiService
    {
        public Task<AiExtractionResult> AnalyzeInvoiceAsync(string filePath)
        {
            var name = Path.GetFileNameWithoutExtension(filePath);
            var parts = name.Split('_');
            var client = parts.Length > 0 ? parts[0] : "General";
            if (client.Length > 0)
            {
                client = char.ToUpper(client[0]) + client.Substring(1).ToLower();
            }
            return Task.FromResult(new AiExtractionResult
            {
                ClientName = client,
                TotalAmount = 100.00m,
                Comments = "Comentario de prueba",
                InvoiceType = "Factura C",
                PointOfSale = "0001",
                InvoiceNumber = "00000001",
                IssueDate = DateTime.Today,
                ClientCuit = "20301018135",
                ClientVatType = "Monotributista",
                Items = new List<FacturaDetalleDto>
                {
                    new FacturaDetalleDto { Description = "Item 1", Quantity = 1.0m, UnitPrice = 100.00m, Subtotal = 100.00m, VatRate = 0.0m }
                }
            });
        }

        public Task<List<string>> GetAvailableModelsAsync(string provider, string endpoint)
        {
            return Task.FromResult(new List<string> { "llama3", "mistral" });
        }
    }

    public class FakeExportService : IExportService
    {
        public byte[] ExportToCsv(IEnumerable<Factura> facturas) => Array.Empty<byte>();
        public byte[] ExportToJson(IEnumerable<Factura> facturas) => Array.Empty<byte>();
        public Task ExportToExcelIncrementalAsync(IEnumerable<Factura> facturas, string filePath) => Task.CompletedTask;
    }
}
