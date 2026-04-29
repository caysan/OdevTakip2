using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using OdevTakip2.Models;
using OdevTakip2.Services;

namespace OdevTakip2
{
    public partial class SettingsWindow : Window
    {
        private readonly string _configPath;
        private readonly string _userFilesPath;

        public ObservableCollection<string> InstitutionTypes { get; } = new();
        public ObservableCollection<WhatsAppGroupViewModel> WhatsAppGroupItems { get; } = new();
        public ObservableCollection<E12AccountViewModel> E12AccountItems { get; } = new();
        public ObservableCollection<OrganizationItem> Organizations { get; } = new();
        public ObservableCollection<ScheduleEntryViewModel> ScheduleItems { get; } = new();
        public ObservableCollection<AIProviderViewModel> AIProviderItems { get; } = new();

        public string[] AIProviderTypeOptions { get; } = { "Gemini", "OpenAI" };
        public int[] AIProviderPriorityOptions { get; } = { 1, 2, 3, 4, 5 };

        public SettingsWindow(string configPath)
        {
            InitializeComponent();
            _configPath = configPath;
            _userFilesPath = Path.GetDirectoryName(_configPath) ?? AppContext.BaseDirectory;
            Loaded += SettingsWindow_Loaded;
        }

        private async void SettingsWindow_Loaded(object sender, RoutedEventArgs e)
        {
            await LoadOrganizationsAsync();
            LoadConfig();
        }

        // ── ViewModels ─────────────────────────────────────────────────────────
        public class WhatsAppGroupViewModel : INotifyPropertyChanged
        {
            private string _name = "", _type = "";
            public string Name { get => _name; set { _name = value; OnPC(nameof(Name)); } }
            public string InstitutionType { get => _type; set { _type = value; OnPC(nameof(InstitutionType)); } }
            public event PropertyChangedEventHandler? PropertyChanged;
            void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public class E12AccountViewModel : INotifyPropertyChanged
        {
            private string _orgId = "", _user = "", _pass = "", _type = "";
            public string OrganizationId { get => _orgId; set { _orgId = value; OnPC(nameof(OrganizationId)); } }
            public string Username { get => _user; set { _user = value; OnPC(nameof(Username)); } }
            public string Password { get => _pass; set { _pass = value; OnPC(nameof(Password)); } }
            public string InstitutionType { get => _type; set { _type = value; OnPC(nameof(InstitutionType)); } }
            public event PropertyChangedEventHandler? PropertyChanged;
            void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public class ScheduleEntryViewModel : INotifyPropertyChanged
        {
            private string _type = "", _path = "", _status = "", _hash = "";
            public string InstitutionType { get => _type; set { _type = value; OnPC(nameof(InstitutionType)); } }
            public string ImagePath { get => _path; set { _path = value; OnPC(nameof(ImagePath)); } }
            public string Status { get => _status; set { _status = value; OnPC(nameof(Status)); } }
            public string ImageHash { get => _hash; set { _hash = value; } }
            public event PropertyChangedEventHandler? PropertyChanged;
            void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public class AIProviderViewModel : INotifyPropertyChanged
        {
            private static readonly string[] GeminiModels = { "gemini-2.5-flash", "gemini-2.5-pro", "gemini-2.0-flash", "gemini-1.5-pro", "gemini-1.5-flash" };
            private static readonly string[] OpenAIModels = { "gpt-4o", "gpt-4o-mini", "gpt-4-turbo", "gpt-3.5-turbo" };

            private string _providerType = "Gemini";
            private string _apiKey = "";
            private string _modelId = "gemini-1.5-flash";
            private int _priority = 1;
            private bool _isEnabled = true;

            public string ProviderType
            {
                get => _providerType;
                set
                {
                    if (_providerType == value) return;
                    _providerType = value;
                    OnPC(nameof(ProviderType));
                    OnPC(nameof(AvailableModels));
                    if (!AvailableModels.Contains(_modelId))
                        ModelId = AvailableModels[0];
                }
            }
            public string ApiKey { get => _apiKey; set { _apiKey = value; OnPC(nameof(ApiKey)); } }
            public string ModelId { get => _modelId; set { _modelId = value; OnPC(nameof(ModelId)); } }
            public int Priority { get => _priority; set { _priority = value; OnPC(nameof(Priority)); } }
            public bool IsEnabled { get => _isEnabled; set { _isEnabled = value; OnPC(nameof(IsEnabled)); } }

            public string[] AvailableModels => _providerType == "OpenAI" ? OpenAIModels : GeminiModels;

            public event PropertyChangedEventHandler? PropertyChanged;
            void OnPC(string n) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }

        public class OrganizationItem
        {
            public string Id { get; set; } = string.Empty;
            public string Name { get; set; } = string.Empty;
        }

        // ── Load ───────────────────────────────────────────────────────────────
        private async Task LoadOrganizationsAsync()
        {
            try
            {
                using var client = new HttpClient();
                client.DefaultRequestHeaders.Add("Accept", "application/json, text/plain, */*");
                var json = await client.GetStringAsync(
                    "https://ogrenci.e12.com.tr/api/organizations/cache?page=1&perPage=250");
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("data", out var data) &&
                    data.TryGetProperty("items", out var items))
                {
                    foreach (var item in items.EnumerateArray())
                        Organizations.Add(new OrganizationItem
                        {
                            Id = item.GetProperty("id").GetString() ?? "",
                            Name = item.GetProperty("name").GetString() ?? ""
                        });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("Okul listesi alınamadı: " + ex.Message, "Uyarı",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void LoadConfig()
        {
            if (!File.Exists(_configPath))
            {
                InstitutionTypes.Add("Okul");
                InstitutionTypes.Add("Dershane");
                return;
            }
            try
            {
                var config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath));
                if (config == null) return;

                foreach (var t in config.InstitutionTypes) InstitutionTypes.Add(t);
                if (InstitutionTypes.Count == 0) { InstitutionTypes.Add("Okul"); InstitutionTypes.Add("Dershane"); }

                foreach (var g in config.WhatsAppGroupList)
                    WhatsAppGroupItems.Add(new WhatsAppGroupViewModel { Name = g.Name, InstitutionType = g.InstitutionType });

                foreach (var a in config.E12AccountList)
                    E12AccountItems.Add(new E12AccountViewModel
                    {
                        OrganizationId = a.OrganizationId,
                        Username = a.Username,
                        Password = a.Password,
                        InstitutionType = a.InstitutionType
                    });

                foreach (var s in config.ScheduleList)
                {
                    string status = "";
                    if (!string.IsNullOrEmpty(s.ImagePath))
                    {
                        string safeType = SafeFileName(s.InstitutionType);
                        string schFile = Path.Combine(_userFilesPath, $"{safeType}_DersProgrami.json");
                        status = File.Exists(schFile) ? "Analiz mevcut." : "Henüz analiz yapılmadı.";
                    }
                    ScheduleItems.Add(new ScheduleEntryViewModel
                    {
                        InstitutionType = s.InstitutionType,
                        ImagePath = s.ImagePath,
                        ImageHash = s.ImageHash,
                        Status = status
                    });
                }

                // AI providers — migrate legacy ApiKey if needed
                if (config.AIProviders.Count > 0)
                {
                    foreach (var p in config.AIProviders)
                        AIProviderItems.Add(new AIProviderViewModel
                        {
                            ProviderType = p.ProviderType,
                            ApiKey = p.ApiKey,
                            ModelId = p.ModelId,
                            Priority = p.Priority,
                            IsEnabled = p.IsEnabled
                        });
                }
                else if (!string.IsNullOrWhiteSpace(config.ApiKey))
                {
                    AIProviderItems.Add(new AIProviderViewModel
                    {
                        ProviderType = "Gemini",
                        ApiKey = config.ApiKey,
                        ModelId = "gemini-1.5-flash",
                        Priority = 1,
                        IsEnabled = true
                    });
                }
            }
            catch { }
        }

        // ── WhatsApp groups ────────────────────────────────────────────────────
        private void BtnAddGroup_Click(object sender, RoutedEventArgs e)
            => WhatsAppGroupItems.Add(new WhatsAppGroupViewModel
            { InstitutionType = InstitutionTypes.Count > 0 ? InstitutionTypes[0] : "" });

        private void BtnRemoveGroup_Click(object sender, RoutedEventArgs e)
        { if (((Button)sender).Tag is WhatsAppGroupViewModel vm) WhatsAppGroupItems.Remove(vm); }

        // ── E12 accounts ───────────────────────────────────────────────────────
        private void BtnAddE12Account_Click(object sender, RoutedEventArgs e)
            => E12AccountItems.Add(new E12AccountViewModel
            { InstitutionType = InstitutionTypes.Count > 0 ? InstitutionTypes[0] : "" });

        private void BtnRemoveE12Account_Click(object sender, RoutedEventArgs e)
        { if (((Button)sender).Tag is E12AccountViewModel vm) E12AccountItems.Remove(vm); }

        // ── Institution types ──────────────────────────────────────────────────
        private void BtnAddInstitutionType_Click(object sender, RoutedEventArgs e)
        {
            var text = txtNewInstitutionType.Text.Trim();
            if (!string.IsNullOrEmpty(text) && !InstitutionTypes.Contains(text))
            {
                InstitutionTypes.Add(text);
                txtNewInstitutionType.Text = "";
            }
        }

        private void BtnRemoveInstitutionType_Click(object sender, RoutedEventArgs e)
        { if (((Button)sender).Tag is string t) InstitutionTypes.Remove(t); }

        // ── AI providers ──────────────────────────────────────────────────────
        private void BtnAddAIProvider_Click(object sender, RoutedEventArgs e)
            => AIProviderItems.Add(new AIProviderViewModel { Priority = AIProviderItems.Count + 1 });

        private void BtnRemoveAIProvider_Click(object sender, RoutedEventArgs e)
        { if (((Button)sender).Tag is AIProviderViewModel vm) AIProviderItems.Remove(vm); }

        private async void BtnAiTest_Click(object sender, RoutedEventArgs e)
        {
            var providers = System.Linq.Enumerable.ToList(System.Linq.Enumerable.OrderBy(System.Linq.Enumerable.Where(AIProviderItems, p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ApiKey)), p => p.Priority));
            if (providers.Count == 0)
            {
                MessageBox.Show("Öncelikle listeye bir API Key ekleyiniz ve aktif yapınız.");
                return;
            }

            btnAiTest.IsEnabled = false;

            try
            {
                bool success = false;
                foreach (var p in providers)
                {
                    try
                    {
                        var aiSvc = new AIGeneratorService(p.ApiKey, p.ModelId, p.ProviderType == "OpenAI" ? "" : "https://generativelanguage.googleapis.com/v1beta/openai/");
                        string reply = await aiSvc.TestConnectionAsync("Merhaba, sesim geliyor mu?", null);
                        MessageBox.Show($"Başarılı! [{p.ProviderType}] Yanıtı:\n\n{reply}", "AI Test Başarılı", MessageBoxButton.OK, MessageBoxImage.Information);
                        success = true;
                        break;
                    }
                    catch (Exception)
                    {
                        // ignore and try next
                    }
                }

                if (!success)
                {
                    MessageBox.Show("Mevcut hiçbir AI sağlayıcısına bağlanılamadı. Lütfen API Key veya bağlantı ayarlarını kontrol edin.", "Bağlantı Hatası", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            finally
            {
                btnAiTest.IsEnabled = true;
            }
        }

        // ── Schedule entries ───────────────────────────────────────────────────
        private void BtnAddScheduleEntry_Click(object sender, RoutedEventArgs e)
            => ScheduleItems.Add(new ScheduleEntryViewModel
            { InstitutionType = InstitutionTypes.Count > 0 ? InstitutionTypes[0] : "" });

        private void BtnRemoveScheduleEntry_Click(object sender, RoutedEventArgs e)
        { if (((Button)sender).Tag is ScheduleEntryViewModel vm) ScheduleItems.Remove(vm); }

        private void BtnBrowseScheduleEntry_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).Tag is not ScheduleEntryViewModel vm) return;
            var dlg = new OpenFileDialog
            {
                Title = "Ders Programı Görseli Seç",
                Filter = "Görsel Dosyalar|*.jpg;*.jpeg;*.png;*.bmp|Tüm Dosyalar|*.*"
            };
            if (dlg.ShowDialog() == true) vm.ImagePath = dlg.FileName;
        }

        private async void BtnAnalyzeScheduleEntry_Click(object sender, RoutedEventArgs e)
        {
            if (((Button)sender).Tag is not ScheduleEntryViewModel vm) return;

            if (string.IsNullOrWhiteSpace(vm.ImagePath) || !File.Exists(vm.ImagePath))
            {
                MessageBox.Show("Lütfen önce geçerli bir görsel seçin (Gözat butonu ile).");
                return;
            }
            var firstProvider = AIProviderItems.Where(p => p.IsEnabled && !string.IsNullOrWhiteSpace(p.ApiKey))
                                                  .OrderBy(p => p.Priority).FirstOrDefault();
            if (firstProvider == null)
            {
                MessageBox.Show("AI analizi için 'AI Sağlayıcılar' sekmesinden en az bir aktif sağlayıcı ekleyiniz.");
                return;
            }
            var apiKey = firstProvider.ApiKey;
            var aiSvc = new AIGeneratorService(apiKey, firstProvider.ModelId,
                firstProvider.ProviderType == "OpenAI" ? "" : "https://generativelanguage.googleapis.com/v1beta/openai/");

            vm.Status = "AI analiz ediyor...";
            ((Button)sender).IsEnabled = false;
            try
            {
                string json = await aiSvc.ConvertScheduleToJsonAsync(vm.ImagePath);
                string safeType = SafeFileName(vm.InstitutionType);
                string schPath = Path.Combine(_userFilesPath, $"{safeType}_DersProgrami.json");
                await File.WriteAllTextAsync(schPath, json);

                using var md5 = MD5.Create();
                using var stream = File.OpenRead(vm.ImagePath);
                vm.ImageHash = Convert.ToHexString(md5.ComputeHash(stream));

                // Persist hash immediately so MainWindow won't re-analyze
                SaveScheduleHashToConfig(vm);

                vm.Status = $"Tamamlandı — {DateTime.Now:HH:mm}";
            }
            catch (Exception ex)
            {
                vm.Status = "Hata!";
                MessageBox.Show($"Analiz hatası: {ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally { ((Button)sender).IsEnabled = true; }
        }

        private void SaveScheduleHashToConfig(ScheduleEntryViewModel updated)
        {
            try
            {
                var config = new AppConfig();
                if (File.Exists(_configPath))
                    config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath)) ?? new AppConfig();

                var existing = config.ScheduleList.Find(s => s.InstitutionType == updated.InstitutionType);
                if (existing != null)
                {
                    existing.ImagePath = updated.ImagePath;
                    existing.ImageHash = updated.ImageHash;
                }
                else
                {
                    config.ScheduleList.Add(new ScheduleConfig
                    {
                        InstitutionType = updated.InstitutionType,
                        ImagePath = updated.ImagePath,
                        ImageHash = updated.ImageHash
                    });
                }
                File.WriteAllText(_configPath,
                    JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        // ── Other Settings ─────────────────────────────────────────────────────
        private void BtnClearWPCache_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var result = MessageBox.Show("WhatsApp önbelleği silinecek ve yeniden QR kod okutmanız gerekecek. Onaylıyor musunuz?", "Onay", MessageBoxButton.YesNo, MessageBoxImage.Question);
                if (result == MessageBoxResult.Yes)
                {
                    string wpSessionPath = Path.Combine(_userFilesPath, "WhatsAppSession");
                    if (Directory.Exists(wpSessionPath))
                    {
                        Directory.Delete(wpSessionPath, true);
                        MessageBox.Show("WhatsApp önbelleği başarıyla temizlendi.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                    else
                    {
                        MessageBox.Show("Temizlenecek bir önbellek bulunamadı.", "Bilgi", MessageBoxButton.OK, MessageBoxImage.Information);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Önbellek temizlenirken hata oluştu:\n{ex.Message}", "Hata", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ── Save ───────────────────────────────────────────────────────────────
        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var config = new AppConfig();
                if (File.Exists(_configPath))
                    config = JsonSerializer.Deserialize<AppConfig>(File.ReadAllText(_configPath)) ?? new AppConfig();

                config.InstitutionTypes = new List<string>(InstitutionTypes);

                config.WhatsAppGroupList = new List<WhatsAppGroupConfig>();
                foreach (var vm in WhatsAppGroupItems)
                    if (!string.IsNullOrWhiteSpace(vm.Name))
                        config.WhatsAppGroupList.Add(new WhatsAppGroupConfig
                        { Name = vm.Name.Trim(), InstitutionType = vm.InstitutionType ?? "" });

                config.E12AccountList = new List<E12AccountConfig>();
                foreach (var vm in E12AccountItems)
                    if (!string.IsNullOrWhiteSpace(vm.Username))
                        config.E12AccountList.Add(new E12AccountConfig
                        {
                            OrganizationId = vm.OrganizationId,
                            Username = vm.Username,
                            Password = vm.Password,
                            InstitutionType = vm.InstitutionType ?? ""
                        });

                config.ScheduleList = new List<ScheduleConfig>();
                foreach (var vm in ScheduleItems)
                    config.ScheduleList.Add(new ScheduleConfig
                    {
                        InstitutionType = vm.InstitutionType,
                        ImagePath = vm.ImagePath,
                        ImageHash = vm.ImageHash
                    });

                config.ApiKey = "";  // clear legacy
                config.AIProviders = new List<AIProviderConfig>();
                foreach (var vm in AIProviderItems)
                    if (!string.IsNullOrWhiteSpace(vm.ApiKey))
                        config.AIProviders.Add(new AIProviderConfig
                        {
                            ProviderType = vm.ProviderType,
                            ApiKey = vm.ApiKey,
                            ModelId = vm.ModelId,
                            Priority = vm.Priority,
                            IsEnabled = vm.IsEnabled
                        });

                File.WriteAllText(_configPath,
                    JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true }));

                MessageBox.Show("Ayarlar başarıyla kaydedildi.", "Bilgi",
                                MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Kaydetme hatası: {ex.Message}", "Hata",
                                MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private static string SafeFileName(string name)
            => string.Concat(name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");
    }
}
