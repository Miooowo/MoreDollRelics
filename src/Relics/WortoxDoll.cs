using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 沃拓克斯玩偶：拾起时最大生命 +10；击杀敌方生物获得灵魂，最多 20。
/// 超出的灵魂自动掉落，为你与盟友各回复 5 点生命。
/// 右键每次消耗 1 个灵魂，为你与盟友各回复 10 点生命；灵魂为 0 时不治疗。
/// </summary>
public sealed class WortoxDoll : RelicModel, IDollRelic
{
	public const int MaxSouls = 20;
	private const decimal MaxHpOnPickup = 10m;
	private const decimal OverflowHeal = 5m;
	private const decimal ReleaseHeal = 10m;

	private int _souls;

	public override RelicRarity Rarity => RelicRarity.Event;

	public override bool ShowCounter => true;

	public override int DisplayAmount => _souls;

	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new MaxHpVar(MaxHpOnPickup),
		new DynamicVar("MaxSouls", MaxSouls),
		new DynamicVar("OverflowHeal", OverflowHeal),
		new DynamicVar("ReleaseHeal", ReleaseHeal),
	};

	[SavedProperty]
	public int Souls
	{
		get => _souls;
		set
		{
			AssertMutable();
			int clamped = value < 0 ? 0 : value > MaxSouls ? MaxSouls : value;
			if (_souls == clamped)
				return;
			_souls = clamped;
			InvokeDisplayAmountChanged();
		}
	}

	public override async Task AfterObtained()
	{
		if (Owner?.Creature == null)
			return;
		Flash();
		await CreatureCmd.GainMaxHp(Owner.Creature, MaxHpOnPickup);
	}

	public override async Task AfterDeath(PlayerChoiceContext choiceContext, Creature target, CardModel? source, bool wasRemovalPrevented, float deathAnimLength)
	{
		Creature? ownerCreature = Owner?.Creature;
		if (ownerCreature == null || target.Side == ownerCreature.Side)
			return;

		if (Souls < MaxSouls)
		{
			Flash();
			Souls++;
			return;
		}

		Flash();
		await HealYouAndAllies(OverflowHeal);
	}

	/// <summary>
	/// 右键顶栏遗物时调用。每次消耗 1 个灵魂并治疗；灵魂为 0 时直接返回，不治疗。
	/// </summary>
	public async Task ReleaseSouls()
	{
		if (Souls <= 0)
			return;

		Souls--;
		Flash();
		await HealYouAndAllies(ReleaseHeal);
	}

	/// <summary>
	/// 治疗本局在场的你与盟友玩家。不含宠物。
	/// </summary>
	private async Task HealYouAndAllies(decimal amount)
	{
		if (Owner == null || amount <= 0m)
			return;

		var healed = new HashSet<Creature>();
		foreach (Player player in Owner.RunState.Players)
		{
			Creature? creature = player?.Creature;
			if (creature == null || !creature.IsAlive || !healed.Add(creature))
				continue;
			await CreatureCmd.Heal(creature, amount);
		}

		Creature? ownerCreature = Owner.Creature;
		if (ownerCreature != null && ownerCreature.IsAlive && healed.Add(ownerCreature))
			await CreatureCmd.Heal(ownerCreature, amount);
	}
}
