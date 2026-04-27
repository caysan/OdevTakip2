using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using OdevTakip2.Models;

namespace OdevTakip2.Services
{
    public class E12ScraperService
    {
        public async Task<List<HomeworkItem>> ScrapeAsync(E12AccountConfig account, string userFilesPath)
        {
            if (!Directory.Exists(userFilesPath)) Directory.CreateDirectory(userFilesPath);
            var results = new List<HomeworkItem>();

            try
            {
                string token = await GetTokenAsync(account.OrganizationId, account.Username, account.Password);
                if (string.IsNullOrEmpty(token))
                    throw new Exception("E12 token alınamadı. Kullanıcı adı, parola veya okulu kontrol ediniz.");

                string safeType = string.IsNullOrEmpty(account.InstitutionType) ? "Diger" : SafeFileName(account.InstitutionType);

                // Schedule: save dated + per-institution-type cache
                var scheduleJson = await GetClassScheduleAsync(token);
                await File.WriteAllTextAsync(
                    Path.Combine(userFilesPath, $"{safeType}_E12_Ders_Programi_{DateTime.Now:yyyyMMdd}.json"),
                    scheduleJson);

                if (!string.IsNullOrEmpty(account.InstitutionType))
                {
                    await File.WriteAllTextAsync(
                        Path.Combine(userFilesPath, $"{safeType}_DersProgrami.json"),
                        scheduleJson);
                }

                // Homework
                var assignmentsJson = await GetHomeworkAssignmentsAsync(token);
                await File.WriteAllTextAsync(
                    Path.Combine(userFilesPath, $"{safeType}_E12_Odevler_{DateTime.Now:yyyyMMdd}.json"),
                    assignmentsJson);

                using var jsonDoc = JsonDocument.Parse(assignmentsJson);
                if (jsonDoc.RootElement.TryGetProperty("data", out var dataElement))
                {
                    JsonElement itemsElement;
                    if (dataElement.ValueKind == JsonValueKind.Array)
                        itemsElement = dataElement;
                    else if (dataElement.TryGetProperty("items", out var nested))
                        itemsElement = nested;
                    else
                        return results;

                    if (itemsElement.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in itemsElement.EnumerateArray())
                        {
                            if (!item.TryGetProperty("homework", out var hwEl)) continue;

                            string title = hwEl.TryGetProperty("title", out var t) ? t.GetString() ?? "" : "";
                            string desc = hwEl.TryGetProperty("description", out var d) ? d.GetString() ?? "" : "";
                            string lessonName = "";
                            if (hwEl.TryGetProperty("lesson", out var les) && les.TryGetProperty("name", out var ln))
                                lessonName = ln.GetString() ?? "";

                            string cleanDesc = System.Text.RegularExpressions.Regex.Replace(desc, "<.*?>", "").Trim();
                            string content = $"{lessonName} - {title}\n{cleanDesc}";

                            DateTime? dueDate = null;
                            if (item.TryGetProperty("dueDate", out var dt1) && dt1.ValueKind == JsonValueKind.String)
                            { if (DateTime.TryParse(dt1.GetString(), out var d1)) dueDate = d1; }
                            else if (hwEl.TryGetProperty("dueDate", out var dt2) && dt2.ValueKind == JsonValueKind.String)
                            { if (DateTime.TryParse(dt2.GetString(), out var d2)) dueDate = d2; }

                            string senderName = "";
                            if (hwEl.TryGetProperty("createdByName", out var cbn)) senderName = cbn.GetString() ?? "";
                            if (string.IsNullOrWhiteSpace(senderName) && item.TryGetProperty("createdByName", out var cbn2)) senderName = cbn2.GetString() ?? "";

                            results.Add(new HomeworkItem
                            {
                                Source = "E12",
                                InstitutionType = account.InstitutionType,
                                Content = content,
                                Timestamp = DateTime.Now,
                                DueDate = dueDate,
                                Sender = senderName
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"E12 Scraping Error ({account.InstitutionType}): {ex.Message}");
                throw;
            }

            return results;
        }

        private static string SafeFileName(string name)
            => string.Concat(name.Split(Path.GetInvalidFileNameChars())).Replace(" ", "_");

        private static async Task<string> GetTokenAsync(string organizationId, string identifier, string password)
        {
            using var client = new HttpClient();
            var request = new HttpRequestMessage(HttpMethod.Post, "https://ogrenci.e12.com.tr/api/authentication/login");
            request.Headers.Add("sec-ch-ua-platform", "\"Windows\"");
            request.Headers.Add("Referer", "https://ogrenci.e12.com.tr/signin");
            request.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
            request.Headers.Add("Accept", "application/json, text/plain, */*");
            request.Headers.Add("sec-ch-ua", "\"Chromium\";v=\"146\", \"Not-A.Brand\";v=\"24\", \"Google Chrome\";v=\"146\"");
            request.Content = new StringContent(
                $"{{\"identifier\":\"{identifier}\",\"password\":\"{password}\",\"organizationID\":\"{organizationId}\"}}",
                Encoding.UTF8, "application/json");

            var response = await client.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                throw new Exception($"Login failed: {response.StatusCode} {await response.Content.ReadAsStringAsync()}");

            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            if (doc.RootElement.TryGetProperty("data", out var data) && data.TryGetProperty("token", out var token))
                return token.GetString() ?? "";
            return "";
        }

        private static async Task<string> GetClassScheduleAsync(string token)
        {
            using var client = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Get, "https://ogrenci.e12.com.tr/api/class-schedules/my-schedule");
            req.Headers.Add("Authorization", $"Bearer {token}");
            req.Headers.Add("Referer", "https://ogrenci.e12.com.tr/timeline");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            req.Headers.Add("Accept", "application/json, text/plain, */*");
            var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync();
        }

        private static async Task<string> GetHomeworkAssignmentsAsync(string token)
        {
            using var client = new HttpClient();
            var req = new HttpRequestMessage(HttpMethod.Get,
                "https://ogrenci.e12.com.tr/api/homework-assignments?page=1&perPage=100&isCompleted=false&order=homeworks.created_at+desc");
            req.Headers.Add("Authorization", $"Bearer {token}");
            req.Headers.Add("Referer", "https://ogrenci.e12.com.tr/homeworks?tab=regular&isCompleted=false");
            req.Headers.TryAddWithoutValidation("User-Agent", "Mozilla/5.0");
            req.Headers.Add("Accept", "application/json, text/plain, */*");
            var resp = await client.SendAsync(req);
            resp.EnsureSuccessStatusCode();
            return await resp.Content.ReadAsStringAsync();
        }
    }
}
