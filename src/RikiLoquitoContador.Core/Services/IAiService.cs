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
        public string? EmisorNombre { get; set; }
        public string? EmisorCuit { get; set; }
        public string? ReceptorNombre { get; set; }
        public string? ReceptorCuit { get; set; }
        public string? ReceptorVatType { get; set; }
        public decimal? TotalAmount { get; set; }
        public string? Comments { get; set; }
        public string? InvoiceType { get; set; }
        public string? PointOfSale { get; set; }
        public string? InvoiceNumber { get; set; }
        public System.DateTime? IssueDate { get; set; }
        public System.Collections.Generic.List<FacturaDetalleDto> Items { get; set; } = new();

        // Robust Fallbacks for Local LLMs (snake_case, English, Spanish translations)
        [System.Text.Json.Serialization.JsonPropertyName("emisor_nombre")]
        public string? EmisorNombreSnake { set => EmisorNombre = value; }

        [System.Text.Json.Serialization.JsonPropertyName("nombre_emisor")]
        public string? NombreEmisor { set => EmisorNombre = value; }

        [System.Text.Json.Serialization.JsonPropertyName("emisor_cuit")]
        public string? EmisorCuitSnake { set => EmisorCuit = value; }

        [System.Text.Json.Serialization.JsonPropertyName("cuit_emisor")]
        public string? CuitEmisor { set => EmisorCuit = value; }

        [System.Text.Json.Serialization.JsonPropertyName("cuitemisor")]
        public string? CuitEmisorNoUnderscore { set => EmisorCuit = value; }

        [System.Text.Json.Serialization.JsonPropertyName("receptor_nombre")]
        public string? ReceptorNombreSnake { set => ReceptorNombre = value; }

        [System.Text.Json.Serialization.JsonPropertyName("nombre_receptor")]
        public string? NombreReceptor { set => ReceptorNombre = value; }

        [System.Text.Json.Serialization.JsonPropertyName("receptor_cuit")]
        public string? ReceptorCuitSnake { set => ReceptorCuit = value; }

        [System.Text.Json.Serialization.JsonPropertyName("cuit_receptor")]
        public string? CuitReceptor { set => ReceptorCuit = value; }

        [System.Text.Json.Serialization.JsonPropertyName("cuitreceptor")]
        public string? CuitReceptorNoUnderscore { set => ReceptorCuit = value; }

        [System.Text.Json.Serialization.JsonPropertyName("receptor_vat_type")]
        public string? ReceptorVatTypeSnake { set => ReceptorVatType = value; }

        [System.Text.Json.Serialization.JsonPropertyName("receptor_vat")]
        public string? ReceptorVatSnake { set => ReceptorVatType = value; }

        [System.Text.Json.Serialization.JsonPropertyName("condicion_iva")]
        public string? CondicionIva { set => ReceptorVatType = value; }

        [System.Text.Json.Serialization.JsonPropertyName("total_amount")]
        public decimal? TotalAmountSnake { set => TotalAmount = value; }

        [System.Text.Json.Serialization.JsonPropertyName("monto_total")]
        public decimal? MontoTotal { set => TotalAmount = value; }

        [System.Text.Json.Serialization.JsonPropertyName("invoice_type")]
        public string? InvoiceTypeSnake { set => InvoiceType = value; }

        [System.Text.Json.Serialization.JsonPropertyName("tipo_factura")]
        public string? TipoFactura { set => InvoiceType = value; }

        [System.Text.Json.Serialization.JsonPropertyName("point_of_sale")]
        public string? PointOfSaleSnake { set => PointOfSale = value; }

        [System.Text.Json.Serialization.JsonPropertyName("punto_venta")]
        public string? PuntoVenta { set => PointOfSale = value; }

        [System.Text.Json.Serialization.JsonPropertyName("invoice_number")]
        public string? InvoiceNumberSnake { set => InvoiceNumber = value; }

        [System.Text.Json.Serialization.JsonPropertyName("numero_comprobante")]
        public string? NumeroComprobante { set => InvoiceNumber = value; }

        [System.Text.Json.Serialization.JsonPropertyName("issue_date")]
        public System.DateTime? IssueDateSnake { set => IssueDate = value; }

        [System.Text.Json.Serialization.JsonPropertyName("fecha_emision")]
        public System.DateTime? FechaEmision { set => IssueDate = value; }
    }

    public class FacturaDetalleDto
    {
        public string Description { get; set; } = string.Empty;
        public decimal Quantity { get; set; }
        public decimal UnitPrice { get; set; }
        public decimal Subtotal { get; set; }
        public decimal? VatRate { get; set; }

        [System.Text.Json.Serialization.JsonPropertyName("unit_price")]
        public decimal UnitPriceSnake { set => UnitPrice = value; }

        [System.Text.Json.Serialization.JsonPropertyName("precio_unitario")]
        public decimal PrecioUnitario { set => UnitPrice = value; }

        [System.Text.Json.Serialization.JsonPropertyName("vat_rate")]
        public decimal? VatRateSnake { set => VatRate = value; }

        [System.Text.Json.Serialization.JsonPropertyName("tasa_iva")]
        public decimal? TasaIva { set => VatRate = value; }
    }
}
