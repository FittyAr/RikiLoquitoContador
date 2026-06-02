namespace RikiLoquitoContador.Core.Models
{
    public class AppSettings
    {
        public ConnectionStringsSettings ConnectionStrings { get; set; } = new();
        public SecuritySettings SecuritySettings { get; set; } = new();
        public ScanningSettings ScanningSettings { get; set; } = new();
        public AiSettings AiSettings { get; set; } = new();
    }

    public class ConnectionStringsSettings
    {
        public string DefaultConnection { get; set; } = "Data Source=facturas.db";
    }

    public class SecuritySettings
    {
        public string PasswordHash { get; set; } = string.Empty;
    }

    public class ScanningSettings
    {
        public string WatchFolderPath { get; set; } = string.Empty;
        public int ScanIntervalSeconds { get; set; } = 10;
    }

    public class AiSettings
    {
        public string Provider { get; set; } = "OpenAI";
        public string Endpoint { get; set; } = "https://api.openai.com/v1/chat/completions";
        public string ModelName { get; set; } = "gpt-4o-mini";
        public string ApiKey { get; set; } = string.Empty;
    }
}
