using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 狗王玩偶：你的回合开始时，恢复1生命值并获得2点格挡。
/// 在你的回合内受到一次不低于生命值上限20%的伤害时，下回合开始获得1层无实体。
/// </summary>
public sealed class DogkingDoll : RelicModel
{
	private const decimal HealPerTurn = 1m;
	private const decimal BlockPerTurn = 2m;
	private const decimal BigDamageThresholdPercent = 0.2m;

	private bool _grantIntangibleNextTurn;

	public override RelicRarity Rarity => RelicRarity.Event;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
	{
		new DynamicVar("Heal", HealPerTurn),
		new DynamicVar("Block", BlockPerTurn),
		new DynamicVar("ThresholdPercent", BigDamageThresholdPercent * 100m)
	};

	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (Owner?.Creature == null || player != Owner)
			return;
		var creature = Owner.Creature;
		Flash();
		await CreatureCmd.Heal(creature, HealPerTurn);
		await CreatureCmd.GainBlock(creature, BlockPerTurn, ValueProp.Unpowered, null);

		if (_grantIntangibleNextTurn)
		{
			_grantIntangibleNextTurn = false;
			Flash();
			await PowerCmd.Apply<IntangiblePower>(creature, 1m, creature, null);
		}
	}

	public override Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != Owner?.Creature || result.UnblockedDamage <= 0)
			return Task.CompletedTask;
		var combatState = Owner.Creature.CombatState;
		if (combatState == null || combatState.CurrentSide != CombatSide.Player)
			return Task.CompletedTask;
		decimal maxHp = Owner.Creature.MaxHp;
		if (maxHp <= 0)
			return Task.CompletedTask;
		decimal threshold = maxHp * BigDamageThresholdPercent;
		if ((decimal)result.UnblockedDamage >= threshold)
			_grantIntangibleNextTurn = true;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom _)
	{
		_grantIntangibleNextTurn = false;
		return Task.CompletedTask;
	}
}
