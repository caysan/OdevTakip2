using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
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
            string basePath = AppContext.BaseDirectory;
#if DEBUG
            DirectoryInfo dir = new DirectoryInfo(basePath);
            while (dir != null && dir.GetFiles("*.csproj").Length == 0)
                dir = dir.Parent;
            if (dir != null) basePath = dir.FullName;
#endif
            string userFilesPath = Path.Combine(basePath, "UserFiles");
            if (!Directory.Exists(userFilesPath)) Directory.CreateDirectory(userFilesPath);

            _userDataDir = Path.Combine(userFilesPath, "WhatsAppSession");
            _downloadsDir = Path.Combine(userFilesPath, "Downloads");
            if (!Directory.Exists(_downloadsDir)) Directory.CreateDirectory(_downloadsDir);
        }

        // ── Helpers ────────────────────────────────────────────────────────────
        private static string SafePrefix(string institutionType)
        {
            if (string.IsNullOrWhiteSpace(institutionType)) return "";
            string safe = new string(institutionType.ToLowerInvariant()
                .Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray()).Trim('_');
            return safe + "-";
        }

        private static bool IsValidFile(string filename)
        {
            if (string.IsNullOrEmpty(filename)) return false;
            string ext = Path.GetExtension(filename).ToLowerInvariant();
            return ext is ".pdf" or ".doc" or ".docx" or ".xls" or ".xlsx" or ".jpg" or ".jpeg" or ".png";
        }

        // ── Main ───────────────────────────────────────────────────────────────
        public async Task<List<HomeworkItem>> ScrapeMessagesAsync(List<WhatsAppGroupConfig> groups, int scrollCount = 3)
        {
            var totalResults = new List<HomeworkItem>();
            string mainFolderPath = Path.Combine(_downloadsDir, "WhatsApp");
            if (Directory.Exists(mainFolderPath))
            {
                try { Directory.Delete(mainFolderPath, true); } catch { }
            }
            Directory.CreateDirectory(mainFolderPath);

            using var playwright = await Playwright.CreateAsync();
            await using var browserContext = await playwright.Chromium.LaunchPersistentContextAsync(
                _userDataDir,
                new BrowserTypeLaunchPersistentContextOptions
                {
                    Headless = false,
                    ViewportSize = new ViewportSize { Width = 1280, Height = 720 },
                    AcceptDownloads = true
                });

            var page = browserContext.Pages.Count > 0
                ? browserContext.Pages[0]
                : await browserContext.NewPageAsync();

            await page.GotoAsync("https://web.whatsapp.com/", new PageGotoOptions { Timeout = 120000 });

            // Yükleme ekranının (progress bar) kaybolmasını bekle
            try
            {
                var progressBar = page.Locator("progress");
                if (await progressBar.CountAsync() > 0)
                {
                    await progressBar.Last.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 180000 });
                }
            }
            catch { }

            // Kullanıcıyı uyaran ve müdahaleyi kısıtlayan görsel bir kalkan (overlay) ekle.
            // Playwright'ın tıklamalarını engellememesi için pointer-events: none kullanıyoruz.
            try
            {
                await page.AddStyleTagAsync(new PageAddStyleTagOptions
                {
                    Content = @"
                        body::after {
                            content: '⚠️ BOT İŞLEM YAPIYOR - LÜTFEN EKRANA DOKUNMAYIN ⚠️';
                            position: fixed; top: 0; left: 0; width: 100vw; height: 100vh;
                            background: rgba(0, 0, 0, 0.75);
                            color: #ff3b3b; font-size: 32px; font-weight: bold;
                            display: flex; justify-content: center; align-items: center;
                            z-index: 2147483647; pointer-events: none; text-shadow: 2px 2px 4px #000;
                            backdrop-filter: blur(2px);
                            animation: pulse 2s infinite;
                        }
                        @keyframes pulse { 0% { opacity: 0.8; } 50% { opacity: 1; } 100% { opacity: 0.8; } }
                    "
                });
            }
            catch { }

            // Ekstra başlangıç senkronizasyon payı (WhatsApp web arkaplanda senkronize olur)
            await Task.Delay(10000);

            foreach (var group in groups)
            {
                string groupName = group.Name;
                string instType = group.InstitutionType;
                string prefix = SafePrefix(instType);
                string safeGrpName = string.Join("_", groupName.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
                string groupFolder = Path.Combine(mainFolderPath, safeGrpName);
                string docsFolder = Path.Combine(groupFolder, "docs");
                Directory.CreateDirectory(groupFolder);
                Directory.CreateDirectory(docsFolder);

                // Per-group dedup state
                var seenSignatures = new HashSet<string>();
                var processedBlobs = new HashSet<string>();
                var groupResults = new List<HomeworkItem>();

                try
                {
                    var searchBox = "div[data-testid='chat-list-search'], #side input[type='text'], div[title='Arama kutusu']";
                    await page.WaitForSelectorAsync(searchBox, new PageWaitForSelectorOptions { Timeout = 180000 });

                    bool groupFound = false;
                    for (int retry = 0; retry < 5; retry++)
                    {
                        await page.FillAsync(searchBox, "");
                        await Task.Delay(1000);
                        await page.FillAsync(searchBox, groupName);
                        await Task.Delay(3000); // Arama sonuçlarının listelenmesini bekle
                        await page.Keyboard.PressAsync("Enter");

                        // Grup açıldıktan sonra mesajların tam olarak yüklenmesi için bekle
                        await Task.Delay(5000);

                        // Grubun gerçekten açılıp açılmadığını başlık üzerinden kontrol et
                        var headerTitle = await page.QuerySelectorAsync("header span[dir='auto']");
                        if (headerTitle != null)
                        {
                            var openedName = await headerTitle.InnerTextAsync();
                            if (openedName.Contains(groupName, StringComparison.OrdinalIgnoreCase))
                            {
                                groupFound = true;
                                break;
                            }
                        }

                        Console.WriteLine($"UYARI: '{groupName}' henüz tam yüklenmedi veya bulunamadı. Tekrar deneniyor... ({retry + 1}/5)");
                        await Task.Delay(5000); // Senkronizasyon için 5 sn bekle ve tekrar dene
                    }

                    if (!groupFound)
                    {
                        Console.WriteLine($"HATA: '{groupName}' grubu bulunamadı veya senkronize olmadı. Atlanıyor.");
                        continue;
                    }

                    // Wait for sync if needed
                    try
                    {
                        var syncLoc = page.Locator("text=/eski mesajlar senkronize|senkronize ediliyor|syncing older messages|ilerleme durumunu görmek için|click to see progress|daha fazla mesaj|get older messages/i").First;
                        if (await syncLoc.IsVisibleAsync())
                        {
                            await syncLoc.ClickAsync();
                            await syncLoc.WaitForAsync(new LocatorWaitForOptions { State = WaitForSelectorState.Hidden, Timeout = 300000 }); // 5 dakikaya kadar bekle
                            await Task.Delay(3000);
                        }
                    }
                    catch { }

                    DateTime today = DateTime.Today;
                    int daysSinceMonday = (int)today.DayOfWeek - (int)DayOfWeek.Monday;
                    if (daysSinceMonday < 0) daysSinceMonday += 7;
                    DateTime targetDate = today.AddDays(-daysSinceMonday).AddDays(-7);

                    var panelHandle = await page.QuerySelectorAsync("div[data-tab='8'], div[role='region']");

                    if (panelHandle != null)
                    {
                        // ── Initial read before any scrolling ──────────────────
                        await CollectVisibleMessages(page, groupName, instType, prefix,
                            docsFolder, targetDate, seenSignatures, processedBlobs, groupResults);

                        bool reachedTarget = false;
                        int sameCountTicks = 0;
                        int prevMsgCount = 0;

                        while (!reachedTarget)
                        {
                            // Check earliest visible message date
                            var firstDateStr = await page.EvaluateAsync<string>(@"() => {
                                let rows = document.querySelectorAll(""div[role='row']"");
                                for (let i = 0; i < rows.length; i++) {
                                    let el = rows[i].querySelector('div.copyable-text[data-pre-plain-text]');
                                    if (el) return el.getAttribute('data-pre-plain-text');
                                }
                                return null;
                            }");

                            if (!string.IsNullOrEmpty(firstDateStr))
                            {
                                var m = Regex.Match(firstDateStr, @"\b(\d{1,2})[\./-](\d{1,2})[\./-](\d{2,4})\b");
                                if (m.Success && DateTime.TryParse(m.Value, out DateTime msgDate) && msgDate < targetDate)
                                    reachedTarget = true;
                            }

                            if (reachedTarget) break;

                            // Stuck check
                            int curCount = await page.EvaluateAsync<int>("document.querySelectorAll(\"div[role='row']\").length");
                            if (curCount == prevMsgCount) { sameCountTicks++; if (sameCountTicks > 3) break; }
                            else sameCountTicks = 0;
                            prevMsgCount = curCount;
                            if (curCount > 2000) break;

                            // Scroll up
                            await panelHandle.FocusAsync();
                            for (int j = 0; j < 15; j++)
                            {
                                await page.Keyboard.PressAsync("PageUp");
                                await Task.Delay(100);
                            }
                            await Task.Delay(1500);

                            // ── Read messages exposed by this scroll ────────────
                            await CollectVisibleMessages(page, groupName, instType, prefix,
                                docsFolder, targetDate, seenSignatures, processedBlobs, groupResults);
                        }
                    }

                    // Save JSON for this group
                    string jsonPath = Path.Combine(groupFolder, $"{safeGrpName}.json");
                    await File.WriteAllTextAsync(jsonPath,
                        JsonSerializer.Serialize(groupResults, new JsonSerializerOptions { WriteIndented = true }));

                    totalResults.AddRange(groupResults);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Grup taramasi basarisiz: {groupName}. Hata: {ex.Message}");
                }
                finally
                {
                    await page.Keyboard.PressAsync("Escape");
                    await Task.Delay(1000);
                }
            }

            await browserContext.CloseAsync();
            return totalResults;
        }

        // ── Per-scroll message collection ──────────────────────────────────────
        private static async Task CollectVisibleMessages(
            IPage page,
            string groupName,
            string institutionType,
            string filePrefix,
            string docsFolder,
            DateTime targetDate,
            HashSet<string> seenSignatures,
            HashSet<string> processedBlobs,
            List<HomeworkItem> results)
        {
            var elements = await page.QuerySelectorAllAsync(
                "div[role='row'], div[class*='message-in'], div[class*='message-out']");

            foreach (var element in elements)
            {
                // ── Parse timestamp ─────────────────────────────────────────
                var hwItem = new HomeworkItem
                {
                    Source = groupName,
                    InstitutionType = institutionType,
                    Timestamp = DateTime.Now,
                    Content = ""
                };

                var prePlainEl = await element.QuerySelectorAsync("div.copyable-text[data-pre-plain-text]");
                if (prePlainEl != null)
                {
                    var ppt = await prePlainEl.GetAttributeAsync("data-pre-plain-text");
                    if (!string.IsNullOrEmpty(ppt))
                    {
                        var m = Regex.Match(ppt, @"\b(\d{1,2})[\./-](\d{1,2})[\./-](\d{2,4})\b");
                        if (m.Success && DateTime.TryParse(m.Value, out DateTime d))
                            hwItem.Timestamp = d;

                        var senderMatch = Regex.Match(ppt, @"\]\s*([^:]+):");
                        if (senderMatch.Success)
                            hwItem.Sender = senderMatch.Groups[1].Value.Trim();
                    }
                }

                if (hwItem.Timestamp < targetDate) continue;

                // ── Parse text early for dedup check ───────────────────────
                string innerText = "";
                var textSpan = await element.QuerySelectorAsync(
                    "span.selectable-text.copyable-text, span.selectable-text, span[dir='ltr']");
                if (textSpan != null)
                    innerText = (await textSpan.InnerTextAsync()).Trim();

                // Quick dedup: if text seen before, skip entirely (no media re-download either)
                if (!string.IsNullOrWhiteSpace(innerText))
                {
                    string sig = $"{hwItem.Timestamp.Date:yyyyMMdd}_{innerText.ToLowerInvariant()}";
                    if (seenSignatures.Contains(sig)) continue;
                    seenSignatures.Add(sig);
                    hwItem.Content = innerText;
                }

                // ── Download documents ──────────────────────────────────────
                var downloadBtns = await element.QuerySelectorAllAsync(
                    "button:has(span[data-icon='down']), div[role='button']:has(span[data-icon='down'])");
                foreach (var btn in downloadBtns)
                {
                    try
                    {
                        var download = await page.RunAndWaitForDownloadAsync(async () =>
                            await btn.ClickAsync(new ElementHandleClickOptions { Timeout = 2000 }),
                            new PageRunAndWaitForDownloadOptions { Timeout = 5000 });

                        string sf = download.SuggestedFilename;
                        if (IsValidFile(sf))
                        {
                            string absPath = Path.Combine(docsFolder, $"{filePrefix}{sf}");
                            await download.SaveAsAsync(absPath);
                            hwItem.MediaFiles.Add(absPath);
                        }
                        else { await download.CancelAsync(); }
                    }
                    catch { }
                }

                // ── Download images (blob URLs) ─────────────────────────────
                var imgs = await element.QuerySelectorAllAsync("img");
                foreach (var img in imgs)
                {
                    try
                    {
                        var src = await img.GetAttributeAsync("src");
                        if (string.IsNullOrEmpty(src) || !src.StartsWith("blob:")) continue;
                        if (processedBlobs.Contains(src)) continue; // already downloaded
                        processedBlobs.Add(src);

                        var base64Data = await page.EvaluateAsync<string>(@"(blobUrl) => {
                            return new Promise((resolve, reject) => {
                                fetch(blobUrl)
                                    .then(r => r.blob())
                                    .then(blob => {
                                        let reader = new FileReader();
                                        reader.onloadend = () => resolve(reader.result);
                                        reader.onerror   = reject;
                                        reader.readAsDataURL(blob);
                                    })
                                    .catch(reject);
                            });
                        }", src);

                        if (!string.IsNullOrEmpty(base64Data) && base64Data.Contains(","))
                        {
                            byte[] bytes = Convert.FromBase64String(base64Data.Split(',')[1]);
                            string fileName = $"{filePrefix}img_{DateTime.Now:MMdd_HHmmss}_{Guid.NewGuid().ToString()[..6]}.jpg";
                            string absPath = Path.Combine(docsFolder, fileName);
                            await File.WriteAllBytesAsync(absPath, bytes);

                            if (new FileInfo(absPath).Length > 200 * 1024 || !ImageAnalyzer.IsLikelyHomeworkPhoto(absPath))
                                File.Delete(absPath);
                            else
                                hwItem.MediaFiles.Add(absPath);
                        }
                    }
                    catch (Exception ex) { Console.WriteLine($"Resim indirme hatası: {ex.Message}"); }
                }

                if (!string.IsNullOrWhiteSpace(hwItem.Content) || hwItem.MediaFiles.Count > 0)
                    results.Add(hwItem);
            }
        }
    }
}
