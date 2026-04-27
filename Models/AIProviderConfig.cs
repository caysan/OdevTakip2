using System.Text.Json.Serialization;

namespace OdevTakip2.Models
{
    public class AIProviderConfig
    {
        public string ProviderType { get; set; } = "Gemini"; // "Gemini" | "OpenAI"
        public string ApiKey { get; set; } = string.Empty;
        public string ModelId { get; set; } = "gemini-1.5-flash";
        public int Priority { get; set; } = 1;
        public bool IsEnabled { get; set; } = true;

        [JsonIgnore]
        public string EndpointUrl => ProviderType == "OpenAI"
            ? ""   // empty = SDK default (api.openai.com)
            : "https://generativelanguage.googleapis.com/v1beta/openai/";
    }
}
