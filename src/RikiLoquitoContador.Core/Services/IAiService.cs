using System.Collections.Generic;
using System.Threading.Tasks;

namespace RikiLoquitoContador.Core.Services
{
    public interface IAiService
    {
        /// <summary>
        /// Analiza de forma asíncrona un archivo de factura (imagen, PDF o DOCX) usando IA
        /// y extrae el nombre del cliente, monto total y comentarios estructurados.
        /// </summary>
        /// <param name="filePath">Ruta absoluta del archivo a analizar.</param>
        /// <returns>Una tupla con el Nombre del Cliente, Monto Total y Comentarios extraídos.</returns>
        Task<(string? ClientName, decimal? TotalAmount, string? Comments)> AnalyzeInvoiceAsync(string filePath);

        /// <summary>
        /// Consulta los modelos disponibles en el proveedor local (por ejemplo, Ollama).
        /// </summary>
        /// <param name="provider">El nombre del proveedor seleccionado.</param>
        /// <param name="endpoint">El endpoint configurado.</param>
        /// <returns>Una lista con los nombres de los modelos disponibles.</returns>
        Task<List<string>> GetAvailableModelsAsync(string provider, string endpoint);
    }
}
