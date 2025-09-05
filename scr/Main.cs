using System.Reflection;
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
