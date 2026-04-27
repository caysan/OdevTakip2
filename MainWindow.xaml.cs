using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using OdevTakip2.Models;
using OdevTakip2.Services;

namespace OdevTakip2
{
    public partial class MainWindow : Window
    {
        private readonly WhatsAppScraperService _whatsAppService;
        private List<HomeworkItem> _collectedData = new();
        private readonly string _configPath;
        private readonly string _userFilesPath;

        public MainWindow()
        {
            InitializeComponent();
            _whatsAppService = new WhatsAppScraperService();

            string basePath = AppContext.BaseDirectory;
#if DEBUG
            DirectoryInfo dir = new DirectoryInfo(basePath);
            while (dir != null && dir.GetFiles("*.csproj").Length == 0)
                dir = dir.Parent;
            if (dir != null) basePath = dir.FullName;
#endif
            _userFilesPath = Path.Combine(basePath, "UserFiles");
            if (!Directory.Exists(_userFilesPath)) Directory.CreateDirectory(_userFilesPath);
            _configPath = Path.Combine(_userFilesPath, "config.json");

            UpdateScanTimesUI(GetConfig());
        }

        private void SaveConfig(AppConfig config)
        {
            try
            {
                File.WriteAllText(_configPath, JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
                Dispatcher.Invoke(() => UpdateScanTimesUI(config));
            }
            catch { }
        }

        private void UpdateScanTimesUI(AppConfig config)
        {
            txtLastScans.Text = $"Son Tarama - WP: {(config.LastWhatsAppScan?.ToString("dd.MM.yyyy HH:mm") ?? "Yok")} | E12: {(config.LastE12Scan?.ToString("dd.MM.yyyy HH:mm") ?? "Yok")} | AI Plan: {(config.LastAIGenerate?.ToString("dd.MM.yyyy HH:mm") ?? "Yok")}";
        }

        private AppConfig GetConfig()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    return JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath)) ?? new AppConfig();
                }
                catch { }
            }
            return new AppConfig();
        }

        private void Log(string message)
        {
            Dispatcher.Invoke(() =>
            {
                lstLogs.Items.Add($"[{DateTime.Now:HH:mm:ss}] {message}");
                lstLogs.SelectedIndex = lstLogs.Items.Count - 1;
                lstLogs.ScrollIntoView(lstLogs.SelectedItem);
            });
        }

        private void SetAllButtons(bool enabled)
        {
            btnStart.IsEnabled = enabled;
            btnE12Start.IsEnabled = enabled;
            btnGeneratePlan.IsEnabled = enabled;
            btnRunAll.IsEnabled = enabled;
        }

        private void BtnSettings_Click(object sender, RoutedEventArgs e)
        {
            var sw = new SettingsWindow(_configPath);
            sw.Owner = this;
            sw.ShowDialog();
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var config = GetConfig();
            if (config.WhatsAppGroupList.Count == 0)
            {
                MessageBox.Show("Lütfen öncelikle ⚙ Ayarlar menüsünden en az bir WhatsApp grup adı giriniz.");
                return;
            }

            SetAllButtons(false);
            Log($"WhatsApp botu başlatılıyor... Toplam {config.WhatsAppGroupList.Count} grup taranacak.");

            try
            {
                var messages = await _whatsAppService.ScrapeMessagesAsync(config.WhatsAppGroupList, scrollCount: 3);
                Log($"WP İşlemi tamamlandı. Toplam çekilen mesaj sayısı: {messages.Count}");
                _collectedData.AddRange(messages);
                foreach (var msg in messages)
                    Log($"- [{msg.Source}] {(msg.Content.Length > 50 ? msg.Content[..50] + "..." : msg.Content)}");

                var saveConfig = GetConfig();
                saveConfig.LastWhatsAppScan = DateTime.Now;
                SaveConfig(saveConfig);
                File.WriteAllText(Path.Combine(_userFilesPath, "latest_wp.json"), JsonSerializer.Serialize(messages));
            }
            catch (Exception ex) { Log($"Hata oluştu: {ex.Message}"); }
            finally { SetAllButtons(true); }
        }

        private async void BtnE12Start_Click(object sender, RoutedEventArgs e)
        {
            var config = GetConfig();
            if (config.E12AccountList.Count == 0)
            {
                MessageBox.Show("Lütfen önce Ayarlar (⚙) ekranından en az bir E12 hesabı ekleyiniz.");
                return;
            }

            SetAllButtons(false);
            Log($"E12 Veri aktarımı başlatılıyor... ({config.E12AccountList.Count} hesap)");

            try
            {
                var e12Messages = new List<HomeworkItem>();
                foreach (var account in config.E12AccountList)
                {
                    Log($"  E12 hesabı işleniyor: {account.InstitutionType} / {account.Username}");
                    var messages = await new E12ScraperService().ScrapeAsync(account, _userFilesPath);
                    e12Messages.AddRange(messages);
                    Log($"  → {messages.Count} ödev alındı.");
                }
                _collectedData.AddRange(e12Messages);

                var saveConfig = GetConfig();
                saveConfig.LastE12Scan = DateTime.Now;
                SaveConfig(saveConfig);
                File.WriteAllText(Path.Combine(_userFilesPath, "latest_e12.json"), JsonSerializer.Serialize(e12Messages));

                Log("E12 İşlemi tamamlandı.");
            }
            catch (Exception ex) { Log($"E12 Hatası: {ex.Message}"); }
            finally { SetAllButtons(true); }
        }

        // ── AI fallback helper ─────────────────────────────────────────────────
        private async Task<string> GenerateHtmlWithFallbackAsync(
            List<HomeworkItem> data, string? scheduleJson, AppConfig config)
        {
            var providers = config.AIProviders
                .Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ApiKey))
                .OrderBy(p => p.Priority)
                .ToList();

            if (providers.Count == 0)
                return "<html><body><h2>AI Sağlayıcı Tanımlı Değil</h2>" +
                       "<p>Ayarlar → AI Sağlayıcılar bölümünden en az bir sağlayıcı ekleyin.</p></body></html>";

            Exception? lastEx = null;
            for (int i = 0; i < providers.Count; i++)
            {
                var p = providers[i];
                Log($"AI isteği: {p.ProviderType} / {p.ModelId} (öncelik {p.Priority})");
                try
                {
                    return await new AIGeneratorService(p.ApiKey, p.ModelId, p.EndpointUrl)
                        .GenerateHtmlPlanAsync(data, scheduleJson, Log);
                }
                catch (Exception ex)
                {
                    lastEx = ex;
                    Log($"[{p.ProviderType}] başarısız: {ex.Message}");
                    if (i < providers.Count - 1)
                        Log("Sonraki sağlayıcı deneniyor...");
                }
            }
            return $"<html><body><h2>Tüm AI Sağlayıcılar Başarısız</h2><p>{lastEx?.Message}</p></body></html>";
        }



        private async void BtnGeneratePlan_Click(object sender, RoutedEventArgs e)
        {
            var config = GetConfig();
            if (!config.AIProviders.Any(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ApiKey)))
            {
                MessageBox.Show("Öncelikle Ayarlar (⚙) ekranından AI Sağlayıcılar sekmesine bir API Key ekleyiniz.");
                return;
            }

            _collectedData.Clear();
            string wpPath = Path.Combine(_userFilesPath, "latest_wp.json");
            if (File.Exists(wpPath))
            {
                try { _collectedData.AddRange(JsonSerializer.Deserialize<List<HomeworkItem>>(File.ReadAllText(wpPath)) ?? new()); } catch { }
            }
            string e12Path = Path.Combine(_userFilesPath, "latest_e12.json");
            if (File.Exists(e12Path))
            {
                try { _collectedData.AddRange(JsonSerializer.Deserialize<List<HomeworkItem>>(File.ReadAllText(e12Path)) ?? new()); } catch { }
            }

            if (_collectedData.Count == 0)
            {
                MessageBox.Show("Lütfen önce veri toplayınız (önceki başarılı tarama verisi bulunamadı).");
                return;
            }

            SetAllButtons(false);
            try
            {
                await GeneratePlanFilesAsync(config);
            }
            catch (Exception ex) { Log($"AI Hatası: {ex.Message}"); }
            finally { SetAllButtons(true); }
        }

        private async void BtnRunAll_Click(object sender, RoutedEventArgs e)
        {
            var config = GetConfig();
            if (!config.AIProviders.Any(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ApiKey)))
            {
                MessageBox.Show("Öncelikle Ayarlar (⚙) ekranından AI Sağlayıcılar sekmesine bir API Key ekleyiniz.");
                return;
            }

            SetAllButtons(false);
            _collectedData.Clear();
            Log("═══ [1/3] WhatsApp taraması başlıyor... ═══");

            try
            {
                if (config.WhatsAppGroupList.Count > 0)
                {
                    var wpMessages = await _whatsAppService.ScrapeMessagesAsync(config.WhatsAppGroupList, scrollCount: 3);
                    _collectedData.AddRange(wpMessages);
                    Log($"[1/3] WP tamamlandı: {wpMessages.Count} mesaj alındı.");

                    config.LastWhatsAppScan = DateTime.Now;
                    File.WriteAllText(Path.Combine(_userFilesPath, "latest_wp.json"), JsonSerializer.Serialize(wpMessages));
                }
                else
                    Log("[1/3] WP atlandı (grup tanımlı değil).");

                Log("═══ [2/3] E12 verisi çekiliyor... ═══");
                var currentE12 = new List<HomeworkItem>();
                if (config.E12AccountList.Count > 0)
                {
                    foreach (var account in config.E12AccountList)
                    {
                        try
                        {
                            Log($"  E12: {account.InstitutionType} / {account.Username}");
                            var msgs = await new E12ScraperService().ScrapeAsync(account, _userFilesPath);
                            currentE12.AddRange(msgs);
                            Log($"  → {msgs.Count} ödev alındı.");
                        }
                        catch (Exception ex) { Log($"  E12 hatası ({account.InstitutionType}): {ex.Message}"); }
                    }
                    _collectedData.AddRange(currentE12);
                    Log("[2/3] E12 tamamlandı.");

                    config.LastE12Scan = DateTime.Now;
                    File.WriteAllText(Path.Combine(_userFilesPath, "latest_e12.json"), JsonSerializer.Serialize(currentE12));
                }
                else
                    Log("[2/3] E12 atlandı (hesap tanımlı değil).");

                SaveConfig(config);

                if (_collectedData.Count == 0)
                {
                    Log("[!] Hiç veri toplanamadı. Plan oluşturma atlandı.");
                    return;
                }

                await GeneratePlanFilesAsync(config);
            }
            catch (Exception ex) { Log($"Hata: {ex.Message}"); }
            finally { SetAllButtons(true); }
        }

        private async Task GeneratePlanFilesAsync(AppConfig config)
        {
            Log($"═══ AI Plan oluşturuluyor... (Toplam: {_collectedData.Count} veri) ═══");
            string? scheduleJsonStr = await LoadSchedulesAsync(config);

            Dictionary<string, object>? allSchedules = null;
            if (!string.IsNullOrEmpty(scheduleJsonStr))
            {
                try { allSchedules = JsonSerializer.Deserialize<Dictionary<string, object>>(scheduleJsonStr); } catch { }
            }

            var groupedData = _collectedData.GroupBy(x => string.IsNullOrWhiteSpace(x.InstitutionType) ? "Diger" : x.InstitutionType).ToList();

            var combinedHtmlBuilder = new System.Text.StringBuilder();
            combinedHtmlBuilder.AppendLine("<html><head><meta charset=\"UTF-8\">");
            combinedHtmlBuilder.AppendLine("<style>body { font-family: sans-serif; font-size: 10.5pt; margin: 8mm; } table { width: 100%; border-collapse: collapse; margin-bottom: 20px; } th, td { border: 1px solid #ddd; padding: 8px; text-align: left; } th { background-color: #f2f2f2; } h2 { color: #333; margin-top: 30px; border-bottom: 2px solid #333; padding-bottom: 5px; }</style>");
            combinedHtmlBuilder.AppendLine("</head><body>");
            combinedHtmlBuilder.AppendLine($"<p style='text-align: center; color: #555; font-style: italic;'>Oluşturulma: {DateTime.Now:dd MMMM yyyy HH:mm}</p>");

            foreach (var group in groupedData)
            {
                string instType = group.Key;
                Log($"  → '{instType}' ödevleri işleniyor... ({group.Count()} ödev)");

                string? specificScheduleJson = null;
                if (allSchedules != null && allSchedules.ContainsKey(instType))
                {
                    specificScheduleJson = JsonSerializer.Serialize(new Dictionary<string, object> { { instType, allSchedules[instType] } });
                }

                var htmlResult = await GenerateHtmlWithFallbackAsync(group.ToList(), specificScheduleJson, config);

                // Ortaya çıkan HTML sayfasından sadece <body> içini alıyoruz (ya da rootu temizliyoruz)
                var bodyMatch = System.Text.RegularExpressions.Regex.Match(htmlResult, @"<body[^>]*>(.*?)</body>", System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Singleline);
                string innerHtml = bodyMatch.Success ? bodyMatch.Groups[1].Value : htmlResult;

                // Root tag'leri temizle ki çakışmasın
                innerHtml = System.Text.RegularExpressions.Regex.Replace(innerHtml, @"</?html[^>]*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                innerHtml = System.Text.RegularExpressions.Regex.Replace(innerHtml, @"</?head[^>]*>", "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                combinedHtmlBuilder.AppendLine(innerHtml);
            }

            combinedHtmlBuilder.AppendLine("</body></html>");

            string filePath = Path.Combine(_userFilesPath, "Odev_Plani.html");
            await File.WriteAllTextAsync(filePath, combinedHtmlBuilder.ToString());
            Log($"  [OK] Plan oluşturuldu ve üzerine yazıldı: {filePath}");

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true });

            var saveConfig = GetConfig();
            saveConfig.LastAIGenerate = DateTime.Now;
            SaveConfig(saveConfig);
        }

        private async Task<string?> LoadSchedulesAsync(AppConfig config)
        {
            var schedules = new Dictionary<string, object>();

            foreach (var sc in config.ScheduleList)
            {
                if (string.IsNullOrWhiteSpace(sc.InstitutionType)) continue;
                string safeType = SafeFileName(sc.InstitutionType);
                string schPath = Path.Combine(_userFilesPath, $"{safeType}_DersProgrami.json");

                // Re-analyze if image changed since last analysis
                if (!string.IsNullOrEmpty(sc.ImagePath) && File.Exists(sc.ImagePath) && File.Exists(schPath))
                {
                    string currentHash = ComputeFileHash(sc.ImagePath);
                    if (currentHash != sc.ImageHash)
                    {
                        Log($"Ders programı değişti ({sc.InstitutionType}), yeniden analiz ediliyor...");
                        try
                        {
                            var p = config.AIProviders
                                .Where(x => x.IsEnabled && !string.IsNullOrWhiteSpace(x.ApiKey))
                                .OrderBy(x => x.Priority).First();
                            string newJson = await new AIGeneratorService(p.ApiKey, p.ModelId, p.EndpointUrl)
                                .ConvertScheduleToJsonAsync(sc.ImagePath, Log);
                            await File.WriteAllTextAsync(schPath, newJson);
                        }
                        catch (Exception ex) { Log($"Ders programı analiz hatası: {ex.Message}"); }
                    }
                }

                if (!File.Exists(schPath)) continue;
                try
                {
                    var content = await File.ReadAllTextAsync(schPath);
                    using var doc = JsonDocument.Parse(content);
                    schedules[sc.InstitutionType] = doc.RootElement.Clone();
                }
                catch { }
            }

            return schedules.Count > 0
                ? JsonSerializer.Serialize(schedules, new JsonSerializerOptions { WriteIndented = false })
                : null;
        }

        private static string SafeFileName(string name)
            => string.Concat(name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");

        private static string ComputeFileHash(string path)
        {
            using var md5 = MD5.Create();
            using var stream = File.OpenRead(path);
            return Convert.ToHexString(md5.ComputeHash(stream));
        }
    }
}
