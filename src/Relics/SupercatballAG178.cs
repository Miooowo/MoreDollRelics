using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Orbs;
using MegaCrit.Sts2.Core.ValueProps;

namespace MoreDollRelics.src.Relics;

/// <summary>事件遗物「娜娜因猫球」：战斗开始时生成等离子球；每支付 1 点卡牌能量费用获得 4 点格挡；仅玩偶房模组池。relics 表 key：SUPERCATBALL_A_G178。</summary>
public sealed class SupercatballAG178 : RelicModel, IDollRelic
{
	private const decimal BlockPerEnergy = 4m;

	public override RelicRarity Rarity => RelicRarity.Event;

	protected override IEnumerable<IHoverTip> ExtraHoverTips => new[]
	{
		HoverTipFactory.Static(StaticHoverTip.Channeling),
		HoverTipFactory.FromOrb<PlasmaOrb>()
	};
	
	protected override IEnumerable<DynamicVar> CanonicalVars => new DynamicVar[]
	{
		new EnergyVar(1),
		new DynamicVar("BlockPerEnergy", BlockPerEnergy),
	};

	public override async Task BeforeCombatStart()
	{
		if (Owner == null)
			return;
		Flash();
		await OrbCmd.Channel<PlasmaOrb>(new ThrowingPlayerChoiceContext(), Owner);
	}

	public override async Task AfterEnergySpent(CardModel card, int amount)
	{
		if (Owner?.Creature == null || amount <= 0)
			return;
		if (card.Owner != Owner)
			return;
		decimal block = BlockPerEnergy * amount;
		await CreatureCmd.GainBlock(Owner.Creature, block, ValueProp.Unpowered, null);
	}
}
