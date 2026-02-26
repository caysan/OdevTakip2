using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using OdevTakip2.Models;

namespace OdevTakip2.Services
{
    public class AIGeneratorService
    {
        private readonly string _apiKey;
        private readonly string _modelId;
        private readonly string _endpointUrl;

        // Gemini API'si OpenAI uyumlu endpoint sağlıyorsa burayı kullanacağız.
        // Google AI Studio: "https://generativelanguage.googleapis.com/v1beta/openai/"
        public AIGeneratorService(string apiKey, string modelId = "gemini-2.5-flash", string endpointUrl = "https://generativelanguage.googleapis.com/v1beta/openai/")
        {
            _apiKey = apiKey;
            _modelId = modelId;
            _endpointUrl = endpointUrl;
        }

        public async Task<string> GenerateHtmlPlanAsync(List<HomeworkItem> collectedData)
        {
            if (collectedData == null || collectedData.Count == 0)
                return "<h1>Veri Bulunamadı</h1><p>Geçerli bir ödev / mesaj verisi gelmedi.</p>";

            // 1. Veriyi JSON String'ine Dönüştür
            var jsonPayload = JsonSerializer.Serialize(collectedData, new JsonSerializerOptions { WriteIndented = true });

            // 2. Prompt'u Hazırla
            var systemPrompt = @"Sen harika bir 'Öğrenci Çalışma Planı ve Asistanı' uygulamasının beynisin. 
Aşağıda sana WhatsApp mesajlarından ve okul sistemlerinden (E12 vb.) derlenmiş öğrencilerin ödev / duyuru listesi JSON formatında verilecektir. 
Görevin, bu karmaşık veriyi analiz etmek, tekrar edenleri tespit edip birleştirmek ve öğrenci için temiz, anlaşılır ve motive edici bir 'Haftalık / Günlük Çalışma Planı' sunmaktır.
Çıktı FORMAT KURALI: 
- Lütfen YALNIZCA HTML kodu döndür (Bana ```html vs gibi markdown da dönme. Sadece saf <html>..</html> ver). 
- Kodu yazarken modern, güzel görünümlü (CSS dahil) bir web sayfası tasarla. Göz alıcı renkler veya dark mode kullanılabilir.
- İçerisinde interaktif bir yapılacaklar listesi gibi okunabilir tablolar veya kartlar tasarla.";

            // 3. AI Client'ını Oluştur (Yeni Azure.AI.OpenAI v2 / OpenAI referansına göre)
            var clientOptions = new OpenAIClientOptions();
            clientOptions.Endpoint = new Uri(_endpointUrl);

            var openAiClient = new OpenAIClient(new System.ClientModel.ApiKeyCredential(_apiKey), clientOptions);
            var chatClient = openAiClient.GetChatClient(_modelId);

            var messages = new List<ChatMessage>
            {
                new SystemChatMessage(systemPrompt),
                new UserChatMessage($"İşte analiz etmen gereken Öğrenci Data JSON Dosyası:\n\n{jsonPayload}")
            };

            try 
            {
                Console.WriteLine("AI Modelinden yanıt bekleniyor...");
                ChatCompletion response = await chatClient.CompleteChatAsync(messages);
                var rawHtml = response.Content[0].Text?.Trim();

                // Markdown kod blokları arasına alınmış olma ihtimaline karşı temizlik
                if (!string.IsNullOrEmpty(rawHtml) && rawHtml.StartsWith("```"))
                {
                    rawHtml = rawHtml.Replace("```html", "").Replace("```html\n", "").Replace("```", "");
                }

                return rawHtml ?? "<html><body><h2>AI Yanıt Üretemedi</h2></body></html>";
            }
            catch(Exception ex)
            {
                return $"<html><body><h2>AI Bağlantı Hatası:</h2><p>{ex.Message}</p></body></html>";
            }
        }
    }
}
