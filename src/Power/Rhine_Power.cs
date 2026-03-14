using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Powers;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.ValueProps;

namespace MoreDollRelics.src.Power;

/// <summary>
/// 莱茵玩偶施加的能力：本回合你造成的伤害增加50%。回合结束时移除。
/// </summary>
public sealed class RhinePower : PowerModel
{
	private const decimal DamageMultiplier = 1.5m;

	public override PowerType Type => PowerType.Buff;

	public override PowerStackType StackType => PowerStackType.Single;

	public override decimal ModifyDamageMultiplicative(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (dealer != Owner)
			return 1m;
		if (!props.HasFlag(ValueProp.Move) || props.HasFlag(ValueProp.Unpowered))
			return 1m;
		return DamageMultiplier;
	}

	public override async Task AfterTurnEnd(PlayerChoiceContext choiceContext, CombatSide side)
	{
		if (side == base.Owner.Side)
			await PowerCmd.Remove(this);
	}
}
