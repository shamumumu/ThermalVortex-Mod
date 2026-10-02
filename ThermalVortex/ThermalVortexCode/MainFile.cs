using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Modding;
using ThermalVortex.ThermalVortexCode.Diagnostics;
using ThermalVortex.ThermalVortexCode.CardInspection;
using ThermalVortex.ThermalVortexCode.MonsterField;
using ThermalVortex.ThermalVortexCode.RewardPools;
using ThermalVortex.ThermalVortexCode.Vfx;

namespace ThermalVortex.ThermalVortexCode;

[ModInitializer(nameof(Initialize))]
public partial class MainFile : Node
{
    public const string ModId = "ThermalVortex"; //Used for resource filepath
    public const string ResPath = $"res://{ModId}";

    public static MegaCrit.Sts2.Core.Logging.Logger Logger { get; } = new(ModId, MegaCrit.Sts2.Core.Logging.LogType.Generic);

    public static void Initialize()
    {
        //If you want to use scripts defined in your mod for Godot scenes, uncomment the following line.
        //Godot.Bridge.ScriptManagerBridge.LookupScriptsInAssembly(Assembly.GetExecutingAssembly());

        MonsterFieldPile.Register();
        try
        {
            RewardPoolSetupService.Initialize();
        }
        catch (Exception ex)
        {
            // A config I/O problem should disable "use last build", not the mod.
            Logger.Info($"Reward-pool config initialization failed error={ex}");
        }

        var harmony = new Harmony(ModId);

        PatchAllSafely(harmony);
        CardInspectionInputRouter.Install();
        TaskProbeHost.Initialize();
        AshBlossomVfx.StartPreload();
    }

    private static void PatchAllSafely(Harmony harmony)
    {
        var patched = 0;
        var failed = 0;
        foreach (var type in GetHarmonyPatchTypes())
        {
            try
            {
                harmony.CreateClassProcessor(type).Patch();
                patched++;
            }
            catch (Exception ex)
            {
                failed++;
                Logger.Info($"ThermalVortex Harmony patch failed type={type.FullName} error={ex}");
            }
        }

        Logger.Info($"ThermalVortex Harmony patch summary patched={patched} patchIssues={failed}");
    }

    private static IEnumerable<Type> GetHarmonyPatchTypes()
    {
        return GetAssemblyTypesSafely()
            .Where(type => type.GetCustomAttributes(typeof(HarmonyPatch), false).Length > 0)
            .OrderBy(type => type.FullName, StringComparer.Ordinal);
    }

    private static IEnumerable<Type> GetAssemblyTypesSafely()
    {
        try
        {
            return typeof(MainFile).Assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            Logger.Info($"ThermalVortex Harmony patch type scan incomplete error={ex}");
            return ex.Types.Where(type => type is not null).Cast<Type>();
        }
    }
}
