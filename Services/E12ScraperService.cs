using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Playwright;
using OdevTakip2.Models;

namespace OdevTakip2.Services
{
    public class E12ScraperService
    {
        public async Task<List<HomeworkItem>> ScrapeE12HomeworksAsync(string username, string password)
        {
            var results = new List<HomeworkItem>();

            using var playwright = await Playwright.CreateAsync();
            await using var browser = await playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions
            {
                Headless = true // Arka planda çalışabilir
            });

            var page = await browser.NewPageAsync();

            try 
            {
                // Örnek E12 Login adresi
                await page.GotoAsync("https://e12.com.tr/login", new PageGotoOptions { Timeout = 60000 });

                // Login olma (Form elementlerinin ID'leri örnek olarak verilmiştir, E12 sistemine göre revize edilmesi gerekir)
                await page.FillAsync("input[name='username']", username);
                await page.FillAsync("input[name='password']", password);
                
                // Form submit ve yönlendirmeyi bekle (Modern yaklaşım)
                var waitNavigationTask = page.WaitForURLAsync("**/student/homeworks**");
                await page.ClickAsync("button[type='submit']");
                await waitNavigationTask;

                // Ödev div'lerini çekme (Örnek selector)
                var hwRows = await page.QuerySelectorAllAsync("div.homework-item");

                foreach (var row in hwRows)
                {
                    var titleElem = await row.QuerySelectorAsync("h4.title");
                    var textContent = titleElem != null ? await titleElem.InnerTextAsync() : "Detay Yok";

                    results.Add(new HomeworkItem
                    {
                        Source = "E12",
                        Content = textContent,
                        Timestamp = DateTime.Now
                    });
                }
            } 
            catch(Exception ex)
            {
                Console.WriteLine($"E12 Scraping Error: {ex.Message}");
            }
            finally
            {
                await browser.CloseAsync();
            }

            return results;
        }
    }
}
