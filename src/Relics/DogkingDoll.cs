using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
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
/// 在敌人回合内受到一次不低于生命上限20%的未格挡伤害时，击晕来源敌人并在下回合开始获得3层无实体。
/// </summary>
public sealed class DogkingDoll : RelicModel, IDollRelic
{
	private const decimal HealPerTurn = 1m;
	private const decimal BlockPerTurn = 2m;
	private const decimal BigDamageThresholdPercent = 0.2m;
	private const decimal MaxHpLossOnObtain = 3m;
	private const decimal MaxHpGainPerCycle = 1m;
	private const int BattlesPerMaxHpGain = 3;
	private const decimal IntangibleStacksOnTrigger = 3m;
	private const decimal StunDurationTurns = 1m;

	private decimal _pendingIntangibleStacks;

	[SavedProperty]
	public int BattlesCleared { get; set; }

	public override RelicRarity Rarity => RelicRarity.Event;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
	{
		new DynamicVar("PickupMaxHpLoss", MaxHpLossOnObtain),
		new DynamicVar("Heal", HealPerTurn),
		new DynamicVar("Block", BlockPerTurn),
		new DynamicVar("ThresholdPercent", BigDamageThresholdPercent * 100m),
		new DynamicVar("BattlesPerGain", BattlesPerMaxHpGain),
		new DynamicVar("MaxHpGain", MaxHpGainPerCycle),
		new DynamicVar("IntangibleStacks", IntangibleStacksOnTrigger)
	};

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

		if (_pendingIntangibleStacks > 0m)
		{
			decimal stacks = _pendingIntangibleStacks;
			_pendingIntangibleStacks = 0m;
			Flash();
			await PowerCmd.Apply<IntangiblePower>(choiceContext, creature, stacks, creature, null);
		}
	}

	public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != Owner?.Creature || result.UnblockedDamage <= 0)
			return;
		var combatState = Owner.Creature.CombatState;
		if (combatState == null || combatState.CurrentSide != CombatSide.Enemy)
			return;
		decimal maxHp = Owner.Creature.MaxHp;
		if (maxHp <= 0)
			return;
		decimal threshold = maxHp * BigDamageThresholdPercent;
		if ((decimal)result.UnblockedDamage >= threshold)
		{
			_pendingIntangibleStacks += IntangibleStacksOnTrigger;
			if (dealer != null)
				await TryApplyStun(choiceContext, dealer, target);
		}
	}

	public override async Task AfterCombatEnd(CombatRoom _)
	{
		_pendingIntangibleStacks = 0m;
		if (Owner?.Creature == null || Owner.Creature.IsDead)
			return;

		BattlesCleared++;
		if (BattlesCleared % BattlesPerMaxHpGain == 0)
		{
			Flash();
			await TryGainMaxHp(Owner.Creature, MaxHpGainPerCycle);
		}
	}

	private static async Task TryGainMaxHp(Creature creature, decimal amount)
	{
		MethodInfo? method = AccessTools.Method(
			typeof(CreatureCmd),
			"GainMaxHp",
			new[] { typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal), typeof(bool) }
		);
		if (method != null)
		{
			object? task = method.Invoke(
				null,
				new object[] { new ThrowingPlayerChoiceContext(), creature, amount, false }
			);
			if (task is Task awaitable)
			{
				await awaitable;
				return;
			}
		}

		method = AccessTools.Method(
			typeof(CreatureCmd),
			"GainMaxHp",
			new[] { typeof(PlayerChoiceContext), typeof(Creature), typeof(decimal) }
		);
		if (method != null)
		{
			object? task = method.Invoke(
				null,
				new object[] { new ThrowingPlayerChoiceContext(), creature, amount }
			);
			if (task is Task awaitable)
			{
				await awaitable;
				return;
			}
		}

		// 若不存在增上限命令，退化为不执行（避免在未知 API 上造成崩溃）。
	}

	private static async Task TryApplyStun(PlayerChoiceContext ctx, Creature enemyTarget, Creature source)
	{
		Type? stunPowerType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Powers.StunPower")
		                     ?? AccessTools.TypeByName("MegaCrit.Sts2.Core.Models.Powers.StunnedPower");
		if (stunPowerType == null)
			return;

		MethodInfo? genericApply = typeof(PowerCmd)
			.GetMethods(BindingFlags.Public | BindingFlags.Static)
			.FirstOrDefault(m =>
				m.Name == "Apply" &&
				m.IsGenericMethodDefinition &&
				m.GetGenericArguments().Length == 1 &&
				m.GetParameters().Length == 5
			);
		if (genericApply == null)
			return;

		MethodInfo apply = genericApply.MakeGenericMethod(stunPowerType);
		object? result = apply.Invoke(null, new object?[] { ctx, enemyTarget, StunDurationTurns, source, null });
		if (result is Task task)
			await task;
	}
}
