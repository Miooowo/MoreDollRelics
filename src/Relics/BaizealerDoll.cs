using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
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
/// 絔狼玩偶：每场战斗开始时获得6点荆棘；每受到一次攻击，随机对敌人造成1点伤害6次。
/// </summary>
public sealed class BaizealerDoll : RelicModel
{
	private const decimal ThornsAmount = 6m;
	private const int HitsPerAttack = 6;
	private const decimal DamagePerHit = 1m;

	public override RelicRarity Rarity => RelicRarity.Event;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[] { new PowerVar<ThornsPower>(ThornsAmount), new DynamicVar("Hits", HitsPerAttack), new DynamicVar("Damage", DamagePerHit) };

	public override async Task BeforeCombatStart()
	{
		if (Owner?.Creature == null)
			return;
		Flash();
		await PowerCmd.Apply<ThornsPower>(Owner.Creature, ThornsAmount, Owner.Creature, null);
	}

	public override async Task AfterDamageReceived(PlayerChoiceContext choiceContext, Creature target, DamageResult result, ValueProp props, Creature? dealer, CardModel? cardSource)
	{
		if (target != Owner.Creature || result.UnblockedDamage <= 0)
			return;
		var combatState = Owner.Creature.CombatState;
		if (combatState == null)
			return;
		var hittable = combatState.HittableEnemies.ToList();
		if (hittable.Count == 0)
			return;
		var rng = Owner.RunState.Rng.CombatTargets;
		for (int i = 0; i < HitsPerAttack; i++)
		{
			Creature? enemy = rng.NextItem(hittable);
			if (enemy != null && enemy.IsAlive)
			{
				Flash();
				await CreatureCmd.Damage(new ThrowingPlayerChoiceContext(), enemy, DamagePerHit, ValueProp.Unpowered, Owner.Creature);
			}
		}
	}
}

