using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.CardPools;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 月夜零玩偶：第 4 回合开始时对全体敌人造成伤害；自第 4 回合起每回合开始将一张 canonical 3 费非 X 能力牌置入手牌；每打出 3 张能力牌额外自动打出一次。
/// </summary>
public sealed class IZeroDoll : RelicModel, IDollRelic
{
	private const int GiftPowerCanonicalEnergy = 3;
	private const int PowersPerBonusPlay = 3;
	private const int NovaTurn = 4;
	private const decimal NovaDamage = 15m;

	private int _powersPlayedThisCombat;
	private int _playerTurnNumberInCombat;
	private bool _suppressPowerCounting;

	public override RelicRarity Rarity => RelicRarity.Event;

	public override bool ShowCounter => CombatManager.Instance.IsInProgress;

	/// <summary>本场已打出能力牌数对 <see cref="PowersPerBonusPlay"/> 取模；每满 3 触发回声后显示为 0。</summary>
	public override int DisplayAmount => _powersPlayedThisCombat % PowersPerBonusPlay;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
	{
		new EnergyVar(1),
		new DynamicVar("PowersPerEcho", PowersPerBonusPlay),
		new DynamicVar("NovaTurn", NovaTurn),
		new DynamicVar("NovaDamage", NovaDamage),
	};

	public override Task BeforeCombatStart()
	{
		_powersPlayedThisCombat = 0;
		_playerTurnNumberInCombat = 0;
		_suppressPowerCounting = false;
		RefreshEchoCounter();
		return Task.CompletedTask;
	}

	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (Owner?.Creature == null || player != Owner)
			return;

		_playerTurnNumberInCombat++;
		if (_playerTurnNumberInCombat == NovaTurn)
		{
			Flash();
			await DealNovaToAllEnemies(choiceContext);
		}

		if (_playerTurnNumberInCombat >= NovaTurn)
			await TryAddRandomColoredPowerToHand();
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext context, CardPlay cardPlay)
	{
		if (Owner?.Creature == null || cardPlay.Card.Owner != Owner)
			return;
		if (cardPlay.Card.Type != CardType.Power)
			return;
		if (_suppressPowerCounting)
			return;

		_powersPlayedThisCombat++;
		RefreshEchoCounter();
		if (_powersPlayedThisCombat % PowersPerBonusPlay != 0)
			return;

		_suppressPowerCounting = true;
		try
		{
			Flash();
			CardModel? blueprint = ModelDb.GetById<CardModel>(cardPlay.Card.Id);
			if (blueprint is not { } bp)
				return;
			// 须用 CombatState.CreateCard：RunState.CreateCard 只进 Run 的牌表，不进本场 CombatState，弃牌时会抛错
			ICombatState? combat = Owner.Creature.CombatState;
			if (combat == null)
				return;
			CardModel echo = combat.CreateCard(bp, Owner);
			await CardCmd.AutoPlay(context, echo, null, AutoPlayType.Default);
		}
		finally
		{
			_suppressPowerCounting = false;
		}
	}

	public override Task AfterCombatEnd(CombatRoom _)
	{
		_powersPlayedThisCombat = 0;
		_playerTurnNumberInCombat = 0;
		_suppressPowerCounting = false;
		RefreshEchoCounter();
		return Task.CompletedTask;
	}

	private void RefreshEchoCounter()
	{
		InvokeDisplayAmountChanged();
	}

	private async Task TryAddRandomColoredPowerToHand()
	{
		if (Owner == null || Owner.Creature?.CombatState == null)
			return;

		// 全部角色颜色池 + 无色池，均匀权重后只保留 canonical 费用为 3 的非 X 费「能力」牌（不改写费用，与描述一致）
		List<CardPoolModel> poolModels = ModelDb.AllCharacterCardPools
			.Append(ModelDb.CardPool<ColorlessCardPool>())
			.Distinct()
			.ToList();

		var options = CardCreationOptions.ForNonCombatWithUniformOdds(
			poolModels,
			static c => c.Type == CardType.Power
				&& !c.EnergyCost.CostsX
				&& c.EnergyCost.Canonical == GiftPowerCanonicalEnergy);

		List<CardModel> pool = options.GetPossibleCards(Owner).ToList();
		if (pool.Count == 0)
			return;

		Rng rng = Owner.RunState?.Rng?.Niche ?? Rng.Chaotic;
		CardModel? picked = rng.NextItem(pool);
		if (picked == null)
			return;
		ICombatState combat = Owner.Creature.CombatState;
		CardModel instance = combat.CreateCard(picked, Owner);
		Flash();
		await CardPileCmd.AddGeneratedCardToCombat(instance, PileType.Hand, Owner);
	}

	private async Task DealNovaToAllEnemies(PlayerChoiceContext choiceContext)
	{
		ICombatState? cs = Owner?.Creature?.CombatState;
		if (cs == null)
			return;

		List<Creature> enemies = cs.HittableEnemies.Where(e => e != null && e.IsAlive).ToList();
		if (enemies.Count == 0)
			return;

		if (Owner?.Creature == null)
			return;
		await CreatureCmd.Damage(choiceContext, enemies, NovaDamage, ValueProp.Unpowered, Owner.Creature, null);
	}
}
