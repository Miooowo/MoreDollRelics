using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens.RelicCollection;
using MoreDollRelics.src.Relics;

namespace MoreDollRelics.src.Patches;

/// <summary>
/// 将「玩偶的邀请函」挂到图鉴中的涅奥先古遗物子分类里显示。
/// 仅影响图鉴展示，不改任何遗物池掉落。
/// </summary>
[HarmonyPatch(typeof(NRelicCollectionCategory), "LoadRelics")]
internal static class NeowAncientCollectionPatch
{
	private static readonly FieldInfo? SubCategoriesField =
		AccessTools.Field(typeof(NRelicCollectionCategory), "_subCategories");
	private static readonly FieldInfo? RelicsContainerField =
		AccessTools.Field(typeof(NRelicCollectionCategory), "_relicsContainer");
	private static readonly FieldInfo? HeaderLabelField =
		AccessTools.Field(typeof(NRelicCollectionCategory), "_headerLabel");
	private static readonly MethodInfo? LoadRelicNodesMethod =
		AccessTools.Method(typeof(NRelicCollectionCategory), "LoadRelicNodes");

	[HarmonyPostfix]
	private static void Postfix(
		NRelicCollectionCategory __instance,
		RelicRarity relicRarity,
		NRelicCollection collection,
		HashSet<RelicModel> seenRelics,
		HashSet<RelicModel> allUnlockedRelics)
	{
		if (relicRarity != RelicRarity.Ancient)
			return;

		try
		{
			RelicModel invitation = ModelDb.Relic<DollInvitationLetter>();
			if (collection.Relics.Any(r => r.Id == invitation.Id))
				return;

			AncientEventModel? neowAncient = ModelDb.AllSharedAncients.FirstOrDefault(a => a is Neow) ?? ModelDb.Event<Neow>();
			if (neowAncient == null)
				return;

			if (SubCategoriesField?.GetValue(__instance) is not IEnumerable<NRelicCollectionCategory> subCategories)
				return;

			NRelicCollectionCategory? neowCategory = FindNeowCategory(subCategories, neowAncient);
			if (neowCategory == null)
				return;

			if (RelicsContainerField?.GetValue(neowCategory) is not Godot.GridContainer relicsContainer)
				return;

			List<RelicModel> relics = relicsContainer
				.GetChildren()
				.OfType<NRelicCollectionEntry>()
				.Select(e => e.relic)
				.Where(r => r != null)
				.ToList();

			if (relics.Any(r => r.Id == invitation.Id))
				return;

			relics.Add(invitation);
			relics = relics
				.OrderBy(r => r.Title.GetFormattedText(), MegaCrit.Sts2.Core.Localization.LocManager.Instance.StringComparer)
				.ToList();

			LoadRelicNodesMethod?.Invoke(neowCategory, new object[] { relics, seenRelics, allUnlockedRelics });
			collection.AddRelics(new[] { invitation });
		}
		catch (Exception ex)
		{
			Log.Warn($"[MoreDollRelics] Failed to inject DollInvitationLetter into Neow ancient collection: {ex}");
		}
	}

	private static NRelicCollectionCategory? FindNeowCategory(IEnumerable<NRelicCollectionCategory> subCategories, AncientEventModel neowAncient)
	{
		string neowTitle = neowAncient.Title.GetFormattedText();
		var byHeader = new List<NRelicCollectionCategory>();
		var byIcon = new List<NRelicCollectionCategory>();

		foreach (NRelicCollectionCategory sub in subCategories)
		{
			if (HeaderLabelField?.GetValue(sub) is MegaCrit.Sts2.addons.mega_text.MegaRichTextLabel headerLabel)
			{
				string headerText = headerLabel.Text ?? string.Empty;
				if (headerText.Contains(neowTitle, StringComparison.OrdinalIgnoreCase))
					byHeader.Add(sub);
			}

			if (AccessTools.Field(typeof(NRelicCollectionCategory), "_icon")?.GetValue(sub) is Godot.TextureRect icon
			    && icon.Texture == neowAncient.RunHistoryIcon)
				byIcon.Add(sub);
		}

		return byHeader.FirstOrDefault() ?? byIcon.FirstOrDefault();
	}
}
