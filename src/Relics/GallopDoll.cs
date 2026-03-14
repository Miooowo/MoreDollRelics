using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 加洛普玩偶：战斗开始时每损失20%生命获得1点力量；战斗中每损失5点生命回复1点生命；战斗结束时回复3点生命。
/// </summary>
public sealed class GallopDoll : RelicModel
{
	private const decimal HpLossPercentPerStrength = 0.2m;  // 每20%已损失生命获得1力量
	private const int HpLostPerHealInCombat = 5;             // 战中每损失5点生命回复1点
	private const decimal HealAtCombatEnd = 3m;

	public override RelicRarity Rarity => RelicRarity.Event;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
	{
		new DynamicVar("HpLossPercent", HpLossPercentPerStrength * 100m),
		new DynamicVar("HpPerHeal", HpLostPerHealInCombat),
		new DynamicVar("HealEnd", HealAtCombatEnd)
	};

	public override async Task BeforeCombatStart()
	{
		if (Owner?.Creature == null)
			return;
		var creature = Owner.Creature;
		decimal maxHp = creature.MaxHp;
		if (maxHp <= 0)
			return;
		decimal lost = maxHp - creature.CurrentHp;
		if (lost <= 0)
			return;
		decimal threshold = maxHp * HpLossPercentPerStrength;
		int strengthGain = threshold > 0 ? (int)(lost / threshold) : 0;
		if (strengthGain <= 0)
			return;
		Flash();
		await PowerCmd.Apply<StrengthPower>(creature, (decimal)strengthGain, creature, null);
	}

	public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != Owner?.Creature || result.UnblockedDamage <= 0)
			return;
		int healAmount = result.UnblockedDamage / HpLostPerHealInCombat;
		if (healAmount <= 0)
			return;
		Flash();
		await CreatureCmd.Heal(Owner.Creature, (decimal)healAmount);
	}

	public override async Task AfterCombatEnd(CombatRoom _)
	{
		if (Owner?.Creature == null || Owner.Creature.IsDead)
			return;
		Flash();
		await CreatureCmd.Heal(Owner.Creature, HealAtCombatEnd);
	}
}
