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
            bool treatPdfAsImage = ext == ".pdf" && settings.ScanningSettings.TreatPdfAsImage;
            bool isImage = ext == ".png" || ext == ".jpg" || ext == ".jpeg" || treatPdfAsImage;
            
            string prompt;
            try
            {
                prompt = GetEmbeddedPrompt();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load embedded prompt.");
                return new AiExtractionResult { Comments = $"Error al cargar el prompt de la IA: {ex.Message}" };
            }

            try
            {
                object requestPayload;

                if (isImage)
                {
                    var base64Images = new System.Collections.Generic.List<string>();
                    string mimeType = "image/jpeg";

                    if (treatPdfAsImage)
                    {
                        try
                        {
                            using var pdfStream = File.OpenRead(filePath);
                            foreach (var bitmap in PDFtoImage.Conversion.ToImages(pdfStream))
                            {
                                using (bitmap)
                                using (var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Jpeg, 90))
                                {
                                    if (data != null)
                                    {
                                        base64Images.Add(Convert.ToBase64String(data.ToArray()));
                                    }
                                }
                            }
                        }
                        catch (Exception renderEx)
                        {
                            _logger.LogError(renderEx, "Error rendering PDF to image: {FilePath}", filePath);
                            return new AiExtractionResult { Comments = $"Error al renderizar PDF a imagen: {renderEx.Message}" };
                        }

                        if (base64Images.Count == 0)
                        {
                            _logger.LogWarning("No pages rendered from PDF: {FilePath}", filePath);
                            return new AiExtractionResult { Comments = "No se pudieron renderizar páginas del PDF." };
                        }
                    }
                    else
                    {
                        byte[] imageBytes = await File.ReadAllBytesAsync(filePath);
                        base64Images.Add(Convert.ToBase64String(imageBytes));
                        mimeType = ext == ".png" ? "image/png" : "image/jpeg";
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
                                    content = prompt,
                                    images = base64Images.ToArray()
                                }
                            },
                            stream = false,
                            options = new { temperature = 0.1 }
                        };
                    }
                    else
                    {
                        var contentList = new System.Collections.Generic.List<object>
                        {
                            new
                            {
                                type = "text",
                                text = prompt
                            }
                        };

                        foreach (var base64Str in base64Images)
                        {
                            contentList.Add(new
                            {
                                type = "image_url",
                                image_url = new
                                {
                                    url = $"data:{mimeType};base64,{base64Str}"
                                }
                            });
                        }

                        requestPayload = new
                        {
                            model = aiSettings.ModelName,
                            messages = new[]
                            {
                                new
                                {
                                    role = "user",
                                    content = contentList.ToArray()
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

        private string GetEmbeddedPrompt()
        {
            var assembly = typeof(AiService).Assembly;
            string resourceName = "RikiLoquitoContador.Core.Prompts.invoice_analysis_prompt.md";
            
            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                var resources = assembly.GetManifestResourceNames();
                throw new InvalidOperationException($"Could not find embedded resource '{resourceName}'. Available resources: {string.Join(", ", resources)}");
            }
            
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
    }
}
