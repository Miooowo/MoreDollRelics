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
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;
using MoreDollRelics.src.Power;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 莱茵玩偶：战斗开始增加10%的攻击伤害。回合开始获得1点能量，能量耗尽时，下回合增加50%的伤害。
/// </summary>
public sealed class RhineDoll : RelicModel
{
	private const decimal BattleStartDamageBonusPercent = 10m;
	private const decimal EnergyPerTurn = 1m;
	private const decimal DepletedBonusPercent = 50m;

	/// <summary>战斗开始起效的 10% 攻击伤害倍率（1.1）。</summary>
	private static readonly decimal BattleDamageMultiplier = 1m + BattleStartDamageBonusPercent / 100m;
	/// <summary>上回合结束时能量为 0 则置为 true，本回合开始时施加 RhinePower（+50% 伤害）。</summary>
	private bool _grantBonusDamageNextTurn;

	public override RelicRarity Rarity => RelicRarity.Event;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new DynamicVar("BattleDamagePercent", BattleStartDamageBonusPercent),
		new EnergyVar((int)EnergyPerTurn),
		new DynamicVar("DepletedPercent", DepletedBonusPercent)
	};

	public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (dealer != Owner?.Creature)
			return 1m;
		// IsPoweredAttack 在游戏 Core 中为 internal 扩展方法，模组无法引用，此处用等价判断：带 Move 且非 Unpowered
		if (!props.HasFlag(ValueProp.Move) || props.HasFlag(ValueProp.Unpowered))
			return 1m;
		return BattleDamageMultiplier;
	}

	public override async Task BeforeCombatStart()
	{
		if (Owner?.Creature == null)
			return;
		Flash();
		// 仅做视觉提示，实际 10% 由 ModifyDamageMultiplicative 提供
	}

	public override async Task AfterPlayerTurnStart(PlayerChoiceContext choiceContext, Player player)
	{
		if (Owner?.Creature == null || player != Owner)
			return;
		Flash();
		await PlayerCmd.GainEnergy(EnergyPerTurn, Owner);
		if (_grantBonusDamageNextTurn)
		{
			_grantBonusDamageNextTurn = false;
			Flash();
			await PowerCmd.Apply<RhinePower>(Owner.Creature, 1m, Owner.Creature, null);
		}
	}

	/// <summary>回合结束前记录本回合是否能量耗尽，用于下回合 +50% 伤害。</summary>
	public override Task BeforeTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (Owner?.Creature == null || side != CombatSide.Player)
			return Task.CompletedTask;
		if (Owner!.PlayerCombatState.Energy <= 0)
			_grantBonusDamageNextTurn = true;
		return Task.CompletedTask;
	}

	public override Task AfterCombatEnd(CombatRoom _)
	{
		_grantBonusDamageNextTurn = false;
		return Task.CompletedTask;
	}
}
