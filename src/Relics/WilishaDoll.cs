using System.Collections.Generic;
using System.Linq;
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
using MegaCrit.Sts2.Core.ValueProps;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 薇莉莎玩偶：每打出 10 张攻击牌，对随机敌人造成 40 点伤害并额外扣除 15 点生命。
/// </summary>
public sealed class WilishaDoll : RelicModel, IDollRelic
{
	private const int AttacksPerTrigger = 10;
	private const decimal TriggerDamage = 40m;
	private const decimal HpLoss = 15m;

	private int _attacksPlayedThisCombat;

	public override RelicRarity Rarity => RelicRarity.Event;

	public override bool ShowCounter => CombatManager.Instance.IsInProgress;

	public override int DisplayAmount => _attacksPlayedThisCombat % AttacksPerTrigger;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
	{
		new DynamicVar("Attacks", AttacksPerTrigger),
		new DynamicVar("Damage", TriggerDamage),
		new DynamicVar("HpLoss", HpLoss),
	};

	public override Task BeforeCombatStart()
	{
		_attacksPlayedThisCombat = 0;
		InvokeDisplayAmountChanged();
		return Task.CompletedTask;
	}

	public override async Task AfterCardPlayed(PlayerChoiceContext choiceContext, CardPlay cardPlay)
	{
		if (Owner?.Creature == null || cardPlay.Card.Owner != Owner)
			return;
		if (!CombatManager.Instance.IsInProgress || cardPlay.Card.Type != CardType.Attack)
			return;

		_attacksPlayedThisCombat++;
		InvokeDisplayAmountChanged();
		if (_attacksPlayedThisCombat % AttacksPerTrigger != 0)
			return;

		ICombatState? combatState = Owner.Creature.CombatState;
		if (combatState == null)
			return;

		List<Creature> enemies = combatState.HittableEnemies.Where(e => e.IsAlive).ToList();
		if (enemies.Count == 0)
			return;

		Creature? target = Owner.RunState.Rng.CombatTargets.NextItem(enemies);
		if (target == null)
			return;

		Flash();
		await CreatureCmd.Damage(choiceContext, target, TriggerDamage, ValueProp.Unpowered, Owner.Creature, cardPlay.Card);
		await CreatureCmd.Damage(
			choiceContext,
			target,
			HpLoss,
			ValueProp.Unblockable | ValueProp.Unpowered,
			Owner.Creature,
			null);
	}

	public override Task AfterCombatEnd(CombatRoom _)
	{
		_attacksPlayedThisCombat = 0;
		InvokeDisplayAmountChanged();
		return Task.CompletedTask;
	}
}
