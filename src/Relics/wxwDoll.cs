using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace MoreDollRelics.src.Relics;

public sealed class wxwDoll : RelicModel
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
		decimal healAmount = 1m - creature.CurrentHp;
		if (healAmount > 0m)
			await CreatureCmd.Heal(creature, healAmount);
		await CreatureCmd.GainBlock(creature, 999m, ValueProp.Unpowered, null);
	}

	public override async Task AfterSideTurnStart(CombatSide side, CombatState combatState)
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
}

