using System;
using System.Windows;
using OdevTakip2.Services;

namespace OdevTakip2
{
    public partial class MainWindow : Window
    {
        private readonly WhatsAppScraperService _whatsAppService;
        private List<OdevTakip2.Models.HomeworkItem> _collectedData = new();

        public MainWindow()
        {
            InitializeComponent();
            _whatsAppService = new WhatsAppScraperService();
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

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            var groupName = txtGroupName.Text;
            if (string.IsNullOrWhiteSpace(groupName))
            {
                MessageBox.Show("Lütfen bir grup adı giriniz.");
                return;
            }

            btnStart.IsEnabled = false;
            Log($"WhatsApp botu başlatılıyor... Hedef: {groupName}");

            try
            {
                var messages = await _whatsAppService.ScrapeMessagesAsync(groupName, scrollCount: 3);
                
                Log($"İşlem tamamlandı. Toplam çekilen text mesaj sayısı: {messages.Count}");
                _collectedData.AddRange(messages); // Ana havuza ekle

                foreach(var msg in messages)
                {
                    Log($"- {msg.Content}");
                }
            }
            catch (Exception ex)
            {
                Log($"Hata oluştu: {ex.Message}");
            }
            finally
            {
                btnStart.IsEnabled = true;
            }
        }

        private async void BtnGeneratePlan_Click(object sender, RoutedEventArgs e)
        {
            var apiKey = txtApiKey.Text;
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                MessageBox.Show("Öncelikle bir Gemini API Key giriniz.");
                return;
            }

            if (_collectedData.Count == 0)
            {
                MessageBox.Show("Lütfen önce veri toplayınız (WP Botu başlatarak).");
                return;
            }

            btnGeneratePlan.IsEnabled = false;
            Log($"AI Plan oluşturuluyor... (Toplam Veri: {_collectedData.Count})");

            try
            {
                var aiService = new AIGeneratorService(apiKey);
                var htmlResult = await aiService.GenerateHtmlPlanAsync(_collectedData);

                string filePath = System.IO.Path.Combine(Environment.CurrentDirectory, "plan.html");
                await System.IO.File.WriteAllTextAsync(filePath, htmlResult);

                Log($"Başarılı! Plan oluşturuldu: {filePath}");
                
                // HTML Dosyasını varsayılan tarayıcıda aç
                var sInfo = new System.Diagnostics.ProcessStartInfo(filePath) { UseShellExecute = true };
                System.Diagnostics.Process.Start(sInfo);
            }
            catch (Exception ex)
            {
                Log($"AI Hatası: {ex.Message}");
            }
            finally
            {
                btnGeneratePlan.IsEnabled = true;
            }
        }
    }
}