using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using ClosedXML.Excel;
using CsvHelper;
using RikiLoquitoContador.Core.Models;

namespace RikiLoquitoContador.Core.Services
{
    public interface IExportService
    {
        byte[] ExportToCsv(IEnumerable<Factura> facturas);
        byte[] ExportToJson(IEnumerable<Factura> facturas);
        Task ExportToExcelIncrementalAsync(IEnumerable<Factura> facturas, string filePath);
    }

    public class ExportService : IExportService
    {
        private static readonly SemaphoreSlim _excelSemaphore = new(1, 1);
        public byte[] ExportToCsv(IEnumerable<Factura> facturas)
        {
            using var memoryStream = new MemoryStream();
            using var writer = new StreamWriter(memoryStream);
            using var csv = new CsvWriter(writer, CultureInfo.InvariantCulture);
            
            // Map columns for Spanish output
            csv.WriteField("ID");
            csv.WriteField("Nombre de Archivo");
            csv.WriteField("Ruta de Archivo");
            csv.WriteField("Extension");
            csv.WriteField("Tamano (Bytes)");
            csv.WriteField("Fecha Creacion");
            csv.WriteField("Fecha Indexado");
            csv.WriteField("Cliente");
            csv.WriteField("Monto Total");
            csv.WriteField("Comentarios");
            csv.NextRecord();

            foreach (var f in facturas)
            {
                csv.WriteField(f.Id);
                csv.WriteField(f.FileName);
                csv.WriteField(f.FilePath);
                csv.WriteField(f.FileExtension);
                csv.WriteField(f.FileSizeBytes);
                csv.WriteField(f.FileCreatedAt.ToString("g"));
                csv.WriteField(f.IndexedAt.ToString("g"));
                csv.WriteField(f.ClientName ?? "");
                csv.WriteField(f.TotalAmount?.ToString("F2", CultureInfo.InvariantCulture) ?? "");
                csv.WriteField(f.Comments ?? "");
                csv.NextRecord();
            }

            writer.Flush();
            return memoryStream.ToArray();
        }

        public byte[] ExportToJson(IEnumerable<Factura> facturas)
        {
            var options = new JsonSerializerOptions 
            { 
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            };
            return JsonSerializer.SerializeToUtf8Bytes(facturas, options);
        }

        public async Task ExportToExcelIncrementalAsync(IEnumerable<Factura> facturas, string filePath)
        {
            await _excelSemaphore.WaitAsync();
            try
            {
                int maxRetries = 5;
                int delayMs = 1000;

                for (int attempt = 0; attempt < maxRetries; attempt++)
                {
                    try
                    {
                        XLWorkbook workbook;
                        IXLWorksheet worksheet;
                        HashSet<int> existingIds = new();

                        if (File.Exists(filePath))
                        {
                            // Open existing workbook
                            try
                            {
                                workbook = new XLWorkbook(filePath);
                                worksheet = workbook.Worksheet(1) ?? workbook.Worksheets.Add("Facturas");
                            }
                            catch
                            {
                                // If file is corrupted, create a new one
                                workbook = new XLWorkbook();
                                worksheet = workbook.Worksheets.Add("Facturas");
                            }
                        }
                        else
                        {
                            workbook = new XLWorkbook();
                            worksheet = workbook.Worksheets.Add("Facturas");
                        }

                        // If worksheet is empty (new workbook), write headers
                        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
                        if (lastRow == 0)
                        {
                            string[] headers = {
                                "ID", "Nombre de Archivo", "Ruta de Archivo", "Extension", 
                                "Tamano (KB)", "Fecha Creacion", "Fecha Indexado", 
                                "Cliente", "Monto Total", "Comentarios"
                            };

                            for (int i = 0; i < headers.Length; i++)
                            {
                                worksheet.Cell(1, i + 1).Value = headers[i];
                                worksheet.Cell(1, i + 1).Style.Font.Bold = true;
                                worksheet.Cell(1, i + 1).Style.Fill.BackgroundColor = XLColor.LightBlue;
                            }
                            lastRow = 1;
                        }
                        else
                        {
                            // Read existing IDs to prevent duplicates
                            for (int r = 2; r <= lastRow; r++)
                            {
                                var idCell = worksheet.Cell(r, 1).Value;
                                if (idCell.IsNumber)
                                {
                                    existingIds.Add((int)idCell.GetNumber());
                                }
                            }
                        }

                        // Append only new items
                        int writeRow = lastRow + 1;
                        bool addedAny = false;

                        foreach (var f in facturas)
                        {
                            if (existingIds.Contains(f.Id))
                            {
                                continue; // Skip already exported invoice
                            }

                            worksheet.Cell(writeRow, 1).Value = f.Id;
                            worksheet.Cell(writeRow, 2).Value = f.FileName;
                            worksheet.Cell(writeRow, 3).Value = f.FilePath;
                            worksheet.Cell(writeRow, 4).Value = f.FileExtension;
                            worksheet.Cell(writeRow, 5).Value = Math.Round((double)f.FileSizeBytes / 1024.0, 2);
                            worksheet.Cell(writeRow, 6).Value = f.FileCreatedAt;
                            worksheet.Cell(writeRow, 7).Value = f.IndexedAt;
                            worksheet.Cell(writeRow, 8).Value = f.ClientName ?? "General";
                            
                            if (f.TotalAmount.HasValue)
                            {
                                worksheet.Cell(writeRow, 9).Value = (double)f.TotalAmount.Value;
                                worksheet.Cell(writeRow, 9).Style.NumberFormat.Format = "$#,##0.00";
                            }
                            else
                            {
                                worksheet.Cell(writeRow, 9).Value = "";
                            }
                            
                            worksheet.Cell(writeRow, 10).Value = f.Comments ?? "";

                            writeRow++;
                            addedAny = true;
                        }

                        if (addedAny)
                        {
                            // Auto fit columns for clean look
                            worksheet.Columns().AdjustToContents();
                            
                            // Save file
                            var dir = Path.GetDirectoryName(filePath);
                            if (dir != null && !Directory.Exists(dir))
                            {
                                Directory.CreateDirectory(dir);
                            }

                            workbook.SaveAs(filePath);
                        }

                        workbook.Dispose();
                        return; // Success! Exit loop and method
                    }
                    catch (IOException) when (attempt < maxRetries - 1)
                    {
                        // File is likely locked by another process/Excel, wait and retry
                        await Task.Delay(delayMs);
                    }
                }
            }
            finally
            {
                _excelSemaphore.Release();
            }
        }
    }
}
