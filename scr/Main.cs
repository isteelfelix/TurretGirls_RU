using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using MelonLoader;

[assembly: MelonInfo(typeof(TurretGirlsRus.TurretGirlsRusMod), "TurretGirls RU", "1.0.0", "Chieftain51")]
[assembly: MelonGame("NanairoEnterprise", "TurretGirls")]

namespace TurretGirlsRus
{
    public class TurretGirlsRusMod : MelonMod
    {
        public override void OnInitializeMelon()
        {
            MelonLogger.Msg("Turret Girls Rus v1.0.0 by Chieftain51");

            // Явно инициализируем сборщик непереведённых строк
            // typeof(...) alone не вызывает статический конструктор, поэтому используем RuntimeHelpers
            try
            {
                RuntimeHelpers.RunClassConstructor(typeof(UntranslatedCollector).TypeHandle);
            }
            catch (Exception ex)
            {
                MelonLogger.Warning($"Failed to force-run UntranslatedCollector ctor: {ex}");
            }

            TranslationManager.LoadTranslations();
            TextHooks.Hook();
        }

        public override void OnUpdate()
        {
            // Run the runtime translator tick on main thread
            try { TextHooksRuntimeTranslator.UpdateTick(); } catch { }
        }
    }
}
