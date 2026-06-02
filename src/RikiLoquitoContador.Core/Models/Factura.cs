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
    }
}
