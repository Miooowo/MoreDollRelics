using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Relics;
using MoreDollRelics.src.Relics;

namespace MoreDollRelics.src.Patches;

/// <summary>
/// 顶栏遗物没有右键钩子。右键按下时，若是沃拓克斯玩偶且灵魂大于 0，则消耗 1 个灵魂。
/// </summary>
[HarmonyPatch(typeof(NClickableControl), "_GuiInput")]
internal static class WortoxDollRightClickPatch
{
	private static void Postfix(NClickableControl __instance, InputEvent inputEvent)
	{
		if (__instance is not NRelicInventoryHolder holder || !holder.IsEnabled)
			return;
		if (inputEvent is not InputEventMouseButton mouse || !mouse.Pressed || mouse.ButtonIndex != MouseButton.Right)
			return;
		if (holder.Relic?.Model is not WortoxDoll doll)
			return;

		_ = doll.ReleaseSouls();
	}
}
