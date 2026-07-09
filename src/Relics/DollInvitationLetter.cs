using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Saves.Runs;

namespace MoreDollRelics.src.Relics;

/// <summary>
/// 玩偶的邀请函：击败 3 名精英敌人后，第二幕第一个问号房必定为玩偶室（触发后失效）。
/// </summary>
public sealed class DollInvitationLetter : RelicModel, IDollRelic
{
	private const int RequiredEliteKills = 3;

	[SavedProperty]
	public int EliteKillsProgress { get; set; }

	[SavedProperty]
	public bool IsConsumed { get; set; }

	public override RelicRarity Rarity => RelicRarity.Ancient;

	public override bool ShowCounter => !IsConsumed;

	public override int DisplayAmount => EliteKillsProgress;

	public bool IsReadyToForceDollRoom => !IsConsumed && EliteKillsProgress >= RequiredEliteKills;

	protected override IEnumerable<DynamicVar> CanonicalVars => new[]
	{
		new DynamicVar("RequiredElites", RequiredEliteKills)
	};

	public override IReadOnlySet<RoomType> ModifyUnknownMapPointRoomTypes(IReadOnlySet<RoomType> roomTypes)
	{
		// 参考佛珠手链路径：先在问号房类型阶段保证走事件，再由补丁把事件替换为玩偶室。
		if (!IsReadyToForceDollRoom || !IsAct2())
			return roomTypes;

		return new HashSet<RoomType> { RoomType.Event };
	}

	public override async Task AfterCombatVictory(CombatRoom room)
	{
		if (IsConsumed)
			return;
		if (room.RoomType != RoomType.Elite)
			return;

		EliteKillsProgress++;
		InvokeDisplayAmountChanged();
		Flash();
	}

	public void Consume()
	{
		if (IsConsumed)
			return;
		IsConsumed = true;
		Status = RelicStatus.Disabled;
		InvokeDisplayAmountChanged();
		Flash();
	}

	private bool IsAct2()
	{
		object? runState = Owner?.RunState;
		if (runState == null)
			return false;

		object? explicitAct = GetMemberValue(runState, "CurrentAct", "Act", "ActNumber");
		if (explicitAct is int actNum)
			return actNum == 2;

		object? index = GetMemberValue(runState, "CurrentActIndex");
		return index is int i && (i == 1 || i == 2);
	}

	private static object? GetMemberValue(object source, params string[] names)
	{
		Type type = source.GetType();
		foreach (string name in names)
		{
			var prop = HarmonyLib.AccessTools.Property(type, name);
			if (prop != null)
			{
				try
				{
					return prop.GetValue(source);
				}
				catch
				{
					// ignore and continue fallback probing
				}
			}

			var field = HarmonyLib.AccessTools.Field(type, name);
			if (field != null)
			{
				try
				{
					return field.GetValue(source);
				}
				catch
				{
					// ignore and continue fallback probing
				}
			}
		}

		return null;
	}
}

