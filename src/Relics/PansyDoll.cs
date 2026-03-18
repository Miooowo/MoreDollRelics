using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 三色堇玩偶：将你每回合打出的第一张牌的一张消耗复制品放入手牌，该复制品本场战斗中费用减少1。
/// </summary>
public sealed class PansyDoll : RelicModel
{
	private const int CostReduceThisCombat = 1;

	private bool _wasUsedThisTurn;
	private CardModel? _cardBeingPlayed;

	public override RelicRarity Rarity => RelicRarity.Event;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
    {
        new EnergyVar(1),
	};

	private bool WasUsedThisTurn
	{
		get => _wasUsedThisTurn;
		set
		{
			AssertMutable();
			_wasUsedThisTurn = value;
		}
	}

	private CardModel? CardBeingPlayed
	{
		get => _cardBeingPlayed;
		set
		{
			AssertMutable();
			_cardBeingPlayed = value;
		}
	}

	public override Task BeforeCardPlayed(CardPlay cardPlay)
	{
		if (CardBeingPlayed != null)
			return Task.CompletedTask;
		if (cardPlay.Card.Owner != Owner)
			return Task.CompletedTask;
		if (WasUsedThisTurn)
			return Task.CompletedTask;
		CardBeingPlayed = cardPlay.Card;
		return Task.CompletedTask;
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (cardPlay.Card != CardBeingPlayed)
			return;
		Flash();
		CardModel copy = cardPlay.Card.CreateClone();
		copy.EnergyCost.AddThisCombat(-CostReduceThisCombat, reduceOnly: true);
		CardCmd.ApplyKeyword(copy, CardKeyword.Exhaust);
		await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Hand, addedByPlayer: true);
		WasUsedThisTurn = true;
		CardBeingPlayed = null;
	}

	public override Task BeforeSideTurnStart(PlayerChoiceContext choiceContext, CombatSide side, CombatState combatState)
	{
		if (side != Owner?.Creature?.Side)
			return Task.CompletedTask;
		WasUsedThisTurn = false;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom _)
	{
		WasUsedThisTurn = false;
		return Task.CompletedTask;
	}
}
