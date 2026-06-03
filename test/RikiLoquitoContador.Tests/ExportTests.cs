using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using ClosedXML.Excel;
using RikiLoquitoContador.Core.Models;
using RikiLoquitoContador.Core.Services;
using Xunit;

namespace RikiLoquitoContador.Tests
{
    public class ExportTests : IDisposable
    {
        private readonly string _testExcelFile;

        public ExportTests()
        {
            _testExcelFile = Path.Combine(Path.GetTempPath(), $"InvoicesTest_{Guid.NewGuid():N}.xlsx");
        }

        [Fact]
        public void ExportToCsv_ShouldReturnValidCsvBytes()
        {
            // Arrange
            var service = new ExportService();
            var facturas = new List<Factura>
            {
                new() { Id = 1, FileHash = "hash1", FileName = "factura1.pdf", EmisorNombre = "Acme", ReceptorNombre = "Receptor1", TotalAmount = 1500.50m },
                new() { Id = 2, FileHash = "hash2", FileName = "factura2.png", EmisorNombre = "Globex", ReceptorNombre = "Receptor2", TotalAmount = 2500m }
            };

            // Act
            var bytes = service.ExportToCsv(facturas);
            var csvString = System.Text.Encoding.UTF8.GetString(bytes);

            // Assert
            Assert.NotNull(bytes);
            Assert.Contains("ID,Nombre de Archivo,Ruta de Archivo", csvString);
            Assert.Contains("factura1.pdf", csvString);
            Assert.Contains("Acme", csvString);
            Assert.Contains("1500.50", csvString);
        }

        [Fact]
        public void ExportToJson_ShouldReturnValidJson()
        {
            // Arrange
            var service = new ExportService();
            var facturas = new List<Factura>
            {
                new() { Id = 1, FileHash = "hash1", FileName = "factura1.pdf", EmisorNombre = "Acme", TotalAmount = 1500.50m }
            };

            // Act
            var bytes = service.ExportToJson(facturas);
            var deserialized = JsonSerializer.Deserialize<List<Factura>>(bytes);

            // Assert
            Assert.NotNull(bytes);
            Assert.NotNull(deserialized);
            Assert.Single(deserialized);
            Assert.Equal("factura1.pdf", deserialized[0].FileName);
        }

        [Fact]
        public async Task ExportToExcelIncremental_ShouldCreateFileAndAppendCorrectly()
        {
            // Arrange
            var service = new ExportService();
            var initialFacturas = new List<Factura>
            {
                new() { Id = 1, FileHash = "hash1", FileName = "factura1.pdf", EmisorNombre = "Acme", TotalAmount = 100m, FileSizeBytes = 1024 },
                new() { Id = 2, FileHash = "hash2", FileName = "factura2.png", EmisorNombre = "Globex", TotalAmount = 200m, FileSizeBytes = 2048 }
            };

            // Act - First export (Creates file)
            await service.ExportToExcelIncrementalAsync(initialFacturas, _testExcelFile);

            // Assert - First export
            Assert.True(File.Exists(_testExcelFile));
            using (var workbook = new XLWorkbook(_testExcelFile))
            {
                var sheet = workbook.Worksheet(1);
                var rowCount = sheet.LastRowUsed()?.RowNumber() ?? 0;
                Assert.Equal(3, rowCount); // 1 Header row + 2 data rows
                
                var firstHash = sheet.Cell(2, 1).Value.GetText();
                Assert.Equal("hash1", firstHash);
            }

            // Arrange - Add one new factura and modify list
            var updatedFacturas = new List<Factura>
            {
                new() { Id = 1, FileHash = "hash1", FileName = "factura1.pdf", EmisorNombre = "Acme", TotalAmount = 100m, FileSizeBytes = 1024 },
                new() { Id = 2, FileHash = "hash2", FileName = "factura2.png", EmisorNombre = "Globex", TotalAmount = 200m, FileSizeBytes = 2048 },
                new() { Id = 3, FileHash = "hash3", FileName = "factura3.pdf", EmisorNombre = "Stark", TotalAmount = 300m, FileSizeBytes = 3072 } // New
            };

            // Act - Second export (Appends only the new one)
            await service.ExportToExcelIncrementalAsync(updatedFacturas, _testExcelFile);

            // Assert - Second export (Total rows should be 4: 1 Header + 3 data rows)
            using (var workbook = new XLWorkbook(_testExcelFile))
            {
                var sheet = workbook.Worksheet(1);
                var rowCount = sheet.LastRowUsed()?.RowNumber() ?? 0;
                Assert.Equal(4, rowCount);

                var thirdHash = sheet.Cell(4, 1).Value.GetText();
                Assert.Equal("hash3", thirdHash);
                
                var thirdEmisor = sheet.Cell(4, 4).Value.GetText();
                Assert.Equal("Stark", thirdEmisor);
            }
        }

        public void Dispose()
        {
            if (File.Exists(_testExcelFile))
            {
                File.Delete(_testExcelFile);
            }
        }
    }
}
