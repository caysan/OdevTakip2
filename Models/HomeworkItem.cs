using System;
using System.Collections.Generic;

namespace OdevTakip2.Models
{
    public class HomeworkItem
    {
        public string Source { get; set; } = string.Empty; // "WhatsApp", "E12"
        public string Content { get; set; } = string.Empty; // Mesaj veya Duyuru İçeriği
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public List<string> MediaFiles { get; set; } = new List<string>(); // İndirilen eklerin yerel dosya yolları
        
        // Metadata or extra info like sender name etc
        public string Sender { get; set; } = string.Empty;
    }
}
