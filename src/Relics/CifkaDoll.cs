using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MoreDollRelics.src.Power;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 西夫卡玩偶：拾起时移除牌组全部诅咒（含永恒）且之后不再获得诅咒；
/// 战斗开始时使所有敌人本回合失去 10 力量与 10 敏捷；
/// 每打出 5 张攻击牌给予所有敌人 2 层虚弱；
/// 第三幕必定遭遇一次审判事件。
/// </summary>
public sealed class CifkaDoll : RelicModel, IDollRelic
{
	private const int GloryActIndex = 2;
	private const int AttacksPerWeak = 5;
	private const decimal StrDexLoss = 10m;
	private const decimal WeakAmount = 2m;

	private int _attacksPlayedThisCombat;

	public override RelicRarity Rarity => RelicRarity.Event;

	public override bool ShowCounter => CombatManager.Instance.IsInProgress;

	public override int DisplayAmount => _attacksPlayedThisCombat % AttacksPerWeak;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
	{
		new DynamicVar("StrLoss", StrDexLoss),
		new DynamicVar("DexLoss", StrDexLoss),
		new DynamicVar("Attacks", AttacksPerWeak),
		new DynamicVar("Weak", WeakAmount),
	};

	protected override IEnumerable<IHoverTip> ExtraHoverTips => new[]
	{
		HoverTipFactory.FromPower<WeakPower>(),
		HoverTipFactory.FromPower<StrengthPower>(),
		HoverTipFactory.FromPower<DexterityPower>(),
	};

	public override async Task AfterObtained()
	{
		if (Owner == null)
			return;

		List<CardModel> curses = Owner.Deck.Cards.Where(c => c.Type == CardType.Curse).ToList();
		if (curses.Count == 0)
			return;

		Flash();
		await CardPileCmd.RemoveFromDeck(curses);
	}

	public override async Task AfterCardChangedPiles(CardModel card, PileType oldPileType, AbstractModel? source)
	{
		if (Owner == null || card.Owner != Owner || card.Type != CardType.Curse)
			return;
		if (card.Pile?.Type != PileType.Deck)
			return;

		Flash();
		await CardPileCmd.RemoveFromDeck(card);
	}

	public override async Task BeforeCombatStart()
	{
		if (Owner?.Creature == null)
			return;

		_attacksPlayedThisCombat = 0;
		InvokeDisplayAmountChanged();

		ICombatState? combatState = Owner.Creature.CombatState;
		if (combatState == null)
			return;

		List<Creature> enemies = combatState.HittableEnemies.Where(e => e.IsAlive).ToList();
		if (enemies.Count == 0)
			return;

		Flash();
		var ctx = new ThrowingPlayerChoiceContext();
		await PowerCmd.Apply<CifkaStrDownPower>(ctx, enemies, StrDexLoss, Owner.Creature, null);
		await PowerCmd.Apply<CifkaDexDownPower>(ctx, enemies, StrDexLoss, Owner.Creature, null);
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (Owner?.Creature == null || cardPlay.Card.Owner != Owner)
			return;
		if (!CombatManager.Instance.IsInProgress || cardPlay.Card.Type != CardType.Attack)
			return;

		_attacksPlayedThisCombat++;
		InvokeDisplayAmountChanged();
		if (_attacksPlayedThisCombat % AttacksPerWeak != 0)
			return;

		ICombatState? combatState = Owner.Creature.CombatState;
		if (combatState == null)
			return;

		List<Creature> enemies = combatState.HittableEnemies.Where(e => e.IsAlive).ToList();
		if (enemies.Count == 0)
			return;

		Flash();
		await PowerCmd.Apply<WeakPower>(choiceContext, enemies, WeakAmount, Owner.Creature, cardPlay.Card);
	}

	public override EventModel ModifyNextEvent(EventModel currentEvent)
	{
		if (Owner?.RunState is not RunState runState || runState.CurrentActIndex != GloryActIndex)
			return currentEvent;

		EventModel trial = ModelDb.Event<Trial>();
		if (runState.VisitedEventIds.Contains(trial.Id))
			return currentEvent;

		return trial;
	}

	public override Task AfterCombatEnd(CombatRoom _)
	{
		_attacksPlayedThisCombat = 0;
		InvokeDisplayAmountChanged();
		return Task.CompletedTask;
	}
}
