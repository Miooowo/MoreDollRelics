using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.DevConsole;
using MegaCrit.Sts2.Core.DevConsole.ConsoleCommands;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Models;
using MoreDollRelics.src.Relics;

namespace MoreDollRelics.src.Patches;

/// <summary>
/// 控制台 <c>relic add doll_all</c>：获得原版玩偶室三件套 + 所有带 <see cref="IDollRelic"/>（doll 标签）的模组遗物。
/// </summary>
[HarmonyPatch(typeof(RelicConsoleCmd), nameof(RelicConsoleCmd.Process))]
internal static class RelicConsoleDollAllPatch
{
	private const string DollAllArg = "DOLL_ALL";

	private static readonly RelicModel[] VanillaDollRelics =
	{
		ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.DaughterOfTheWind>(),
		ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.MrStruggles>(),
		ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.BingBong>(),
	};

	[HarmonyPrefix]
	private static bool Prefix(Player? issuingPlayer, string[] args, ref CmdResult __result)
	{
		if (!TryGetTargetRelicId(args, out string? relicId) || relicId == null)
			return true;

		if (!relicId.Equals(DollAllArg, StringComparison.OrdinalIgnoreCase))
			return true;

		bool isRemove = args.Length >= 1
			&& args[0].Equals("remove", StringComparison.OrdinalIgnoreCase);
		if (isRemove)
		{
			__result = new CmdResult(success: false, "doll_all 仅支持 add，请逐个 remove 遗物 id。");
			return false;
		}

		if (issuingPlayer == null)
		{
			__result = new CmdResult(success: false, "A run is currently not in progress!");
			return false;
		}

		int added = 0;
		int skipped = 0;

		foreach (RelicModel canonical in EnumerateDollAllCanonicals())
		{
			if (PlayerHasRelic(issuingPlayer, canonical.Id))
			{
				skipped++;
				continue;
			}

			TaskHelper.RunSafely(RelicCmd.Obtain(canonical.ToMutable(), issuingPlayer));
			added++;
		}

		__result = new CmdResult(
			success: true,
			$"doll_all：新增 {added} 个遗物（原版玩偶 + 模组 {DollRelicTag.Doll} 标签）；已拥有跳过 {skipped}。");
		return false;
	}

	private static IEnumerable<RelicModel> EnumerateDollAllCanonicals()
	{
		foreach (RelicModel v in VanillaDollRelics)
			yield return v;

		foreach (RelicModel r in ModelDb.AllRelics
			         .Where(static x => x is IDollRelic)
			         .OrderBy(static x => x.Id.Entry, StringComparer.OrdinalIgnoreCase))
			yield return r;
	}

	private static bool PlayerHasRelic(Player player, ModelId relicId) =>
		player.Relics.Any(r => r.Id == relicId);

	private static bool TryGetTargetRelicId(string[] args, out string? relicId)
	{
		relicId = null;
		if (args.Length < 1)
			return false;

		if (args[0].Equals("add", StringComparison.OrdinalIgnoreCase)
		    || args[0].Equals("remove", StringComparison.OrdinalIgnoreCase))
		{
			if (args.Length < 2)
				return false;
			relicId = args[1];
			return true;
		}

		relicId = args[0];
		return true;
	}
}
