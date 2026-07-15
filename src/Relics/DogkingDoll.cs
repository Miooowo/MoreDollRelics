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
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 狗王玩偶：拾起时失去3点生命上限；回合开始时恢复1生命并获得2格挡；
/// 每3场战斗获得1点生命上限；
/// 在敌人回合内受到一次不低于生命上限20%的未格挡伤害时，击晕伤害来源并在下回合开始获得3层无实体。
/// </summary>
public sealed class DogkingDoll : RelicModel, IDollRelic
{
	private const decimal HealPerTurn = 1m;
	private const decimal BlockPerTurn = 2m;
	private const decimal BigDamageThresholdPercent = 0.2m;
	private const decimal MaxHpLossOnObtain = 3m;
	private const int CombatsPerMaxHpGain = 3;
	private const decimal MaxHpGainPerTrigger = 1m;
	private const decimal IntangibleStacksOnTrigger = 3m;

	private bool _grantIntangibleNextTurn;
	private int _combatsSinceLastMaxHpGain;

	public override RelicRarity Rarity => RelicRarity.Event;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
	{
		new DynamicVar("PickupMaxHpLoss", MaxHpLossOnObtain),
		new DynamicVar("Heal", HealPerTurn),
		new DynamicVar("Block", BlockPerTurn),
		new DynamicVar("ThresholdPercent", BigDamageThresholdPercent * 100m),
		new DynamicVar("IntangibleStacks", IntangibleStacksOnTrigger),
		new DynamicVar("CombatsPerGain", CombatsPerMaxHpGain),
		new DynamicVar("MaxHpGain", MaxHpGainPerTrigger),
	};

	[SavedProperty]
	public int CombatsSinceLastMaxHpGain
	{
		get => _combatsSinceLastMaxHpGain;
		set
		{
			AssertMutable();
			_combatsSinceLastMaxHpGain = value < 0 ? 0 : value;
		}
	}

	public override async Task AfterObtained()
	{
		if (Owner?.Creature == null)
			return;
		Flash();
		await CreatureCmd.LoseMaxHp(new BlockingPlayerChoiceContext(), Owner.Creature, MaxHpLossOnObtain, isFromCard: false);
	}

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
			await PowerCmd.Apply<IntangiblePower>(choiceContext, creature, IntangibleStacksOnTrigger, creature, null);
		}
	}

	public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != Owner?.Creature || result.UnblockedDamage <= 0)
			return;
		Creature ownerCreature = Owner.Creature;
		var combatState = ownerCreature.CombatState;
		if (combatState == null || combatState.CurrentSide != CombatSide.Enemy)
			return;
		decimal maxHp = ownerCreature.MaxHp;
		if (maxHp <= 0)
			return;
		decimal threshold = maxHp * BigDamageThresholdPercent;
		if ((decimal)result.UnblockedDamage < threshold)
			return;

		_grantIntangibleNextTurn = true;
		if (dealer != null && dealer.IsAlive && dealer.Side == CombatSide.Enemy)
		{
			Flash();
			await CreatureCmd.Stun(dealer, "DOGKING_DOLL");
		}
	}

	public override async Task AfterCombatEnd(CombatRoom _)
	{
		_grantIntangibleNextTurn = false;
		if (Owner?.Creature == null)
			return;

		CombatsSinceLastMaxHpGain++;
		if (CombatsSinceLastMaxHpGain < CombatsPerMaxHpGain)
			return;

		CombatsSinceLastMaxHpGain = 0;
		Flash();
		await CreatureCmd.GainMaxHp(Owner.Creature, MaxHpGainPerTrigger);
	}
}
