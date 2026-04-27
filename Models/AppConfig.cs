using System.Collections.Generic;

namespace OdevTakip2.Models
{
    public class AppConfig
    {
        public List<WhatsAppGroupConfig> WhatsAppGroupList { get; set; } = new();
        public string ApiKey { get; set; } = string.Empty;
        public List<E12AccountConfig> E12AccountList { get; set; } = new();
        public List<ScheduleConfig> ScheduleList { get; set; } = new();
        public List<string>           InstitutionTypes { get; set; } = new() { "Okul", "Dershane" };
        public List<AIProviderConfig> AIProviders      { get; set; } = new();

        public System.DateTime? LastWhatsAppScan { get; set; }
        public System.DateTime? LastE12Scan { get; set; }
        public System.DateTime? LastAIGenerate { get; set; }
    }
}
