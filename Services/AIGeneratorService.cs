using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using OpenAI;
using OpenAI.Chat;
using OdevTakip2.Models;
using Google.GenAI;

namespace OdevTakip2.Services
{
    public class AIGeneratorService
    {
        private readonly string _apiKey;
        private readonly string _modelId;
        private readonly string? _endpointUrl;
        private readonly bool _isGemini;

        private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
            { ".jpg", ".jpeg", ".png" };

        private const long MaxImageBytes = 2 * 1024 * 1024; // 2 MB
        private static readonly int[] RetryDelaysSeconds = { 15, 30, 60 };

        public AIGeneratorService(string apiKey, string modelId = "gemini-1.5-flash",
            string? endpointUrl = "https://generativelanguage.googleapis.com/v1beta/openai/")
        {
            _apiKey = apiKey;
            _modelId = modelId;
            _endpointUrl = endpointUrl;

            // Assume it's Gemini natively if no endpoint is specified or if it's the old googleapis endpoint
            _isGemini = (string.IsNullOrEmpty(_endpointUrl) && _modelId.StartsWith("gemini")) ||
                        (_endpointUrl != null && _endpointUrl.Contains("googleapis.com"));
        }

        private static async Task<T> WithRetryAsync<T>(Func<Task<T>> action, Action<string>? log = null)
        {
            for (int attempt = 0; attempt < RetryDelaysSeconds.Length; attempt++)
            {
                try
                {
                    return await action();
                }
                catch (Exception ex) when (IsRetryable(ex))
                {
                    int wait = RetryDelaysSeconds[attempt];
                    log?.Invoke($"API geçici hata (429/503) — {wait}s sonra yeniden denenecek... (deneme {attempt + 1}/{RetryDelaysSeconds.Length})");
                    await Task.Delay(TimeSpan.FromSeconds(wait));
                }
            }
            return await action();
        }

        private static bool IsRetryable(Exception ex)
            => ex is Google.GenAI.ClientError ||
               ex.Message.Contains("429") ||
               ex.Message.Contains("503") ||
               ex.Message.Contains("too many requests", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("service unavailable", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) ||
               ex.Message.Contains("UNAVAILABLE", StringComparison.OrdinalIgnoreCase);

        private Google.GenAI.Client BuildGeminiClient()
        {
            var options = new Google.GenAI.Types.HttpOptions { Timeout = (int)TimeSpan.FromMinutes(10).TotalMilliseconds };
            return new Google.GenAI.Client(apiKey: _apiKey, httpOptions: options);
        }

        private ChatClient BuildOpenAIClient()
        {
            var options = new OpenAIClientOptions
            {
                NetworkTimeout = TimeSpan.FromMinutes(10)
            };

            if (!string.IsNullOrEmpty(_endpointUrl))
            {
                options.Endpoint = new Uri(_endpointUrl.EndsWith("/") ? _endpointUrl : _endpointUrl + "/");
            }

            return new OpenAIClient(new System.ClientModel.ApiKeyCredential(_apiKey), options)
                       .GetChatClient(_modelId);
        }

        public async Task<string> TestConnectionAsync(string message, Action<string>? log = null)
        {
            log?.Invoke($"AI Test İstek Gönderiliyor: {message}");
            if (_isGemini)
            {
                var client = BuildGeminiClient();
                var response = await WithRetryAsync(
                    async () => await client.Models.GenerateContentAsync(_modelId, message), log);
                return response.Text?.Trim() ?? "[Boş Yanıt]";
            }
            else
            {
                var chatClient = BuildOpenAIClient();
                var messages = new List<ChatMessage> { new UserChatMessage(message) };
                ChatCompletion response = await WithRetryAsync(
                    async () => (ChatCompletion)await chatClient.CompleteChatAsync(messages), log);
                return response.Content[0].Text?.Trim() ?? "[Boş Yanıt]";
            }
        }

        public async Task<string> ConvertScheduleToJsonAsync(string imagePath, Action<string>? log = null)
        {
            byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);
            string ext = Path.GetExtension(imagePath);
            string mimeType = ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";

            string prompt = "Bu görselde bir okul veya dershane ders programı bulunmaktadır.\n" +
                            "Lütfen bu ders programını aşağıdaki JSON formatında çıkar:\n" +
                            "{\n  \"schedule\": {\n    \"Pazartesi\": [{\"period\": 1, \"time\": \"08:00-08:45\", \"lesson\": \"Matematik\"}],\n    ...\n  }\n}\n" +
                            "Yalnızca geçerli JSON döndür. Açıklama veya markdown ekleme.";

            string raw = "{}";

            if (_isGemini)
            {
                var client = BuildGeminiClient();
                var contents = new Google.GenAI.Types.Content
                {
                    Role = "user",
                    Parts = new List<Google.GenAI.Types.Part>
                    {
                        Google.GenAI.Types.Part.FromText(prompt),
                        Google.GenAI.Types.Part.FromBytes(imageBytes, mimeType)
                    }
                };

                var response = await WithRetryAsync(
                    async () => await client.Models.GenerateContentAsync(_modelId, contents), log);
                raw = response.Text?.Trim() ?? "{}";
            }
            else
            {
                var chatClient = BuildOpenAIClient();
                var contentParts = new List<ChatMessageContentPart>
                {
                    ChatMessageContentPart.CreateTextPart(prompt),
                    ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(imageBytes), mimeType)
                };

                var messages = new List<ChatMessage> { new UserChatMessage(contentParts) };
                ChatCompletion response = await WithRetryAsync(
                    async () => (ChatCompletion)await chatClient.CompleteChatAsync(messages), log);

                raw = response.Content[0].Text?.Trim() ?? "{}";
            }

            if (raw.StartsWith("```"))
                raw = raw.Replace("```json", "").Replace("```\n", "").Replace("```", "").Trim();
            return raw;
        }

        public async Task<string> GenerateHtmlPlanAsync(List<HomeworkItem> collectedData, string? scheduleJson = null, Action<string>? log = null)
        {
            if (collectedData == null || collectedData.Count == 0)
                return "<h1>Veri Bulunamadı</h1><p>Geçerli bir ödev / mesaj verisi gelmedi.</p>";

            string today = DateTime.Now.ToString("d MMMM yyyy, dddd", new CultureInfo("tr-TR"));

            //var systemPrompt =
            //    $"Sen bir 'Öğrenci Çalışma Planı Asistanı' uygulamasının beynisin.\n" +
            //    $"Öğrencinin ödev listesi JSON ve (varsa) ders programı JSON verilecektir. Ekteki görseller ödev fotoğraflarıdır — içlerindeki metinleri oku ve analize dahil et.\n\n" +
            //    $"BUGÜNÜN TARİHİ: {today}\n\n" +
            //    "GÖREV KURALLARI:\n" +
            //    "1. Gelen veriler TEK BİR kurum tipine aittir. Sayfanın en üstüne HTML'de `<h2>` başlığı ile bu kurumun adını (örn: `<h2>Okul Ödevleri</h2>` veya `<h2>Dershane Ödevleri</h2>`) büyük ve belirgin şekilde yaz, ardından sadece o kuruma ait olan tabloyu çiz.\n" +
            //    "2. Ödevleri teslim tarihine göre sırala — en yakın tarihli en üstte.\n" +
            //    "3. Teslim tarihi bugünden 1-2 gün içinde olan ödevleri kırmızı (#cc0000) + kalın (bold) yaz; ⚠ ikonu ekle.\n" +
            //    "4. Eğer bu kuruma özel bir ders programı verilmişse, her derse ait haftanın günü ve saatini ödev satırında ('Ders' veya 'Kaynak' sütununun altına) göster.\n" +
            //    "5. Tekrar eden ödevleri tek satırda birleştir; kaynak parantez içinde belirtilsin.\n\n" +
            //    "ÇIKTI FORMAT KURALLARI:\n" +
            //    "- YALNIZCA HTML tablo ve <h2> başlık yapısını döndür. <head>, <body> gibi root etiketleri döndürmene GEREK YOKTUR, sadece içeriği (body içini) üret.\n" +
            //    "- Kesinlikle ```html gibi markdown işaretleri ekleme.\n" +
            //    "- Tablo yapısı: Ders | Ödev/İçerik | Teslim Tarihi | Kaynak";
            var systemPrompt =
                $"Sen bir 'Öğrenci Çalışma Planı Asistanı' uygulamasının gelişmiş analiz motorusun.\n" +
                $"Sana tek bir kurum tipine (örn: Okul veya Dershane) ait ödev verileri (WhatsApp mesajları JSON, e12 vb. sistem JSON verileri) ve o kuruma ait Ders Programı JSON verisi verilecektir.\n" +
                $"Ekteki görseller sınıf içi ödev notlarıdır (örn: tahtaya yazılanlar) — içlerindeki metinleri OCR gibi oku, analiz et ve ödev listesine dahil et.\n\n" +

                $"BUGÜNÜN TARİHİ: {today}\n\n" +

                $"GÖREV VE ANALİZ KURALLARI:\n" +
                $"1. KURUM KAPSAMI VE BAŞLIK: Gelen tüm veriler TEK BİR kurum tipine aittir. Her analiz kendi kurumu içinde yapılmalıdır. Çıktının en üstüne HTML'de `<h2>` başlığı ile bu kurumun adını (örn: `<h2>Okul Ödevleri</h2>`) büyük ve belirgin şekilde yaz, ardından tabloyu oluştur.\n" +

                $"2. DİNAMİK TESLİM TARİHİ HESAPLAMA (ÇOK ÖNEMLİ):\n" +
                $"   - Eğer ödev verisinde (JSON veya fotoğraftaki metinde) net bir tarih veya 'yarına', 'haftaya' gibi zaman ifadesi varsa, teslim tarihi olarak bunu kullan. e12 gibi sistemlerden gelen Json verisi içerisindeki değerlerden birisi son tarih olabilir, expire, due, last, sontarih gibi isimlendirmede olabilir. \n" +
                $"   - Eğer ödevde HİÇBİR teslim tarihi belirtilmemişse: Verilen 'Ders Programı JSON' verisini kontrol et. Bugünün tarihinden ({today}) itibaren, o dersin programda yer aldığı İLK GÜNÜ bul ve bu tarihi ödevin teslim tarihi olarak ata.\n" +

                $"3. SIRALAMA VE BİRLEŞTİRME: Ödevleri teslim tarihine göre eskiden yeniye (en yakın tarihli en üstte olacak şekilde) sırala. Aynı derse ait farklı kaynaklardan (WhatsApp, e12, vb.) gelen ödevleri tek bir satırda birleştir ve 'Kaynak' sütununda bu kaynakları virgülle belirterek yaz.\n" +

                $"4. YAKLAŞAN / GEÇMİŞ ÖDEVLER: Gönderilen listede, ödevin teslim tarihi dün, bugün ve gelecek tarihli ödevleri listeye dahil et, diğerlerini işleme alma. Geçmiş tarihli (süresi dolmuş) ödevleri ve teslim tarihi bugünden 1 gün içinde olan acil ödevlerin satır metinlerini ve tarihlerini Kırmızı (#cc0000) yap, yanlarına ⚠ ikonu ekle. Geçmiş tarihli ödevlerin rengini (#F3BE7A) yap \n" +

                $"5. DERS PROGRAMI GÖSTERİMİ: Eğer o derse ait program verisi varsa, tablonun ilgili satırında küçük bir not olarak yeni bir satırda (Div içeriside #CBCBCB renginde, italik ve font-size:9px olacak şekilde) dersin yapılacağı günü ve saati göster.\n\n" +
                $"6. GÖNDEREN BİLGİSİ: Ders adının hemen altına, gönderilen JSON'da yer alan 'Sender' (Gönderen) bilgisini gri (#888888) renkte, italik ve daha küçük bir fontla (örn: <span style=\"color:#888; font-size:0.9em; font-style:italic;\">Gönderen: ...</span>) yazdır.\n\n" +

                $"ÇIKTI FORMAT KURALLARI (KESİN YÖNERGELER):\n" +
                $"- Tablo yapısı kesinlikle şu sütunlardan oluşmalıdır: Ders | Ödev/İçerik | Teslim Tarihi | Kaynak\n" +
                $"- YALNIZCA HTML tablosu ve `<h2>` başlık yapısını döndür. `<html>`, `<head>`, `<body>` gibi root etiketleri KESİNLİKLE DÖNDÜRME; sadece içeriği üret.\n" +
                $"- Kod bloğu belirteçleri (```html veya ```) KESİNLİKLE KULLANMA. Çıktı, doğrudan HTML formatında parse edilecek saf metin olmalıdır.\n\n" +

                $"Ek Açıklamalar:\n" +
                $"- Sana gönderilen ÖDEV LİSTESİ JSON veri yapısındaki `DueDate` alanı (eğer varsa) ödevin KESİN teslim tarihidir. Onu kullan.\n" +
                $"- Eğer `DueDate` boş ise, `Date` alanına ve Ders Programına bakarak mantıklı bir teslim tarihi hesapla.\n\n";

            var aiPayload = collectedData.Select(item => new
            {
                item.Source,
                item.InstitutionType,
                item.Sender,
                Date = item.Timestamp.ToString("yyyy-MM-dd"),
                DueDate = item.DueDate?.ToString("yyyy-MM-dd"),
                item.Content,
                MediaFiles = item.MediaFiles.Select(Path.GetFileName).ToList()
            });
            var jsonPayload = JsonSerializer.Serialize(aiPayload, new JsonSerializerOptions { WriteIndented = false });

            var userText = $"Bugün: {today}\n\n";
            if (!string.IsNullOrWhiteSpace(scheduleJson))
                userText += $"DERS PROGRAMI JSON:\n{scheduleJson}\n\n";
            userText += $"ÖDEV LİSTESİ JSON:\n{jsonPayload}";

            string rawHtml = "";

            if (_isGemini)
            {
                var client = BuildGeminiClient();
                var parts = new List<Google.GenAI.Types.Part> { Google.GenAI.Types.Part.FromText(userText) };

                int imageCount = 0;
                const int MaxImages = 15;
                foreach (var item in collectedData)
                {
                    foreach (var filePath in item.MediaFiles)
                    {
                        if (imageCount >= MaxImages) break;
                        string ext = Path.GetExtension(filePath);
                        if (!ImageExtensions.Contains(ext) || !File.Exists(filePath)) continue;

                        var info = new FileInfo(filePath);
                        if (info.Length > MaxImageBytes)
                        {
                            log?.Invoke($"Görsel atlandı (çok büyük: {info.Length / 1024}KB): {info.Name}");
                            continue;
                        }

                        try
                        {
                            byte[] bytes = await File.ReadAllBytesAsync(filePath);
                            string mime = ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                            parts.Add(Google.GenAI.Types.Part.FromBytes(bytes, mime));
                            imageCount++;
                        }
                        catch { }
                    }
                    if (imageCount >= MaxImages) break;
                }

                log?.Invoke($"AI isteği gönderiliyor (Veri: {collectedData.Count}, Görsel: {imageCount})...");

                var config = new Google.GenAI.Types.GenerateContentConfig { SystemInstruction = new Google.GenAI.Types.Content { Parts = new List<Google.GenAI.Types.Part> { Google.GenAI.Types.Part.FromText(systemPrompt) } } };
                var contents = new Google.GenAI.Types.Content { Role = "user", Parts = parts };

                var response = await WithRetryAsync(
                    async () => await client.Models.GenerateContentAsync(_modelId, contents, config), log);
                rawHtml = response.Text?.Trim() ?? "";
            }
            else
            {
                var chatClient = BuildOpenAIClient();
                var contentParts = new List<ChatMessageContentPart> { ChatMessageContentPart.CreateTextPart(userText) };

                int imageCount = 0;
                const int MaxImages = 15;
                foreach (var item in collectedData)
                {
                    foreach (var filePath in item.MediaFiles)
                    {
                        if (imageCount >= MaxImages) break;
                        string ext = Path.GetExtension(filePath);
                        if (!ImageExtensions.Contains(ext) || !File.Exists(filePath)) continue;

                        var info = new FileInfo(filePath);
                        if (info.Length > MaxImageBytes)
                        {
                            log?.Invoke($"Görsel atlandı (çok büyük: {info.Length / 1024}KB): {info.Name}");
                            continue;
                        }

                        try
                        {
                            byte[] bytes = await File.ReadAllBytesAsync(filePath);
                            string mime = ext.Equals(".png", StringComparison.OrdinalIgnoreCase) ? "image/png" : "image/jpeg";
                            contentParts.Add(ChatMessageContentPart.CreateImagePart(BinaryData.FromBytes(bytes), mime));
                            imageCount++;
                        }
                        catch { }
                    }
                    if (imageCount >= MaxImages) break;
                }

                log?.Invoke($"AI isteği gönderiliyor (Veri: {collectedData.Count}, Görsel: {imageCount})...");

                var messages = new List<ChatMessage>
                {
                    new SystemChatMessage(systemPrompt),
                    new UserChatMessage(contentParts)
                };

                ChatCompletion response = await WithRetryAsync(
                    async () => (ChatCompletion)await chatClient.CompleteChatAsync(messages), log);
                rawHtml = response.Content[0].Text?.Trim() ?? "";
            }

            if (!string.IsNullOrEmpty(rawHtml) && rawHtml.StartsWith("```"))
                rawHtml = rawHtml.Replace("```html", "").Replace("```html\n", "").Replace("```", "");

            return string.IsNullOrEmpty(rawHtml) ? "<html><body><h2>AI Yanıt Üretemedi</h2></body></html>" : rawHtml;
        }
    }
}
