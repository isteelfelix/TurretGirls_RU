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
        private static HashSet<string> _untranslated = new();

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

            MelonLogger.Msg($"[Missing] {original}");

            if (_untranslated.Add(original))
            {
                try
                {
                    File.WriteAllText(UntranslatedFile,
                        JsonConvert.SerializeObject(_untranslated, Formatting.Indented));
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"Не удалось записать untranslated.json: {ex}");
                }
            }

            return original;
        }
    }
}
