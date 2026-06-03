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
            csv.WriteField("Emisor");
            csv.WriteField("CUIT Emisor");
            csv.WriteField("Receptor");
            csv.WriteField("CUIT Receptor");
            csv.WriteField("Condicion IVA Receptor");
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
                csv.WriteField(f.EmisorNombre ?? "");
                csv.WriteField(f.EmisorCuit ?? "");
                csv.WriteField(f.ReceptorNombre ?? "");
                csv.WriteField(f.ReceptorCuit ?? "");
                csv.WriteField(f.ReceptorVatType ?? "");
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
                        HashSet<string> existingHashes = new();

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

                        // Always ensure Column 1 is hidden
                        worksheet.Column(1).Hide();

                        // If worksheet is empty (new workbook), write headers
                        var lastRow = worksheet.LastRowUsed()?.RowNumber() ?? 0;
                        if (lastRow == 0)
                        {
                            string[] headers = {
                                "Hash", "Fecha Creacion", "Fecha Indexado", 
                                "Emisor", "CUIT Emisor", "Receptor", "CUIT Receptor", 
                                "Condicion IVA Receptor", "Tipo Factura", 
                                "Punto Venta", "Nro Comprobante", "Fecha Emision", "Monto Total", "Comentarios"
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
                            // Read existing hashes to prevent duplicates
                            for (int r = 2; r <= lastRow; r++)
                            {
                                var hashCell = worksheet.Cell(r, 1).Value;
                                if (!hashCell.IsBlank)
                                {
                                    existingHashes.Add(hashCell.ToString());
                                }
                            }
                        }

                        // Append only new items
                        int writeRow = lastRow + 1;
                        bool addedAny = false;

                        foreach (var f in facturas)
                        {
                            if (existingHashes.Contains(f.FileHash))
                            {
                                continue; // Skip already exported invoice
                            }

                            worksheet.Cell(writeRow, 1).Value = f.FileHash;
                            worksheet.Cell(writeRow, 2).Value = f.FileCreatedAt;
                            worksheet.Cell(writeRow, 3).Value = f.IndexedAt.ToLocalTime();
                            worksheet.Cell(writeRow, 4).Value = f.EmisorNombre ?? "General";
                            worksheet.Cell(writeRow, 5).Value = f.EmisorCuit ?? "";
                            worksheet.Cell(writeRow, 6).Value = f.ReceptorNombre ?? "";
                            worksheet.Cell(writeRow, 7).Value = f.ReceptorCuit ?? "";
                            worksheet.Cell(writeRow, 8).Value = f.ReceptorVatType ?? "";
                            worksheet.Cell(writeRow, 9).Value = f.InvoiceType ?? "";
                            worksheet.Cell(writeRow, 10).Value = f.PointOfSale ?? "";
                            worksheet.Cell(writeRow, 11).Value = f.InvoiceNumber ?? "";
                            
                            if (f.IssueDate.HasValue)
                            {
                                worksheet.Cell(writeRow, 12).Value = f.IssueDate.Value;
                            }
                            else
                            {
                                worksheet.Cell(writeRow, 12).Value = "";
                            }
                            
                            if (f.TotalAmount.HasValue)
                            {
                                worksheet.Cell(writeRow, 13).Value = (double)f.TotalAmount.Value;
                                worksheet.Cell(writeRow, 13).Style.NumberFormat.Format = "$#,##0.00";
                            }
                            else
                            {
                                worksheet.Cell(writeRow, 13).Value = "";
                            }
                            
                            worksheet.Cell(writeRow, 14).Value = f.Comments ?? "";

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
