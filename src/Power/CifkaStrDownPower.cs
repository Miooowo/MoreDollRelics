using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MoreDollRelics.src.Relics;

namespace MoreDollRelics.src.Power;

/// <summary>西夫卡玩偶：战斗开始时施加的临时力量降低（本回合结束后恢复）。</summary>
public sealed class CifkaStrDownPower : TemporaryStrengthPower
{
	public override AbstractModel OriginModel => ModelDb.Relic<CifkaDoll>();

	protected override bool IsPositive => false;
}
