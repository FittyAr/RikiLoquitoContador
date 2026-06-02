using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text.Json;

namespace RikiLoquitoContador.RazorLib.Services
{
    public interface II18nService
    {
        string T(string key);
    }

    public class I18nService : II18nService
    {
        private readonly Dictionary<string, string> _translations = new();

        public I18nService()
        {
            LoadTranslations();
        }

        private void LoadTranslations()
        {
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("RikiLoquitoContador.RazorLib.es.json");
                if (stream == null)
                {
                    throw new FileNotFoundException("Embedded localization resource 'es.json' not found.");
                }

                using var reader = new StreamReader(stream);
                var json = reader.ReadToEnd();
                using var doc = JsonDocument.Parse(json);
                FlattenJson(doc.RootElement, string.Empty);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading localization: {ex.Message}");
            }
        }

        private void FlattenJson(JsonElement element, string prefix)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.Object:
                    foreach (var property in element.EnumerateObject())
                    {
                        var propName = string.IsNullOrEmpty(prefix) ? property.Name : $"{prefix}.{property.Name}";
                        FlattenJson(property.Value, propName);
                    }
                    break;
                case JsonValueKind.Array:
                    // Array indexing can be added if needed, but not required for this schema
                    break;
                default:
                    _translations[prefix] = element.ToString();
                    break;
            }
        }

        public string T(string key)
        {
            if (_translations.TryGetValue(key, out var val))
            {
                return val;
            }
            return $"[{key}]"; // Fallback for missing keys
        }
    }
}
