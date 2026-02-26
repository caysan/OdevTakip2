using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Playwright;
using OdevTakip2.Models;

namespace OdevTakip2.Services
{
    public class WhatsAppScraperService
    {
        private readonly string _userDataDir;
        private readonly string _downloadsDir;

        public WhatsAppScraperService()
        {
            // Session verisi kalıcı tutulsun ki tekrar tekrar WhatsApp QR sormasın
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _userDataDir = Path.Combine(localAppData, "OdevTakip2", "WhatsAppSession");
            _downloadsDir = Path.Combine(Environment.CurrentDirectory, "Downloads");

            if (!Directory.Exists(_downloadsDir))
                Directory.CreateDirectory(_downloadsDir);
        }

        public async Task<List<HomeworkItem>> ScrapeMessagesAsync(string groupName, int scrollCount = 3)
        {
            var results = new List<HomeworkItem>();

            // Hedef klasör oluştur
            string dateStr = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
            string safeGroupName = string.Join("_", groupName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
            string folderName = $"{safeGroupName}-{dateStr}";
            string folderPath = Path.Combine(_downloadsDir, folderName);
            if (!Directory.Exists(folderPath))
            {
                Directory.CreateDirectory(folderPath);
            }

            using var playwright = await Playwright.CreateAsync();
            
            // Headless: false => Tarayıcıyı görürsünüz (WhatsApp QR okutmak veya izlemek için)
            await using var browserContext = await playwright.Chromium.LaunchPersistentContextAsync(_userDataDir, new BrowserTypeLaunchPersistentContextOptions
            {
                Headless = false, 
                ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
                AcceptDownloads = true
            });

            var page = browserContext.Pages.Count > 0 ? browserContext.Pages[0] : await browserContext.NewPageAsync();
            
            // İndirmeleri yakalamak için download event handler
            page.Download += async (sender, download) =>
            {
                try
                {
                    string filePath = Path.Combine(folderPath, download.SuggestedFilename);
                    await download.SaveAsAsync(filePath);
                }
                catch (Exception)
                {
                    // Ignore download errors
                }
            };

            await page.GotoAsync("https://web.whatsapp.com/", new PageGotoOptions { Timeout = 120000 });

            // QR kodunun okutulmasını veya sayfanın tamamen hazır olmasını bekliyoruz.
            // WhatsApp panele "Sohbetlerde aratın" benzeri bir input (search box) gelene kadar bekleyelim.
            var searchBoxSelector = "div[contenteditable='true'][data-tab='3']";
            await page.WaitForSelectorAsync(searchBoxSelector, new PageWaitForSelectorOptions { Timeout = 180000 }); // Kullanıcı QR okutana kadar ekstra vaktimiz olsun

            // 1. Grubu/Kişiyi bul ve tıkla
            await page.FillAsync(searchBoxSelector, groupName);
            await page.Keyboard.PressAsync("Enter");
            
            // Sohbetin yüklendiğinden emin olmak için biraz bekle
            await Task.Delay(3000); 

            // 2. Yukarı Scroll yap işlemi (Geçmiş mesajlar DOM'a eklensin diye)
            var messagesPanelSelector = "div[data-tab='8']"; // Mesajları barındıran scroll yediğimiz main panel
            var panelHandle = await page.QuerySelectorAsync(messagesPanelSelector);

            if (panelHandle != null)
            {
                for (int i = 0; i < scrollCount; i++)
                {
                    // Scroll'u en yukarı çekmek için javascript execute ediliyor.
                    await page.EvaluateAsync("panel => panel.scrollTop = 0", panelHandle);
                    await Task.Delay(2000); // DOM'un yenilerini yüklemesini bekle
                }
            }
            
            // 3. Mesajları parse et
            // Bu selector'lar (div.message-in, div.message-out vs) WhatsApp DOM güncellendikçe değişebilir.
            // WhatsApp'ta her mesaj kutusu genelde "div.message-in" (gelen) veya "div.message-out" (giden) içerisindedir
            var messageElements = await page.QuerySelectorAllAsync("div.message-in, div.message-out");
            
            foreach (var element in messageElements)
            {
                // Medya İndirme: Bir resim veya doküman varsa "Download" ikonuna sahip butonlara tıkla
                var downloadButtons = await element.QuerySelectorAllAsync("button:has(span[data-icon='down'])");
                foreach (var btn in downloadButtons)
                {
                    try
                    {
                        await btn.ClickAsync(new ElementHandleClickOptions { Timeout = 2000 });
                        await Task.Delay(1500); // İndirme başlasın diye biraz bekle
                    }
                    catch { }
                }

                var copyableTextSpan = await element.QuerySelectorAsync("span.selectable-text.copyable-text");
                if (copyableTextSpan != null)
                {
                    var innerText = await copyableTextSpan.InnerTextAsync();
                    if (!string.IsNullOrWhiteSpace(innerText))
                    {
                        var hwItem = new HomeworkItem
                        {
                            Source = "WhatsApp",
                            Content = innerText,
                            Timestamp = DateTime.Now // Daha detaylı metadata WhatsApp span'larından DOM ile parse edilebilir.
                        };

                        results.Add(hwItem);
                    }
                }
            }

            await browserContext.CloseAsync();

            // JSON olarak klasöre kaydet
            string jsonFileName = $"{safeGroupName}-{dateStr}.json";
            string jsonFilePath = Path.Combine(folderPath, jsonFileName);
            string jsonString = JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true });
            await File.WriteAllTextAsync(jsonFilePath, jsonString);

            return results;
        }
    }
}
