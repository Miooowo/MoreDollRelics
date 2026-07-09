using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MoreDollRelics.src.Relics;

namespace MoreDollRelics.src.Patches;

/// <summary>
/// 将「玩偶的邀请函」加入湼奥先古遗物随机候选（含正向奖励池与骨骰候选池）。
/// </summary>
[HarmonyPatch]
internal static class DollInvitationLetterNeowPatch
{
	private const string NeowDonePositivePage = "NEOW.pages.DONE.POSITIVE.description";

	private static readonly MethodInfo RelicOptionMethod = AccessTools.Method(
		typeof(AncientEventModel),
		"RelicOption",
		new[] { typeof(RelicModel), typeof(string), typeof(string) }
	);

	[HarmonyPatch(typeof(Neow), "get_AllPossibleOptions")]
	[HarmonyPostfix]
	private static void AllPossibleOptionsPostfix(Neow __instance, ref IEnumerable<EventOption> __result)
	{
		var options = (__result as IReadOnlyList<EventOption>)?.ToList() ?? __result.ToList();
		if (ContainsInvitationOption(options))
			return;

		EventOption? invitation = BuildInvitationOption(__instance);
		if (invitation?.Relic == null)
			return;

		options.Add(invitation);
		__result = options;
	}

	[HarmonyPatch(typeof(Neow), "get_PositiveOptions")]
	[HarmonyPostfix]
	private static void PositiveOptionsPostfix(Neow __instance, ref IEnumerable<EventOption> __result)
	{
		var options = (__result as IReadOnlyList<EventOption>)?.ToList() ?? __result.ToList();
		if (ContainsInvitationOption(options))
			return;

		EventOption? invitation = BuildInvitationOption(__instance);
		if (invitation?.Relic == null)
			return;

		// 仅加入正向候选池，由原版随机流程决定是否进入前两个选项。
		options.Add(invitation);
		__result = options;
	}

	private static bool ContainsInvitationOption(IEnumerable<EventOption> options)
	{
		ModelId invitationId = ModelDb.Relic<DollInvitationLetter>().Id;
		foreach (EventOption option in options)
		{
			if (option.Relic?.Id == invitationId)
				return true;
		}

		return false;
	}

	private static EventOption? BuildInvitationOption(Neow neow)
	{
		object? raw = RelicOptionMethod.Invoke(
			neow,
			new object?[] { ModelDb.Relic<DollInvitationLetter>().ToMutable(), "INITIAL", NeowDonePositivePage }
		);
		return raw as EventOption;
	}
}
