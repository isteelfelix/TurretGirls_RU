// UntranslatedCollector.cs
// Optional development-time collector for missing translation keys.
// This file is intended to be included only in development builds. To disable
// collection in client builds, either remove this file from compilation or
// comment out the registration in the static constructor below.

using System;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using MelonLoader;

namespace TurretGirlsRus
{
    public static class UntranslatedCollector
    {
    private static readonly string ModDir = Path.Combine(AppContext.BaseDirectory, "Mods", "TurretGirls_RU");
    // СБОР: runtime flag file name to enable collection. If this file is absent, collection is disabled.
    // This single file may optionally contain an absolute export path on the first line.
    // Create the file 'collect_untranslated.enabled' in the mod folder to enable collection.
    // If the file contains a path (e.g. D:\Proj\Turret Girls RU\translations\tmp) that path will be used as the export directory.
    private const string CollectFlagFileName = "collect_untranslated.enabled"; // СБОР
    // (deprecated) fallback export path filename
    private const string ExportPathFileName = "collect_untranslated.exportpath";

    private static string UntranslatedFile = Path.Combine(ModDir, "untranslated.json");
    private static HashSet<string> _collected = new HashSet<string>();

        // Static constructor registers the collector with TranslationManager.
        // To disable collection for client builds, comment out the body of this ctor.
        static UntranslatedCollector()
        {
            try
            {
                MelonLogger.Msg("UntranslatedCollector: static ctor called");
                // СБОР: only enable collection when the flag file exists in the mod folder.
                var flagPath = Path.Combine(ModDir, CollectFlagFileName);
                MelonLogger.Msg($"UntranslatedCollector: checking flag file at {flagPath}");
                if (!File.Exists(flagPath))
                {
                    MelonLogger.Msg("UntranslatedCollector: collection disabled (flag file not found)");
                    return; // do not register
                }

                // If the enable file contains a path on first line, use it as export path.
                try
                {
                    var content = File.ReadAllText(flagPath).Trim();
                    if (!string.IsNullOrEmpty(content))
                    {
                        MelonLogger.Msg($"UntranslatedCollector: found path in {CollectFlagFileName}: {content}");
                        try
                        {
                            Directory.CreateDirectory(content);
                            UntranslatedFile = Path.Combine(content, "untranslated.json");
                            MelonLogger.Msg($"UntranslatedCollector: will write to {UntranslatedFile} (from enabled file content)");
                        }
                        catch (Exception ex) { MelonLogger.Warning($"UntranslatedCollector: failed to use path from enabled file: {ex}"); }
                    }
                    else
                    {
                        MelonLogger.Msg($"UntranslatedCollector: {CollectFlagFileName} present but empty; will attempt legacy exportpath fallback or use mod folder");
                        // fallback to deprecated exportpath file
                        var exportFile = Path.Combine(ModDir, ExportPathFileName);
                        MelonLogger.Msg($"UntranslatedCollector: checking exportpath file at {exportFile}");
                        if (File.Exists(exportFile))
                        {
                            var target = File.ReadAllText(exportFile).Trim();
                            MelonLogger.Msg($"UntranslatedCollector: exportpath file found, target={target}");
                            if (!string.IsNullOrEmpty(target))
                            {
                                try
                                {
                                    Directory.CreateDirectory(target);
                                    UntranslatedFile = Path.Combine(target, "untranslated.json");
                                    MelonLogger.Msg($"UntranslatedCollector: will write to {UntranslatedFile} (from exportpath file)");
                                }
                                catch (Exception ex) { MelonLogger.Warning($"UntranslatedCollector: failed to set export path from exportpath file: {ex}"); }
                            }
                        }
                    }
                }
                catch (Exception ex) { MelonLogger.Warning($"UntranslatedCollector: exportpath/enabled file error: {ex}"); }

                // Register callback
                TranslationManager.MissingCallback += OnMissing;
                MelonLogger.Msg("UntranslatedCollector: registered MissingCallback");

                // Try to load existing file to continue collecting without duplicates
                if (File.Exists(UntranslatedFile))
                {
                    try
                    {
                        var json = File.ReadAllText(UntranslatedFile);
                        var existing = JsonConvert.DeserializeObject<HashSet<string>>(json);
                        if (existing != null)
                            _collected = existing;
                        MelonLogger.Msg($"UntranslatedCollector: loaded {existing?.Count ?? 0} existing keys");
                    }
                    catch (Exception ex) { MelonLogger.Warning($"UntranslatedCollector: failed to load existing file: {ex}"); }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"UntranslatedCollector: failed to initialize: {ex}");
            }
        }

        private static void OnMissing(string key, string original)
        {
            try
            {
                MelonLogger.Msg($"UntranslatedCollector: OnMissing called with key='{key}', original='{original}'");
                if (string.IsNullOrEmpty(key)) return;
                if (_collected.Add(key))
                {
                    MelonLogger.Msg($"UntranslatedCollector: new untranslated key detected: {key}");
                    // Append and flush on every new key to avoid losing data on crash
                    try
                    {
                        Directory.CreateDirectory(Path.GetDirectoryName(UntranslatedFile));
                        File.WriteAllText(UntranslatedFile, JsonConvert.SerializeObject(_collected, Formatting.Indented));
                        MelonLogger.Msg($"UntranslatedCollector: collected new key: {key} and wrote to {UntranslatedFile}");
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Warning($"UntranslatedCollector: write failed: {ex}");
                    }
                }
            }
            catch (Exception ex) { MelonLogger.Warning($"UntranslatedCollector: OnMissing error: {ex}"); }
        }
    }
}
