using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace MoreDollRelics.src.Relics;

public sealed class wxwDoll : RelicModel, IDollRelic
{
	private bool _wasUsed;
	private bool _triggeredThisCombat;

	public override RelicRarity Rarity => RelicRarity.Event;

	public override bool IsUsedUp => _wasUsed;

	protected override IEnumerable<DynamicVar> CanonicalVars
	{
		get
		{
			yield return new BlockVar(99m, ValueProp.Unpowered);
		}
	}

	[SavedProperty]
	public bool WasUsed
	{
		get => _wasUsed;
		set
		{
			AssertMutable();
			_wasUsed = value;
			if (IsUsedUp)
                Status = RelicStatus.Disabled;
		}
	}

	public override bool ShouldDieLate(Creature creature)
	{
		if (creature != Owner.Creature)
			return true;
		if (WasUsed)
			return true;
		return false;
	}

	public override async Task AfterPreventingDeath(Creature creature)
	{
		Flash();
		WasUsed = true;
		_triggeredThisCombat = true;
		decimal healAmount = 7m - creature.CurrentHp;
		if (healAmount > 0m)
			await CreatureCmd.Heal(creature, healAmount);
		await CreatureCmd.GainBlock(creature, 999m, ValueProp.Unpowered, null);
		await AutoPlayAllCardsFromAllPiles();
	}

	public override async Task AfterSideTurnStart(CombatSide side, IReadOnlyList<Creature> participants, ICombatState combatState)
	{
		if (side != Owner.Creature.Side || !_triggeredThisCombat)
			return;
		Flash();
		await CreatureCmd.GainBlock(Owner.Creature, 99m, ValueProp.Unpowered, null);
	}

	public override Task AfterCombatEnd(CombatRoom _)
	{
		_triggeredThisCombat = false;
		return Task.CompletedTask;
	}

	private async Task AutoPlayAllCardsFromAllPiles()
	{
		if (Owner?.Creature?.CombatState is not ICombatState combatState)
			return;

		var context = new ThrowingPlayerChoiceContext();
		var snapshot = new List<CardModel>();
		foreach (PileType pileType in new[] { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust })
			snapshot.AddRange(GetCardsFromPile(combatState, pileType));

		foreach (CardModel card in snapshot.Distinct().ToList())
		{
			if (card.Owner != Owner)
				continue;
			try
			{
				await CardCmd.AutoPlay(context, card, null, AutoPlayType.Default);
			}
			catch
			{
				// 单张卡无法自动打出时跳过，不阻塞遗物核心保命流程。
			}
		}
	}

	private static IEnumerable<CardModel> GetCardsFromPile(ICombatState combatState, PileType pileType)
	{
		MethodInfo? getPileMethod = combatState.GetType().GetMethod("GetPile", BindingFlags.Instance | BindingFlags.Public);
		object? pile = getPileMethod?.Invoke(combatState, new object[] { pileType });
		if (pile == null)
			return Array.Empty<CardModel>();

		PropertyInfo? cardsProperty = pile.GetType().GetProperty("Cards", BindingFlags.Instance | BindingFlags.Public);
		if (cardsProperty?.GetValue(pile) is not System.Collections.IEnumerable cards)
			return Array.Empty<CardModel>();

		var result = new List<CardModel>();
		foreach (object? item in cards)
		{
			if (item is CardModel card)
				result.Add(card);
		}
		return result;
	}
}

