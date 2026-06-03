using System.Collections.Generic;
using MegaCrit.Sts2.Core.Models;

namespace MoreDollRelics.src.Patches;

/// <summary>
/// 玩偶室单次事件内，模组玩偶随机展示历史：同一遗物已连续出现 2 次则第 3 次不再入选（跨选项/刷新生效）。
/// </summary>
internal static class DollRoomModDollRollTracker
{
	private static readonly List<string> History = new();

	public static void Reset() => History.Clear();

	public static bool IsBlockedThirdConsecutive(RelicModel relic)
	{
		string id = relic.Id.Entry;
		int count = History.Count;
		return count >= 2 && History[count - 1] == id && History[count - 2] == id;
	}

	public static void Record(RelicModel relic) => History.Add(relic.Id.Entry);
}
