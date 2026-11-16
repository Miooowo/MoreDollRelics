using System;
using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.ValueProps;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 艾冰玩偶：战斗中攻击额外造成最大生命值 5% 的伤害；
/// 战斗结束后有 50% 概率掉落一件食物遗物。
/// </summary>
public sealed class AibingDoll : RelicModel, IDollRelic
{
	private const decimal DamagePercentOfMaxHp = 0.05m;
	private const decimal DropChancePercent = 50m;

	private static readonly Func<RelicModel>[] FoodRelicFactories =
	{
		static () => ModelDb.Relic<Strawberry>(),
		static () => ModelDb.Relic<Pear>(),
		static () => ModelDb.Relic<Mango>(),
		static () => ModelDb.Relic<LeesWaffle>(),
		static () => ModelDb.Relic<NutritiousOyster>(),
		static () => ModelDb.Relic<DragonFruit>(),
		static () => ModelDb.Relic<LoomingFruit>(),
		static () => ModelDb.Relic<ChosenCheese>(),
		static () => ModelDb.Relic<MeatOnTheBone>(),
		static () => ModelDb.Relic<BigMushroom>(),
		static () => ModelDb.Relic<FragrantMushroom>(),
	};

	public override RelicRarity Rarity => RelicRarity.Event;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
	{
		new DynamicVar("DamagePercent", DamagePercentOfMaxHp * 100m),
		new DynamicVar("DropChance", DropChancePercent),
	};

	public override decimal ModifyDamageAdditive(Creature? target, decimal amount, ValueProp props, Creature? dealer, CardModel? cardSource, CardPlay? cardPlay)
	{
		Creature? ownerCreature = Owner?.Creature;
		if (ownerCreature == null || dealer != ownerCreature)
			return 0m;
		// IsPoweredAttack 对模组为 internal；等价：带 Move 且非 Unpowered
		if (!props.HasFlag(ValueProp.Move) || props.HasFlag(ValueProp.Unpowered))
			return 0m;
		return ownerCreature.MaxHp * DamagePercentOfMaxHp;
	}

	public override bool TryModifyRewards(Player player, List<Reward> rewards, AbstractRoom? room)
	{
		if (player != Owner || room is not CombatRoom)
			return false;

		List<RelicModel> candidates = GetAvailableFoodRelics(player);
		if (candidates.Count == 0)
			return false;

		if (!Owner.RunState.Rng.Niche.NextBool())
			return false;

		RelicModel? picked = Owner.RunState.Rng.Niche.NextItem(candidates);
		if (picked == null)
			return false;

		Flash();
		rewards.Add(new RelicReward(picked.ToMutable(), player));
		return true;
	}

	private static List<RelicModel> GetAvailableFoodRelics(Player player)
	{
		var runState = player.RunState;
		var list = new List<RelicModel>(FoodRelicFactories.Length);
		foreach (Func<RelicModel> factory in FoodRelicFactories)
		{
			RelicModel relic = factory();
			if (!relic.IsAllowed(runState))
				continue;
			if (!relic.IsStackable && player.GetRelicById(relic.Id) != null)
				continue;
			list.Add(relic);
		}
		return list;
	}
}
