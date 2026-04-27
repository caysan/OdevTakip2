using System;
using System.Collections.Generic;

namespace OdevTakip2.Models
{
    public class HomeworkItem
    {
        public string Source { get; set; } = string.Empty;
        public string InstitutionType { get; set; } = string.Empty;
        public string Content { get; set; } = string.Empty;
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public List<string> MediaFiles { get; set; } = new List<string>();
        public string Sender { get; set; } = string.Empty;
        public DateTime? DueDate { get; set; }
    }
}
