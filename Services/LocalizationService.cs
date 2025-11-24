using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;

namespace OcrApp.Services
{
    public static class LocalizationService
    {
        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static string _language = "zh-CN";
        public static event Action? LanguageChanged;

        public static void SetLanguage(string language)
        {
            if (string.IsNullOrWhiteSpace(language)) language = "zh-CN";
            _language = language;
            LoadLanguageFile(_language);
            LanguageChanged?.Invoke();
        }

        public static string Get(string key)
        {
            if (string.IsNullOrEmpty(key)) return string.Empty;
            if (Strings.TryGetValue(key, out var v)) return v;
            return key;
        }

        private static void LoadLanguageFile(string language)
        {
            try
            {
                string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                string langPath = Path.Combine(baseDir, "Resources", "Languages", language + ".json");
                if (!File.Exists(langPath))
                {
                    langPath = Path.Combine(baseDir, "Resources", "Languages", "zh-CN.json");
                }
                if (File.Exists(langPath))
                {
                    var json = File.ReadAllText(langPath);
                    var dict = JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
                    Strings.Clear();
                    if (dict != null)
                    {
                        foreach (var kv in dict)
                        {
                            Strings[kv.Key] = kv.Value ?? string.Empty;
                        }
                    }
                }
            }
            catch
            {
            }
        }
    }
}