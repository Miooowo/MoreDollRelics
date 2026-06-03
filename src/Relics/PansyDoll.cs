using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 三色堇玩偶：本场战斗前三个「你的回合」中，每个回合打出的第一张牌生成一张带「消耗」的复制品放入抽牌堆；复制品本场费用减少 1。
/// </summary>
public sealed class PansyDoll : RelicModel, IDollRelic
{
	private const int FirstTurnsWithEffect = 3;
	private const int CostReduceThisCombat = 1;

	private int _playerTurnNumberInCombat;
	private bool _spawnedCopyThisPlayerTurn;
	private CardModel? _firstCardThisTurn;

	public override RelicRarity Rarity => RelicRarity.Event;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
	{
		new EnergyVar(CostReduceThisCombat),
	};

	public override Task AfterPlayerTurnStart(PlayerChoiceContext _, Player player)
	{
		if (Owner == null || player != Owner)
			return Task.CompletedTask;

		_playerTurnNumberInCombat++;
		_spawnedCopyThisPlayerTurn = false;
		_firstCardThisTurn = null;
		return Task.CompletedTask;
	}

	public override Task BeforeCardPlayed(CardPlay cardPlay)
	{
		if (Owner == null || cardPlay.Card.Owner != Owner)
			return Task.CompletedTask;
		if (_playerTurnNumberInCombat < 1 || _playerTurnNumberInCombat > FirstTurnsWithEffect)
			return Task.CompletedTask;
		if (_spawnedCopyThisPlayerTurn || _firstCardThisTurn != null)
			return Task.CompletedTask;

		_firstCardThisTurn = cardPlay.Card;
		return Task.CompletedTask;
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext _, CardPlay cardPlay)
	{
		if (cardPlay.Card != _firstCardThisTurn)
			return;

		_firstCardThisTurn = null;
		if (_spawnedCopyThisPlayerTurn)
			return;

		_spawnedCopyThisPlayerTurn = true;
		Flash();
		CardModel copy = cardPlay.Card.CreateClone();
		copy.EnergyCost.AddThisCombat(-CostReduceThisCombat, reduceOnly: true);
		CardCmd.ApplyKeyword(copy, CardKeyword.Exhaust);
		await CardPileCmd.AddGeneratedCardToCombat(copy, PileType.Draw, Owner);
	}

	public override Task BeforeCombatStart()
	{
		_playerTurnNumberInCombat = 0;
		_spawnedCopyThisPlayerTurn = false;
		_firstCardThisTurn = null;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom _)
	{
		_playerTurnNumberInCombat = 0;
		_spawnedCopyThisPlayerTurn = false;
		_firstCardThisTurn = null;
		return Task.CompletedTask;
	}
}
