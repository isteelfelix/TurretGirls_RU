using System;
using System.Reflection;
using System.Linq;
using HarmonyLib;
using UnityEngine;
using System.Threading;
using System.Threading.Tasks;
using MelonLoader;

namespace TurretGirlsRus
{
    // В игре у Scriptable_String обычно есть метод GetText(int langId)
    // Тип может быть в сборке игры и не присутствовать во время компиляции —
    // разрешаем метод в рантайме через AccessTools.TypeByName.
    public static class ScriptableString_GetText_Patch
    {
        // Postfix вызывается после целевого метода. Делать его лёгким и безопасным.
        public static void Postfix(ref string __result)
        {
            if (string.IsNullOrEmpty(__result)) return;
            MelonLogger.Msg($"[Hook:GetText] {__result}");
            var translated = TranslationManager.Translate(__result);
            if (!string.IsNullOrEmpty(translated)) __result = translated;
        }
    }

    // Postfix для get_strings -> Il2CppStringArray
    public static class ScriptableString_GetStrings_Patch
    {
        public static void Postfix(ref object __result)
        {
            try
            {
                if (__result == null) return;
                var arr = __result as Array;
                if (arr == null) return;

                for (int i = 0; i < arr.Length; i++)
                {
                    try
                    {
                        var val = arr.GetValue(i) as string;
                        if (string.IsNullOrEmpty(val)) continue;
                        var translated = TranslationManager.Translate(val);
                        if (!string.IsNullOrEmpty(translated) && translated != val)
                        {
                            arr.SetValue(translated, i);
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"ScriptableString_GetStrings_Patch postfix exception: {ex.Message}");
            }
        }
    }

    public static class TextHooks
    {
        public static void Hook()
        {
            var harmony = new HarmonyLib.Harmony("com.turretgirls.rus");

            // Запускаем отложенные попытки найти и залатать метод, чтобы дать Il2Cpp генерации время
            Task.Run(() =>
            {
                const int maxAttempts = 30;
                const int delayMs = 1000; // 1s
                MethodInfo method = null;
                for (int i = 0; i < maxAttempts; i++)
                {
                    MelonLogger.Msg($"TextHooks: попытка {i + 1}/{maxAttempts} поиска метода GetText...");
                    try
                    {
                        method = FindGetTextMethod();
                    }
                    catch { method = null; }
                    if (method != null) break;
                        // Попытка фоллбека на UI-сеттеры заранее (после нескольких попыток поиска GetText),
                        // но делаем это агрессивно: сначала одиночная попытка, затем фоновые повторные попытки
                        // (0s, 1s, 3s, 10s) чтобы поймать случаи поздней инициализации типов.
                        if (i == 4)
                        {
                            try
                            {
                                MelonLogger.Msg("TextHooks: промежуточная попытка фоллбек-патчинга UI-сеттеров (ранняя)");
                                // Сначала быстрый синхронный попытка — если нашли, можем закончить
                                if (TryPatchUISetters(harmony))
                                {
                                    MelonLogger.Msg("TextHooks: фоллбек-патчинг UI-сеттеров выполнен (ранний). ");
                                    return;
                                }

                                // Не найдено сразу — запустим фоновые ретраи с увеличивающейся задержкой
                                Task.Run(() => TryPatchUISettersWithBackgroundRetries(harmony));
                            }
                            catch (Exception ex)
                            {
                                MelonLogger.Msg($"TextHooks: ранний фоллбек-патчинг UI упал: {ex.Message}");
                            }
                        }
                    Thread.Sleep(delayMs);
                }

                if (method == null)
                {
                    MelonLogger.Warning("TextHooks: не найден тип/method Scriptable_String.GetText после нескольких попыток — пробую фоллбек-патчинг UI-сеттеров...");
                    if (TryPatchUISetters(harmony))
                    {
                        MelonLogger.Msg("TextHooks: фоллбек-патчинг UI-сеттеров выполнен.");
                        return;
                    }

                    MelonLogger.Warning("TextHooks: фоллбек-патчинг не нашёл подходящих UI методов — патч пропущен.");
                    return;
                }

                // Выбираем Postfix в зависимости от возвращаемого типа метода: если массив строк -> используем Postfix для массивов,
                // иначе — для одиночной строки.
                try
                {
                    MethodInfo chosenPostfix = null;
                    var ret = method.ReturnType;
                    if (ret != null && (ret.IsArray || (ret.Name != null && ret.Name.IndexOf("Il2CppStringArray", StringComparison.OrdinalIgnoreCase) >= 0)))
                    {
                        chosenPostfix = typeof(ScriptableString_GetStrings_Patch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        MelonLogger.Msg($"TextHooks: выбрана Postfix (array) для метода {method.DeclaringType?.FullName}.{method.Name} -> {ret.FullName}");
                    }
                    else
                    {
                        chosenPostfix = typeof(ScriptableString_GetText_Patch).GetMethod("Postfix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
                        MelonLogger.Msg($"TextHooks: выбрана Postfix (string) для метода {method.DeclaringType?.FullName}.{method.Name} -> {ret?.FullName}");
                    }

                    if (chosenPostfix != null)
                    {
                        harmony.Patch(method, postfix: new HarmonyMethod(chosenPostfix));
                        MelonLogger.Msg($"TextHooks активированы (patched {method.DeclaringType?.FullName}.{method.Name}).");
                    }
                    else
                    {
                        MelonLogger.Warning("TextHooks: не удалось найти подходящий Postfix метод для патча.");
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"TextHooks: ошибка при применении Postfix патча: {ex.Message}");
                }

                // Даже если мы запатчили Scriptable_String, он может не использоваться в UI.
                // Попробуем также фоллбек-патчинг UI-сеттеров (не блокируя основной поток).
                try
                {
                    Task.Run(() => {
                        try
                        {
                            if (TryPatchUISetters(harmony))
                                MelonLogger.Msg("TextHooks: фоллбек-патчинг UI-сеттеров выполнен (после патча Scriptable_String).");
                            else
                                MelonLogger.Msg("TextHooks: фоллбек-патчинг UI-сеттеров не нашёл подходящих методов (после патча Scriptable_String).");
                        }
                        catch (Exception ex)
                        {
                            MelonLogger.Msg($"TextHooks: фоллбек-патчинг UI упал (после патча Scriptable_String): {ex.Message}");
                        }
                    });
                }
                catch { }
            });
        }

        // Патч для UI-сеттеров: Prefix меняет первый строковый аргумент на перевод
        public static class UISetText_Patch
        {
            public static void Prefix(ref object __0)
            {
                try
                {
                    if (__0 == null) return;
                    string orig = __0 as string;
                    if (string.IsNullOrEmpty(orig))
                    {
                        try { orig = __0.ToString(); } catch { orig = null; }
                    }
                    if (string.IsNullOrEmpty(orig)) return;
                    MelonLogger.Msg($"[UIHook:Prefix] original='" + orig + "'");
                    var translated = TranslationManager.Translate(orig);
                    if (!string.IsNullOrEmpty(translated) && translated != orig)
                    {
                        // Попробуем заменить аргумент; если оригинал был string, присвоим string;
                        // если это Il2CppString-like объект, простая запись строки часто конвертируется Il2CppInterop'ом.
                        try
                        {
                            __0 = translated;
                            MelonLogger.Msg($"[UIHook:Prefix] translated='" + translated + "'");
                        }
                        catch (Exception exAssign)
                        {
                            MelonLogger.Warning($"UISetText_Patch assignment failed: {exAssign.Message}");
                        }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Warning($"UISetText_Patch Prefix exception: {ex.Message}");
                }
            }
        }

        private static bool TryPatchUISetters(HarmonyLib.Harmony harmony)
        {
            // Список потенциальных типов UI/ТМРО
            string[] uiTypeNames = new[] {
                "Il2CppTMPro.TextMeshProUGUI",
                "TMPro.TextMeshProUGUI",
                "Il2CppTMPro.TMP_Text",
                "TMPro.TMP_Text",
                "TMPro.TextMeshPro",
                "UnityEngine.UI.Text",
                "TextMeshProUGUI",
                "TextMeshPro",
                "TMP_Text",
                "UnityEngine.UI"
            };

            foreach (var tn in uiTypeNames)
            {
                try
                {
                    Type t = null;
                    try { t = AccessTools.TypeByName(tn); } catch { t = null; }
                    if (t == null)
                    {
                        // безопаснее попытаться найти тип через Assembly.GetType, без вызова GetTypes()
                        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                        foreach (var asm in assemblies)
                        {
                            try
                            {
                                var tt = asm.GetType(tn, false, false);
                                if (tt != null) { t = tt; break; }
                            }
                            catch { }
                        }
                    }
                    MelonLogger.Msg($"TextHooks: TryPatchUISetters: TypeByName('{tn}') => {(t != null ? t.FullName : "null")} ");
                    // Если AccessTools не нашёл тип — попробуем безопасно проверить сборки через Assembly.GetType
                    if (t == null)
                    {
                        var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                        foreach (var asm in assemblies)
                        {
                            try
                            {
                                var tt = asm.GetType(tn, false, false);
                                if (tt != null)
                                {
                                    t = tt;
                                    MelonLogger.Msg($"TextHooks: TryPatchUISetters: найден тип {tn} через Assembly.GetType в {asm.GetName().Name} -> {t.FullName}");
                                    break;
                                }
                            }
                            catch { }
                        }

                        // Если строгое совпадение не найдено — не делаем полный перебор типов в сборках
                        // (он часто вызывает ReflectionTypeLoadException). Для устойчивости остаёмся
                        // на AccessTools.TypeByName / Assembly.GetType и избегаем asm.GetTypes().
                    }
                    if (t == null) continue;

                    // Попробуем найти SetText(string) или сеттер свойства text (set_text)
                    var candidates = new[] { "SetText", "set_text" };
                    foreach (var cname in candidates)
                    {
                        MethodInfo m = null;
                        try {
                            // попробуем найти метод с сигнатурой (string) напрямую через рефлекшн
                            m = t.GetMethod(cname, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static, null, new Type[] { typeof(string) }, null);
                        } catch { m = null; }
                        if (m != null)
                        {
                            var prefix = new HarmonyMethod(typeof(UISetText_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
                            harmony.Patch(m, prefix: prefix);
                            MelonLogger.Msg($"TextHooks: запатчен UI метод {t.FullName}.{m.Name}");
                            return true;
                        }

                        // Если точная сигнатура не найдена, попробуем найти любой метод с этим именем,
                        // у которого первый параметр похож на строковый тип (включая Il2CppString)
                        try
                        {
                            var declared = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                            foreach (var dm in declared)
                            {
                                try
                                {
                                    if (!dm.Name.Equals(cname, StringComparison.OrdinalIgnoreCase)) continue;
                                    var ps = dm.GetParameters();
                                    if (ps.Length >= 1 && IsStringLike(ps[0].ParameterType))
                                    {
                                        var prefix = new HarmonyMethod(typeof(UISetText_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
                                        harmony.Patch(dm, prefix: prefix);
                                        MelonLogger.Msg($"TextHooks: запатчен UI метод (by-name string-like param) {t.FullName}.{dm.Name}");
                                        return true;
                                    }
                                }
                                catch { }
                            }
                        }
                        catch { }
                    }

                    // Если не нашли точной сигнатуры, попробуем найти любой метод принимающий string первым аргументом
            var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
            foreach (var mm in methods)
                    {
                        try
                        {
                            var ps = mm.GetParameters();
                if (ps.Length >= 1 && IsStringLike(ps[0].ParameterType))
                            {
                                var prefix = new HarmonyMethod(typeof(UISetText_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
                                harmony.Patch(mm, prefix: prefix);
                                MelonLogger.Msg($"TextHooks: запатчен UI метод (fallback) {t.FullName}.{mm.Name}");
                                return true;
                            }
                        }
                        catch { }
                    }
                }
                catch (Exception ex)
                {
                    MelonLogger.Msg($"TextHooks: TryPatchUISetters ошибка для {tn}: {ex.Message}");
                }
            }

            // Дополнительный агрессивный патч для TMP типов: патчим все перегрузки SetText / set_text у TMP_Text и TextMeshProUGUI
            try
            {
                var tmpTypes = new[] { "Il2CppTMPro.TMP_Text", "TMPro.TMP_Text", "Il2CppTMPro.TextMeshProUGUI", "TMPro.TextMeshProUGUI" };
                foreach (var tname in tmpTypes)
                {
                    try
                    {
                                Type tt = null;
                                try { tt = AccessTools.TypeByName(tname); } catch { tt = null; }
                                if (tt == null) continue;
                                var declared = tt.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                                foreach (var dm in declared)
                        {
                            try
                            {
                                if (!(dm.Name.Equals("SetText", StringComparison.OrdinalIgnoreCase) || dm.Name.Equals("set_text", StringComparison.OrdinalIgnoreCase))) continue;
                                var ps = dm.GetParameters();
                                if (ps.Length >= 1 && IsStringLike(ps[0].ParameterType))
                                {
                                    var prefix = new HarmonyMethod(typeof(UISetText_Patch).GetMethod("Prefix", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic));
                                    harmony.Patch(dm, prefix: prefix);
                                    MelonLogger.Msg($"TextHooks: агрессивно запатчен {tt.FullName}.{dm.Name} (declared overload)");
                                    // не возвращаем — пытаемся запатчить все подходящие перегрузки
                                }
                            }
                            catch { }
                        }
                    }
                    catch { }
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"TextHooks: ошибка агрессивного TMP-патчинга: {ex.Message}");
            }

            // no automatic GameObject registration — runtime scanner runs from MelonMod.OnUpdate
            return false;
        }

        private static MethodInfo FindGetTextMethod()
        {
            // Попробуем несколько вариантов имени типа
            string[] tryNames = new[] { "Il2Cpp.Scriptable_String", "Scriptable_String" };

            // Поиск по явным именам
            foreach (var name in tryNames)
            {
                var t = AccessTools.TypeByName(name);
                MelonLogger.Msg($"TextHooks: AccessTools.TypeByName('{name}') => {(t != null ? t.FullName : "null")}");
                if (t != null)
                {
                    var m = FindGetTextOverload(t);
                    MelonLogger.Msg($"TextHooks: поиск методов в {t.FullName} завершён, найден: {(m != null ? m.ToString() : "null")} ");
                    if (m != null) return m;
                }
            }

            // Пропускаем жёсткое получение всех типов через Assembly.GetTypes() — вместо этого
            // используем более безопасный Assembly.GetType ниже (ниже реализовано), чтобы не триггерить
            // ReflectionTypeLoadException при наличии несовместимых UserLibs.
                // Если не нашли по явным именам — проверим наиболее вероятные сборки, но без вызова GetTypes()
                // (GetTypes() триггерит ReflectionTypeLoadException при UserLibs mismatch). Вместо этого
                // попробуем получить тип по имени через Assembly.GetType(name, false), что не пытается загрузить все типы.
                var assemblies = AppDomain.CurrentDomain.GetAssemblies();
                string[] preferred = new[] { "Assembly-CSharp", "Il2Cpp__Generated", "GameAssembly", "Assembly-CSharp-firstpass", "Il2Cpp" };
                foreach (var asm in assemblies.Where(a => preferred.Any(p => a.GetName().Name.IndexOf(p, StringComparison.OrdinalIgnoreCase) >= 0)))
                {
                    var asmName = asm.GetName().Name;
                    MelonLogger.Msg($"TextHooks: проверяю сборку {asmName} методом Assembly.GetType (без GetTypes)");
                    try
                    {
                        // Проверяем несколько вариантов полного имени
                        var candidates = new[] { "Scriptable_String", "Il2Cpp.Scriptable_String", asmName + ".Scriptable_String" };
                        foreach (var candidate in candidates)
                        {
                            Type tt = null;
                            try { tt = asm.GetType(candidate, false, false); } catch { tt = null; }
                            if (tt != null)
                            {
                                MelonLogger.Msg($"TextHooks: найден тип {candidate} через Assembly.GetType в {asmName} -> {tt.FullName}");
                                var m = FindGetTextOverload(tt);
                                MelonLogger.Msg($"TextHooks: метод найден: {(m != null ? m.ToString() : "null")} ");
                                if (m != null) return m;
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"TextHooks: не удалось проверить сборку {asmName}: {ex.Message}");
                    }
                }

            // Фоллбек: как крайняя мера пройдём по всем сборкам, но ограничим время поиска
            MelonLogger.Msg("TextHooks: не найден Scriptable_String в приоритетных сборках; фоллбек отключён для уменьшения шума.");

            return null;
        }

        private static MethodInfo FindGetTextOverload(Type t)
        {
            try
            {
                // Соберём все объявленные методы и фильтруем по возвращаемому типу string
                var methods = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static).Cast<MethodInfo>().ToList();
                MelonLogger.Msg($"TextHooks: {t.FullName} объявлено методов: {methods.Count}");
                // Выведем подписи первых методов для диагностики
                try
                {
                    int cap = Math.Min(50, methods.Count);
                    for (int mi = 0; mi < cap; mi++)
                    {
                        var mm = methods[mi];
                        var ps = mm.GetParameters();
                        var pstr = string.Join(", ", ps.Select(p => p.ParameterType.Name + " " + p.Name));
                        MelonLogger.Msg($"TextHooks: method[{mi}] {mm.Name}({pstr}) -> {mm.ReturnType.Name}");
                    }
                }
                catch (Exception exDump)
                {
                    MelonLogger.Msg($"TextHooks: ошибка при дампе методов: {exDump.Message}");
                }

                var stringReturning = methods.Where(m => m.ReturnType == typeof(string)).ToList();
                MelonLogger.Msg($"TextHooks: методов возвращающих string: {stringReturning.Count}");

                // Ищем методы возвращающие массив строк (например Il2CppStringArray или обычный string[])
                var arrayReturning = methods.Where(m => m.ReturnType.IsArray || (m.ReturnType.Name != null && m.ReturnType.Name.IndexOf("Il2CppStringArray", StringComparison.OrdinalIgnoreCase) >= 0)).ToList();
                MelonLogger.Msg($"TextHooks: методов возвращающих string array: {arrayReturning.Count}");

                // Предпочитаем get_strings -> array
                var arrm = arrayReturning.FirstOrDefault(mi => mi.Name.IndexOf("get_strings", StringComparison.OrdinalIgnoreCase) >= 0 || mi.Name.IndexOf("GetStrings", StringComparison.OrdinalIgnoreCase) >= 0);
                if (arrm != null)
                {
                    MelonLogger.Msg($"TextHooks: выбрана перегрузка (string array) {arrm}");
                    return arrm;
                }

                // 1) Предпочитаем GetText(int) -> string
                var m = stringReturning.FirstOrDefault(mi => mi.Name.Equals("GetText", StringComparison.OrdinalIgnoreCase)
                    && mi.GetParameters().Length == 1
                    && mi.GetParameters()[0].ParameterType == typeof(int));
                if (m != null)
                {
                    MelonLogger.Msg($"TextHooks: выбрана перегрузка {m}");
                    return m;
                }

                // 2) Затем GetText() -> string
                m = stringReturning.FirstOrDefault(mi => mi.Name.Equals("GetText", StringComparison.OrdinalIgnoreCase)
                    && mi.GetParameters().Length == 0);
                if (m != null)
                {
                    MelonLogger.Msg($"TextHooks: выбрана перегрузка {m}");
                    return m;
                }

                // 3) Свойства: геттеры, возвращающие string (например property Text)
                try
                {
                    var props = t.GetProperties(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                    foreach (var p in props)
                    {
                        var getter = p.GetGetMethod(true);
                        if (getter != null && getter.ReturnType == typeof(string))
                        {
                            MelonLogger.Msg($"TextHooks: выбран геттер свойства {p.Name} -> {getter}");
                            return getter;
                        }
                    }
                }
                catch (Exception exProps)
                {
                    MelonLogger.Msg($"TextHooks: ошибка при переборе свойств {t.FullName}: {exProps.Message}");
                }

                // 4) Любой метод, возвращающий string с параметром int или без параметров
                m = stringReturning.FirstOrDefault(mi => mi.GetParameters().Length == 1 && mi.GetParameters()[0].ParameterType == typeof(int));
                if (m != null)
                {
                    MelonLogger.Msg($"TextHooks: выбран метод (ret string, int) {m}");
                    return m;
                }

                m = stringReturning.FirstOrDefault(mi => mi.GetParameters().Length == 0);
                if (m != null)
                {
                    MelonLogger.Msg($"TextHooks: выбран метод (ret string, no params) {m}");
                    return m;
                }

                // 5) Любой первый метод возвращающий string
                if (stringReturning.Count > 0)
                {
                    MelonLogger.Msg($"TextHooks: выбран любой метод возвращающий string: {stringReturning[0]}");
                    return stringReturning[0];
                }

                // 6) Если есть методы возвращающие массив строк — вернём первый из них в качестве fallback
                if (arrayReturning.Count > 0)
                {
                    MelonLogger.Msg($"TextHooks: выбран любой метод возвращающий string array: {arrayReturning[0]}");
                    return arrayReturning[0];
                }
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"TextHooks: FindGetTextOverload исключение: {ex.Message}");
            }

            return null;
        }

        // Фоновые повторы для UI-патча: попытки с задержками 0s,1s,3s,10s
        private static async void TryPatchUISettersWithBackgroundRetries(HarmonyLib.Harmony harmony)
        {
            try
            {
                int[] delays = new[] { 0, 1000, 3000, 10000 };
                foreach (var d in delays)
                {
                    if (d > 0) await System.Threading.Tasks.Task.Delay(d);
                    try
                    {
                        MelonLogger.Msg($"TextHooks: background retry for UI setters (delay {d}ms)");
                        if (TryPatchUISetters(harmony))
                        {
                            MelonLogger.Msg($"TextHooks: фоллбек-патчинг UI-сеттеров выполнен (background, delay {d}ms).");
                            return;
                        }
                    }
                    catch (Exception ex)
                    {
                        MelonLogger.Msg($"TextHooks: background TryPatchUISetters failed (delay {d}ms): {ex.Message}");
                    }
                }

                MelonLogger.Msg("TextHooks: background UI patch retries завершены, методов не найдено.");
            }
            catch (Exception ex)
            {
                MelonLogger.Msg($"TextHooks: TryPatchUISettersWithBackgroundRetries исключение: {ex.Message}");
            }
        }

        private static bool IsStringLike(Type t)
        {
            if (t == null) return false;
            if (t == typeof(string)) return true;
            // Il2Cpp string wrappers often have names like Il2CppString or Il2CppStringArray element types
            var name = t.Name ?? string.Empty;
            if (name.IndexOf("Il2CppString", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (name.IndexOf("String", StringComparison.OrdinalIgnoreCase) >= 0 && !t.IsArray) return true;
            return false;
        }
    }

// Статический рантайм-сканер, вызываемый из MelonMod.OnUpdate (main-thread)
public static class TextHooksRuntimeTranslator
{
    private static int frameCounter = 0;
    private static int framesBetweenScans = 30; // примерно каждые 0.5 секунды при 60fps

    public static void UpdateTick()
    {
        frameCounter++;
        if (frameCounter < framesBetweenScans) return;
        frameCounter = 0;

        try
        {
            Array allObjs = null;
            try
            {
                var findMethod = typeof(UnityEngine.Object).GetMethod("FindObjectsOfType", new Type[] { typeof(Type) });
                if (findMethod != null)
                {
                    var res = findMethod.Invoke(null, new object[] { typeof(UnityEngine.Object) });
                    allObjs = res as Array;
                }
            }
            catch { allObjs = null; }
            if (allObjs == null) return;

            foreach (var o in allObjs)
            {
                var comp = o as UnityEngine.Component;
                if (comp == null) continue;
                var tt = comp.GetType();
                var tn = tt.FullName ?? tt.Name;
                if (tn.IndexOf("TextMeshProUGUI", StringComparison.OrdinalIgnoreCase) < 0 && tn.IndexOf("TMP_Text", StringComparison.OrdinalIgnoreCase) < 0 && tn.IndexOf("UnityEngine.UI.Text", StringComparison.OrdinalIgnoreCase) < 0) continue;

                var textProp = tt.GetProperty("text", BindingFlags.Public | BindingFlags.Instance | BindingFlags.NonPublic);
                if (textProp == null) continue;

                try
                {
                    var cur = textProp.GetValue(comp, null) as string;
                    if (string.IsNullOrEmpty(cur)) continue;
                    var translated = TranslationManager.Translate(cur);
                    if (!string.IsNullOrEmpty(translated) && translated != cur)
                    {
                        textProp.SetValue(comp, translated, null);
                        MelonLoader.MelonLogger.Msg($"[RuntimeTranslator] translated '{cur}' -> '{translated}' on {tn}");
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}

}
