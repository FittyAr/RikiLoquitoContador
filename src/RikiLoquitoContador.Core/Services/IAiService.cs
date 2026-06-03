using System.Collections.Generic;
using System.Threading.Tasks;

namespace RikiLoquitoContador.Core.Services
{
    public interface IAiService
    {
        /// <summary>
        /// Analiza de forma asíncrona un archivo de factura (imagen, PDF o DOCX) usando IA
        /// y extrae la información extendida de la factura.
        /// </summary>
        /// <param name="filePath">Ruta absoluta del archivo a analizar.</param>
        /// <returns>Un objeto AiExtractionResult con los datos extraídos.</returns>
        Task<AiExtractionResult> AnalyzeInvoiceAsync(string filePath);

        /// <summary>
        /// Consulta los modelos disponibles en el proveedor local (por ejemplo, Ollama).
        /// </summary>
        /// <param name="provider">El nombre del proveedor seleccionado.</param>
        /// <param name="endpoint">El endpoint configurado.</param>
        /// <returns>Una lista con los nombres de los modelos disponibles.</returns>
        Task<List<string>> GetAvailableModelsAsync(string provider, string endpoint);
    }

    public class AiExtractionResult
    {
        public string? ClientName { get; set; }
        public decimal? TotalAmount { get; set; }
        public string? Comments { get; set; }
        public string? InvoiceType { get; set; }
        public string? PointOfSale { get; set; }
        public string? InvoiceNumber { get; set; }
        public System.DateTime? IssueDate { get; set; }
        public string? ClientCuit { get; set; }
        public string? ClientVatType { get; set; }
        public System.Collections.Generic.List<FacturaDetalleDto> Items { get; set; } = new();
    }

    public class FacturaDetalleDto
    {
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
        public decimal? VatRate { get; set; }
    }
}
