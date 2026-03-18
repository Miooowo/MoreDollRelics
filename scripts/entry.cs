using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Models.RelicPools;
using MoreDollRelics.src.Relics;

namespace MoreDollRelics.scripts;

[ModInitializer("Init")]
public class Entry
{
	public const string ModId = "MoreDollRelics";
	private static Harmony? _harmony;

	public static void Init()
	{
		_harmony = new Harmony(ModId);
		_harmony.PatchAll();
		Log.Debug("Mod initialized!");
		ModHelper.AddModelToPool<SharedRelicPool, VistaDoll>();
		ModHelper.AddModelToPool<SharedRelicPool, wxwDoll>();
		ModHelper.AddModelToPool<SharedRelicPool, BaizealerDoll>();
		ModHelper.AddModelToPool<SharedRelicPool, GallopDoll>();
		ModHelper.AddModelToPool<SharedRelicPool, DogkingDoll>();
		ModHelper.AddModelToPool<SharedRelicPool, RhineDoll>();
		ModHelper.AddModelToPool<SharedRelicPool, PansyDoll>();
	}
}
