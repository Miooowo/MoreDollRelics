namespace MoreDollRelics.src.Relics;

/// <summary>
/// 标记本模组「玩偶」遗物，逻辑标签为 <see cref="DollRelicTag"/>（控制台 <c>relic add doll_all</c> 等可据此枚举）。
/// </summary>
public interface IDollRelic
{
}

/// <summary>与 <see cref="IDollRelic"/> 对应的标签字符串常量。</summary>
public static class DollRelicTag
{
	public const string Doll = "doll";
}
