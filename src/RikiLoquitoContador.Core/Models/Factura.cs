using System;

namespace RikiLoquitoContador.Core.Models
{
    public class Factura
    {
        public int Id { get; set; }
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string FileExtension { get; set; } = string.Empty;
        public long FileSizeBytes { get; set; }
        public DateTime FileCreatedAt { get; set; }
        public DateTime IndexedAt { get; set; }
        public string? ClientName { get; set; }
        public decimal? TotalAmount { get; set; }
        public string? Comments { get; set; }
        
        // Extended control and extraction fields
        public string FileHash { get; set; } = string.Empty;
        public string? InvoiceType { get; set; }
        public string? PointOfSale { get; set; }
        public string? InvoiceNumber { get; set; }
        public DateTime? IssueDate { get; set; }
        public string? ClientCuit { get; set; }
        public string? ClientVatType { get; set; }
        public string? ProcessedPath { get; set; }
        public string Status { get; set; } = "Success"; // "Success" or "Conflict"
        public string? ErrorMessage { get; set; }

        public System.Collections.Generic.List<FacturaDetalle> Detalles { get; set; } = new();
    }
}
