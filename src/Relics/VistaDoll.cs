using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 薇斯塔玩偶：商店折扣30%，进入商店时每20金币回复1点生命值，战胜敌人获得的金币翻倍。
/// 传送设定：拥有时第一次进入商店生效，离开后直到第二次进入商店前都完全失效；第二次进入时重新激活并执行回血效果，如此循环。
/// </summary>
public sealed class VistaDoll : RelicModel
{
	private const string _discountKey = "Discount";
	private const decimal DiscountPercent = 30m;
	private const int GoldPerHealTick = 20;
	private const decimal HealPerTick = 1m;
	private const int CombatGoldMultiplier = 2;

	public override RelicRarity Rarity => RelicRarity.Event;

	// 进入过的商店数量（第 1、2、3、4…家）
	[SavedProperty]
	public int MerchantIndex { get; set; }

	// 当前是否生效
	[SavedProperty]
	public bool IsActive { get; set; } = true;

	// 上一个房间是否为商店，用来在离开商店后一格房间调整状态
	[SavedProperty]
	public bool WasInMerchant { get; set; }

	protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new DynamicVar("Discount", DiscountPercent) };

	public override decimal ModifyMerchantPrice(Player player, MerchantEntry entry, decimal originalPrice)
	{
		if (player != Owner)
			return originalPrice;
		if (!IsActive)
			return originalPrice;
		return originalPrice * (1m - DynamicVars["Discount"].BaseValue / 100m);
	}

	public override bool ShouldRefillMerchantEntry(MerchantEntry entry, Player player)
	{
		return player == Owner && IsActive;
	}

	public override async Task AfterRoomEntered(AbstractRoom room)
	{
		if (Owner?.Creature == null || Owner.Creature.IsDead)
			return;

		if (room is MerchantRoom)
		{
			// 进入任意商店：薇斯塔必定“回到身上”并立刻生效
			MerchantIndex++;
			IsActive = true;
			Status = RelicStatus.Normal;
			WasInMerchant = true;

			int gold = Owner.Gold;
			decimal healAmount = gold / GoldPerHealTick * HealPerTick;
			if (healAmount > 0m)
			{
				Flash();
				await CreatureCmd.Heal(Owner.Creature, healAmount);
			}
			return;
		}

		// 离开任意商店后的第一个房间，根据商店次数决定是保持生效还是失效：
		// - 离开第 1、3、5... 家商店后：失效
		// - 离开第 2、4、6... 家商店后：保持生效
		if (WasInMerchant)
		{
			if (MerchantIndex % 2 == 1)
			{
				IsActive = false;
				Status = RelicStatus.Disabled;
			}
			else
			{
				IsActive = true;
				Status = RelicStatus.Normal;
			}
			WasInMerchant = false;
		}
	}

	public override bool TryModifyRewardsLate(Player player, List<Reward> rewards, AbstractRoom? room)
	{
		if (player != Owner)
			return false;
		if (room is not CombatRoom)
			return false;
		if (!IsActive)
			return false;

		var newRewards = new List<Reward>();
		foreach (Reward reward in rewards)
		{
			if (reward is GoldReward goldReward)
				newRewards.Add(new GoldReward(goldReward.Amount * CombatGoldMultiplier, player));
			else
				newRewards.Add(reward);
		}
		rewards.Clear();
		rewards.AddRange(newRewards);
		return true;
	}
}

