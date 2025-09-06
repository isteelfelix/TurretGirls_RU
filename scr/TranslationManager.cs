using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using MelonLoader;

namespace TurretGirlsRus
{
    public static class TranslationManager
    {
    private static Dictionary<string, string> _translations = new();
    // Optional external collector can subscribe to this to receive missing keys.
    // Example collector (UntranslatedCollector.cs) subscribes to capture missing strings
    // during development. In client builds you can comment out or remove that file.
    public static Action<string, string> MissingCallback;

        private static readonly string ModDir =
            Path.Combine(AppContext.BaseDirectory, "Mods", "TurretGirls_RU");

        private static readonly string TranslationFile =
            Path.Combine(ModDir, "ru.json");

        private static readonly string UntranslatedFile =
            Path.Combine(ModDir, "untranslated.json");

        public static void LoadTranslations()
        {
            try
            {
                if (File.Exists(TranslationFile))
                {
                    string json = File.ReadAllText(TranslationFile);
                    _translations = JsonConvert.DeserializeObject<Dictionary<string, string>>(json)
                                    ?? new Dictionary<string, string>();
                    MelonLogger.Msg($"Загружено переводов: {_translations.Count}");
                }
                else
                {
                    MelonLogger.Warning($"Файл перевода не найден: {TranslationFile}");
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"Ошибка загрузки перевода: {ex}");
            }
        }

        public static string Translate(string original)
        {
            if (string.IsNullOrEmpty(original))
                return original;

            var key = original.Replace("\r", "").Replace("\n", "").Trim();
            if (_translations.TryGetValue(key, out var translated))
                return translated;

            // Notify optional external collector about missing key. Collector is
            // implemented separately (e.g. UntranslatedCollector.cs) and can be
            // enabled/disabled in development builds. By default we do not write
            // files from in-game code to keep client builds clean.
            try
            {
                MissingCallback?.Invoke(key, original);
            }
            catch { }

            return original;
        }
    }
}
