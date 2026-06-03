using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using RikiLoquitoContador.Core.Models;

namespace RikiLoquitoContador.Core.Services
{
    public class AiService : IAiService
    {
        private readonly IConfigService _configService;
        private readonly ILogger<AiService> _logger;
        private static readonly HttpClient _client = new HttpClient();

        public AiService(
            IConfigService configService,
            ILogger<AiService> logger)
        {
            _configService = configService;
            _logger = logger;
        }

        public async Task<AiExtractionResult> AnalyzeInvoiceAsync(string filePath)
        {
            if (!File.Exists(filePath))
            {
                _logger.LogWarning("File not found for AI analysis: {FilePath}", filePath);
                return new AiExtractionResult { Comments = "Archivo no encontrado." };
            }

            var settings = _configService.GetSettings();
            var aiSettings = settings.AiSettings;

            bool isLocalProvider = aiSettings.Provider != null && 
                                   (aiSettings.Provider.Contains("Ollama") || 
                                    aiSettings.Provider.Contains("LM Studio") || 
                                    aiSettings.Provider.Contains("Compatible"));

            if (!isLocalProvider && string.IsNullOrWhiteSpace(aiSettings.ApiKey))
            {
                _logger.LogWarning("AI API Key is not configured in settings.");
                return new AiExtractionResult { Comments = "Error: La API Key de la IA no está configurada." };
            }

            string ext = Path.GetExtension(filePath).ToLowerInvariant();
            bool isOllamaNative = aiSettings.Provider == "Ollama" || (aiSettings.Endpoint != null && aiSettings.Endpoint.Contains("/api/chat"));
            
            string prompt = "Eres un asistente experto en facturas de Argentina (AFIP). Tu objetivo es analizar la factura e identificar los datos del RECEPTOR de la factura (a quien le facturan, el cliente comprador, NO el emisor vendedor).\n\n" +
                            "DISTINCIÓN IMPORTANTE ENTRE EMISOR Y RECEPTOR:\n" +
                            "- En las facturas de AFIP, la parte superior (cabecera) contiene los datos del EMISOR (el que vende/emite). Su nombre/Razón Social aparece en grande arriba a la izquierda. Su CUIT, Ingresos Brutos, etc. aparecen arriba a la derecha. NO extraigas estos datos como ClientName ni como CUIT del cliente.\n" +
                            "- Los datos del RECEPTOR (el cliente/comprador) se encuentran en un recuadro cerrado en la sección media de la factura. El CUIT del receptor aparece a la izquierda etiquetado como 'CUIT:', su Condición de IVA a la izquierda (ej: 'IVA Responsable Inscripto', 'Responsable Monotributo', 'Exento', 'Consumidor Final'), y su nombre/Razón Social a la derecha etiquetado como 'Apellido y Nombre / Razón Social:'.\n" +
                            "- Asegúrate de asignar a 'ClientName' el nombre del cliente (que figura al lado de 'Apellido y Nombre / Razón Social:' en el recuadro del receptor) y a 'ClientCuit' el CUIT del cliente (que figura al lado de 'CUIT:' en el recuadro del receptor).\n" +
                            "- Asegúrate de asignar a 'ClientVatType' la condición frente al IVA del cliente (que figura al lado de 'Condición frente al IVA:' en el recuadro del receptor).\n\n" +
                            "Extrae la siguiente información:\n" +
                            "1. El nombre o razón social del cliente/comprador receptor (ClientName)\n" +
                            "2. El CUIT del cliente/comprador receptor (ClientCuit)\n" +
                            "3. La condición de IVA del cliente/comprador receptor (ClientVatType, ej. Responsable Inscripto, Monotributista, Consumidor Final, Exento)\n" +
                            "También identifica los siguientes datos del comprobante:\n" +
                            "4. El monto total de la factura como número decimal sin símbolos de moneda (TotalAmount), usando punto como separador decimal\n" +
                            "5. El tipo de factura (InvoiceType, ej. Factura A, Factura B, Factura C, Factura M, Nota de Crédito A, etc. No incluyas el código numérico de AFIP en el tipo, ej: extrae 'Factura C' en vez de 'COD. 011')\n" +
                            "6. El número de punto de venta como string (PointOfSale, ej. 00001)\n" +
                            "7. El número de comprobante como string (InvoiceNumber, ej. 00000002)\n" +
                            "8. La fecha de emisión (IssueDate, en formato YYYY-MM-DD, ej. 2020-10-31)\n" +
                            "9. Un resumen muy breve de los conceptos o comentarios generales (Comments)\n" +
                            "10. La lista de ítems detallados de productos o servicios (Items), donde cada ítem tiene:\n" +
                            "    - Description: Descripción del concepto/producto/servicio\n" +
                            "    - Quantity: Cantidad (número decimal, ej. 1.0)\n" +
                            "    - UnitPrice: Precio unitario (número decimal, ej. 15384.35)\n" +
                            "    - Subtotal: Subtotal (cantidad * precio unitario, número decimal)\n" +
                            "    - VatRate: Tasa de IVA aplicable (número decimal, ej. 21.0, 10.5, 0.0). En facturas C o Monotributo la tasa de IVA suele ser 0.0.\n\n" +
                            "DEBES retornar ÚNICAMENTE un objeto JSON válido con el siguiente esquema exacto:\n" +
                            "{\n" +
                            "  \"ClientName\": \"string\",\n" +
                            "  \"TotalAmount\": 123.45,\n" +
                            "  \"Comments\": \"string\",\n" +
                            "  \"InvoiceType\": \"string\",\n" +
                            "  \"PointOfSale\": \"string\",\n" +
                            "  \"InvoiceNumber\": \"string\",\n" +
                            "  \"IssueDate\": \"YYYY-MM-DD\",\n" +
                            "  \"ClientCuit\": \"string\",\n" +
                            "  \"ClientVatType\": \"string\",\n" +
                            "  \"Items\": [\n" +
                            "    {\n" +
                            "      \"Description\": \"string\",\n" +
                            "      \"Quantity\": 1.0,\n" +
                            "      \"UnitPrice\": 100.0,\n" +
                            "      \"Subtotal\": 100.0,\n" +
                            "      \"VatRate\": 21.0\n" +
                            "    }\n" +
                            "  ]\n" +
                            "}\n" +
                            "No formatees con markdown (no uses ```json), retorna solo el JSON puro.";

            try
            {
                object requestPayload;

                if (ext == ".png" || ext == ".jpg" || ext == ".jpeg")
                {
                    // For image files, read bytes and send base64 data to enable visual analysis
                    byte[] imageBytes = await File.ReadAllBytesAsync(filePath);
                    string base64String = Convert.ToBase64String(imageBytes);
                    string mimeType = ext == ".png" ? "image/png" : "image/jpeg";

                    if (isOllamaNative)
                    {
                        requestPayload = new
                        {
                            model = aiSettings.ModelName,
                            messages = new[]
                            {
                                new
                                {
                                    role = "user",
                                    content = prompt,
                                    images = new[] { base64String }
                                }
                            },
                            stream = false,
                            options = new { temperature = 0.1 }
                        };
                    }
                    else
                    {
                        requestPayload = new
                        {
                            model = aiSettings.ModelName,
                            messages = new[]
                            {
                                new
                                {
                                    role = "user",
                                    content = new object[]
                                    {
                                        new
                                        {
                                            type = "text",
                                            text = prompt
                                        },
                                        new
                                        {
                                            type = "image_url",
                                            image_url = new
                                            {
                                                url = $"data:{mimeType};base64,{base64String}"
                                            }
                                        }
                                    }
                                }
                            },
                            temperature = 0.1
                        };
                    }
                }
                else
                {
                    // For document files, extract text first
                    string extractedText = string.Empty;
                    if (ext == ".pdf")
                    {
                        extractedText = ExtractTextFromPdf(filePath);
                    }
                    else if (ext == ".docx")
                    {
                        extractedText = ExtractTextFromDocx(filePath);
                    }
                    else if (ext == ".doc")
                    {
                        extractedText = ExtractTextFromDoc(filePath);
                    }

                    if (string.IsNullOrWhiteSpace(extractedText))
                    {
                        _logger.LogWarning("No text could be extracted from document: {FilePath}", filePath);
                        return new AiExtractionResult { Comments = "No se pudo extraer texto del documento." };
                    }

                    if (isOllamaNative)
                    {
                        requestPayload = new
                        {
                            model = aiSettings.ModelName,
                            messages = new[]
                            {
                                new
                                {
                                    role = "user",
                                    content = prompt + "\n\n[TEXTO DE LA FACTURA]:\n" + extractedText
                                }
                            },
                            stream = false,
                            options = new { temperature = 0.1 }
                        };
                    }
                    else
                    {
                        requestPayload = new
                        {
                            model = aiSettings.ModelName,
                            messages = new[]
                            {
                                new
                                {
                                    role = "user",
                                    content = new object[]
                                    {
                                        new
                                        {
                                            type = "text",
                                            text = prompt + "\n\n[TEXTO DE LA FACTURA]:\n" + extractedText
                                        }
                                    }
                                }
                            },
                            temperature = 0.1
                        };
                    }
                }

                var jsonPayload = JsonSerializer.Serialize(requestPayload);
                var httpContent = new StringContent(jsonPayload, Encoding.UTF8, "application/json");

                string? targetEndpoint = aiSettings.Endpoint;
                if (!isOllamaNative && !string.IsNullOrWhiteSpace(targetEndpoint))
                {
                    if (targetEndpoint.EndsWith("/v1"))
                    {
                        targetEndpoint += "/chat/completions";
                    }
                    else if (targetEndpoint.EndsWith("/v1/"))
                    {
                        targetEndpoint += "chat/completions";
                    }
                }

                _logger.LogInformation("Sending invoice data to AI endpoint: {Endpoint} using model {Model}", targetEndpoint, aiSettings.ModelName);
                
                using var request = new HttpRequestMessage(HttpMethod.Post, targetEndpoint);
                if (!string.IsNullOrWhiteSpace(aiSettings.ApiKey))
                {
                    request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", aiSettings.ApiKey);
                }
                request.Content = httpContent;

                var response = await _client.SendAsync(request);
                
                if (!response.IsSuccessStatusCode)
                {
                    string errorContent = await response.Content.ReadAsStringAsync();
                    _logger.LogError("AI API returned error status {StatusCode}: {ErrorContent}", response.StatusCode, errorContent);
                    return new AiExtractionResult { Comments = $"Error de API ({response.StatusCode}): {errorContent}" };
                }

                string responseString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseString);
                var root = doc.RootElement;
                
                string? rawContent = null;
                
                if (root.TryGetProperty("message", out var ollamaMessage) && 
                    ollamaMessage.TryGetProperty("content", out var ollamaContentProp))
                {
                    rawContent = ollamaContentProp.GetString();
                }
                else if (root.TryGetProperty("choices", out var choices) && 
                         choices.GetArrayLength() > 0 && 
                         choices[0].TryGetProperty("message", out var openAiMessage) && 
                         openAiMessage.TryGetProperty("content", out var openAiContentProp))
                {
                    rawContent = openAiContentProp.GetString();
                }

                if (string.IsNullOrWhiteSpace(rawContent))
                {
                    return new AiExtractionResult { Comments = "Respuesta vacía o formato desconocido recibida de la IA." };
                }

                return ParseResponse(rawContent);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Exception during AI invoice analysis for {FilePath}", filePath);
                return new AiExtractionResult { Comments = $"Fallo de conexión o análisis de la IA: {ex.Message}" };
            }
        }

        private AiExtractionResult ParseResponse(string rawResponse)
        {
            int start = rawResponse.IndexOf('{');
            int end = rawResponse.LastIndexOf('}');
            if (start >= 0 && end > start)
            {
                string json = rawResponse.Substring(start, end - start + 1);
                try
                {
                    var result = JsonSerializer.Deserialize<AiExtractionResult>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                    if (result != null)
                    {
                        return result;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to parse JSON extracted from AI response. JSON string: {Json}", json);
                    return new AiExtractionResult { Comments = "Error al parsear el JSON de la IA. Detalle: " + ex.Message };
                }
            }
            
            return new AiExtractionResult { Comments = "Error al decodificar la respuesta JSON estructurada de la IA. Respuesta cruda: " + rawResponse };
        }

        private string ExtractTextFromPdf(string filePath)
        {
            try
            {
                using var document = UglyToad.PdfPig.PdfDocument.Open(filePath);
                var sb = new StringBuilder();
                foreach (var page in document.GetPages())
                {
                    sb.AppendLine(page.Text);
                }
                return sb.ToString();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from PDF '{FilePath}' using PdfPig", filePath);
                return string.Empty;
            }
        }

        private string ExtractTextFromDocx(string filePath)
        {
            try
            {
                using var fileStream = File.OpenRead(filePath);
                using var archive = new ZipArchive(fileStream);
                var entry = archive.GetEntry("word/document.xml");
                if (entry == null) return string.Empty;

                using var entryStream = entry.Open();
                var doc = XDocument.Load(entryStream);
                
                XNamespace w = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
                var texts = doc.Descendants(w + "t").Select(t => t.Value);
                return string.Join(" ", texts);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error extracting text from DOCX '{FilePath}'", filePath);
                return string.Empty;
            }
        }

        private string ExtractTextFromDoc(string filePath)
        {
            return "El formato DOC antiguo no está soportado. Conviértalo a DOCX o PDF para extraer texto.";
        }

        public async Task<System.Collections.Generic.List<string>> GetAvailableModelsAsync(string provider, string endpoint)
        {
            var modelsList = new System.Collections.Generic.List<string>();
            
            // Extract base url for Ollama
            string baseUrl = "http://localhost:11434";
            if (!string.IsNullOrWhiteSpace(endpoint))
            {
                try
                {
                    var uri = new Uri(endpoint);
                    baseUrl = $"{uri.Scheme}://{uri.Authority}";
                }
                catch
                {
                    // Fallback to default localhost
                }
            }

            string tagsEndpoint = $"{baseUrl}/api/tags";
            _logger.LogInformation("Scanning Ollama models from: {TagsEndpoint}", tagsEndpoint);

            try
            {
                var response = await _client.GetAsync(tagsEndpoint);
                if (response.IsSuccessStatusCode)
                {
                    string content = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty("models", out var modelsArray) && 
                        modelsArray.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in modelsArray.EnumerateArray())
                        {
                            if (item.TryGetProperty("name", out var nameProp))
                            {
                                string? modelName = nameProp.GetString();
                                if (!string.IsNullOrEmpty(modelName))
                                {
                                    modelsList.Add(modelName);
                                }
                            }
                        }
                    }
                }
                else
                {
                    _logger.LogWarning("Ollama tags endpoint returned status {StatusCode}", response.StatusCode);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to query Ollama models at {TagsEndpoint}", tagsEndpoint);
                throw new InvalidOperationException($"No se pudo conectar a Ollama en {baseUrl}. Asegúrese de que Ollama está ejecutándose. Detalle: {ex.Message}", ex);
            }

            return modelsList;
        }

        private class AiInvoiceResult
        {
            public string? ClientName { get; set; }
            public decimal? TotalAmount { get; set; }
            public string? Comments { get; set; }
        }
    }
}
