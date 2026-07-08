using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;
using Steamworks;

namespace MoreDollRelics.src.Patches;

/// <summary>
/// 第二幕玩偶室出现逻辑：
/// 1) 第一幕精英胜利 >=3 时，第二幕第一个问号房强制玩偶室（若本局尚未出现）。
/// 2) 第二幕持有佛珠手链且问号房没出玩偶室时，累加 +15%（可叠加，出现后清零）。
/// 3) 每有一个玩偶主人玩家参与本局，第二幕问号房额外 +10%。
/// 4) 玩偶室本局只触发一次（触发后不再加权/强制）。
/// </summary>
[HarmonyPatch]
internal static class DollRoomSpawnPatch
{
	private const decimal BeadFailBonusChance = 0.15m;
	private const decimal OwnerBonusChance = 0.10m;

	private static readonly HashSet<string> DollOwnerNames = new(StringComparer.Ordinal)
	{
		"󰀕ClockCycas󰀕",
		"艾趣44",
		"礼帽人",
		"Sssk",
		"铃铛蔷薇",
		"无敌小狗 THE U",
		"CwC",
		"󰀢絔狼BaiZealer󰀢",
		"紫色三色堇"
	};

	private static readonly HashSet<string> NormalizedDollOwnerNames = new(
		DollOwnerNames.Select(NormalizePlayerName),
		StringComparer.OrdinalIgnoreCase
	);

	private static readonly ConditionalWeakTable<RunState, DollRoomSpawnState> StateByRun = new();

	private sealed class DollRoomSpawnState
	{
		public int Act1EliteVictories;
		public int Act2QuestionRoomsSeen;
		public int Act2BeadFailStacks;
		public bool DollRoomSpawnedThisRun;
		public string? LastQuestionRoomKey;
		public string? LastCountedEliteRoomKey;
	}

	[HarmonyTargetMethods]
	private static IEnumerable<MethodBase> TargetEventSelectionMethods()
	{
		foreach (string typeName in new[]
		         {
			         "MegaCrit.Sts2.Core.Odds.UnknownMapPointOdds",
			         "MegaCrit.Sts2.Core.Events.EventOdds",
			         "MegaCrit.Sts2.Core.Events.EventSelector"
		         })
		{
			Type? type = AccessTools.TypeByName(typeName);
			if (type == null)
				continue;

			foreach (string methodName in new[] { "Roll", "RollEvent", "PickEvent", "ChooseEvent" })
			{
				MethodInfo? method = AccessTools.DeclaredMethod(type, methodName);
				if (method == null)
					continue;
				if (!typeof(EventModel).IsAssignableFrom(method.ReturnType))
					continue;
				yield return method;
			}
		}
	}

	[HarmonyPostfix]
	private static void EventSelectionPostfix(object __instance, object[] __args, ref EventModel __result)
	{
		if (__result == null)
			return;
		if (!TryResolveRunState(__instance, __args, out RunState? runState) || runState == null)
			return;
		if (runState.CurrentActIndex != 1)
			return;

		DollRoomSpawnState state = StateByRun.GetOrCreateValue(runState);
		string roomKey = BuildRoomKey(runState);
		bool isNewQuestionRoom = !string.Equals(state.LastQuestionRoomKey, roomKey, StringComparison.Ordinal);
		if (isNewQuestionRoom)
		{
			state.LastQuestionRoomKey = roomKey;
			state.Act2QuestionRoomsSeen++;
		}

		if (state.DollRoomSpawnedThisRun)
			return;

		bool selectedDollRoom = IsDollRoom(__result);
		int act1EliteWins = GetAct1EliteVictories(runState, state);
		bool forceFirstQuestionRoom = isNewQuestionRoom && state.Act2QuestionRoomsSeen == 1 && act1EliteWins >= 3;
		if (forceFirstQuestionRoom)
		{
			__result = ModelDb.Event<DollRoom>();
			MarkDollRoomSpawned(state);
			return;
		}

		if (selectedDollRoom)
		{
			MarkDollRoomSpawned(state);
			return;
		}

		decimal chance = ComputeExtraChance(runState, state);
		if (chance > 0m && RollChance(runState, chance))
		{
			__result = ModelDb.Event<DollRoom>();
			MarkDollRoomSpawned(state);
			return;
		}

		if (HasJuzuBracelet(runState))
			state.Act2BeadFailStacks++;
	}

	[HarmonyPatch]
	private static class Act1EliteCounterPatch
	{
		[HarmonyTargetMethods]
		private static IEnumerable<MethodBase> TargetMethods()
		{
			Type? combatRoomType = AccessTools.TypeByName("MegaCrit.Sts2.Core.Rooms.CombatRoom");
			if (combatRoomType == null)
				yield break;

			foreach (string methodName in new[]
			         {
				         "AfterCombatEnd",
				         "AfterCombatWon",
				         "OnCombatWon",
				         "ResolveRewards",
				         "GiveRewards"
			         })
			{
				MethodInfo? method = AccessTools.DeclaredMethod(combatRoomType, methodName);
				if (method != null)
					yield return method;
			}
		}

		[HarmonyPostfix]
		private static void Postfix(object __instance, object[] __args)
		{
			if (!TryResolveRunState(__instance, __args, out RunState? runState) || runState == null)
				return;
			if (runState.CurrentActIndex != 0)
				return;

			object? currentRoom = GetPropertyValue(runState, "CurrentRoom", "Room");
			if (currentRoom == null)
				currentRoom = __instance;
			if (currentRoom == null || !currentRoom.GetType().Name.Contains("Elite", StringComparison.OrdinalIgnoreCase))
				return;

			DollRoomSpawnState state = StateByRun.GetOrCreateValue(runState);
			string roomKey = BuildRoomKey(runState);
			if (string.Equals(state.LastCountedEliteRoomKey, roomKey, StringComparison.Ordinal))
				return;

			state.LastCountedEliteRoomKey = roomKey;
			state.Act1EliteVictories++;
		}
	}

	private static int GetAct1EliteVictories(RunState runState, DollRoomSpawnState state)
	{
		if (state.Act1EliteVictories > 0)
			return state.Act1EliteVictories;

		if (TryReadIntByNames(runState, out int direct, "Act1EliteKills", "Act1EliteVictories"))
			return direct;

		object? stats = GetPropertyValue(runState, "RunStats", "Stats");
		if (stats != null && TryReadIntByNames(stats, out int fromStats, "Act1EliteKills", "ElitesKilledAct1", "EliteVictoriesAct1"))
			return fromStats;

		if (TryReadIntByNames(runState, out int generic, "ElitesKilled", "EliteVictories", "DefeatedEliteCount"))
			return generic;

		return 0;
	}

	private static decimal ComputeExtraChance(RunState runState, DollRoomSpawnState state)
	{
		int owners = CountDollOwners(runState);
		decimal chance = state.Act2BeadFailStacks * BeadFailBonusChance + owners * OwnerBonusChance;
		if (chance < 0m)
			return 0m;
		return chance > 1m ? 1m : chance;
	}

	private static void MarkDollRoomSpawned(DollRoomSpawnState state)
	{
		state.DollRoomSpawnedThisRun = true;
		state.Act2BeadFailStacks = 0;
	}

	private static bool IsDollRoom(EventModel eventModel) =>
		eventModel is DollRoom || eventModel.GetType().Name.Equals("DollRoom", StringComparison.Ordinal);

	private static bool RollChance(RunState runState, decimal chance)
	{
		Rng rng = runState.Rng?.Niche ?? Rng.Chaotic;
		decimal roll = (decimal)rng.NextDouble();
		return roll < chance;
	}

	private static int CountDollOwners(RunState runState)
	{
		List<string> names = GatherPlayerNames(runState);
		int count = 0;
		foreach (string raw in names)
		{
			if (DollOwnerNames.Contains(raw))
			{
				count++;
				continue;
			}

			string normalized = NormalizePlayerName(raw);
			if (NormalizedDollOwnerNames.Contains(normalized))
				count++;
		}

		return count;
	}

	private static List<string> GatherPlayerNames(RunState runState)
	{
		var names = new List<string>();
		object? playersObj = GetPropertyValue(runState, "Players", "AllPlayers", "PartyPlayers");
		if (playersObj is IEnumerable players)
		{
			foreach (object? player in players)
			{
				if (player == null)
					continue;
				string? name = GetStringProperty(player, "DisplayName", "PlayerName", "UserName", "Name");
				if (!string.IsNullOrWhiteSpace(name))
					names.Add(name.Trim());
			}
		}

		if (names.Count > 0)
			return names;

		try
		{
			string steamName = SteamFriends.GetPersonaName();
			if (!string.IsNullOrWhiteSpace(steamName))
				names.Add(steamName.Trim());
		}
		catch
		{
			// 在未初始化 Steam 时忽略，不影响主逻辑。
		}

		return names;
	}

	private static bool HasJuzuBracelet(RunState runState)
	{
		IEnumerable? players = GetPropertyValue(runState, "Players", "AllPlayers", "PartyPlayers") as IEnumerable;
		if (players == null)
		{
			object? onePlayer = GetPropertyValue(runState, "Owner", "Player");
			players = onePlayer as IEnumerable ?? (onePlayer != null ? new[] { onePlayer } : Array.Empty<object>());
		}

		foreach (object? player in players)
		{
			if (player == null)
				continue;
			if (PlayerHasJuzuBracelet(player))
				return true;
		}

		return false;
	}

	private static bool PlayerHasJuzuBracelet(object player)
	{
		if (GetPropertyValue(player, "Relics", "AllRelics") is not IEnumerable relics)
			return false;
		foreach (object? relic in relics)
		{
			if (relic == null)
				continue;
			string relicTypeName = relic.GetType().Name;
			if (relicTypeName.Contains("Juzu", StringComparison.OrdinalIgnoreCase)
			    && relicTypeName.Contains("Bracelet", StringComparison.OrdinalIgnoreCase))
				return true;

			object? id = GetPropertyValue(relic, "Id");
			if (id == null)
				continue;
			string? entry = GetStringProperty(id, "Entry");
			if (string.IsNullOrWhiteSpace(entry))
				entry = id.ToString();
			if (string.IsNullOrWhiteSpace(entry))
				continue;
			if (entry.Contains("JUZU", StringComparison.OrdinalIgnoreCase)
			    && entry.Contains("BRACELET", StringComparison.OrdinalIgnoreCase))
				return true;
		}

		return false;
	}

	private static string BuildRoomKey(RunState runState)
	{
		var parts = new List<string>(6)
		{
			runState.CurrentActIndex.ToString(CultureInfo.InvariantCulture)
		};

		foreach (string name in new[] { "CurrentFloor", "Floor", "CurrentMapY", "MapY", "CurrentNodeY" })
		{
			if (TryReadIntByNames(runState, out int value, name))
			{
				parts.Add(value.ToString(CultureInfo.InvariantCulture));
				break;
			}
		}

		foreach (string name in new[] { "CurrentMapX", "MapX", "CurrentNodeX" })
		{
			if (TryReadIntByNames(runState, out int value, name))
			{
				parts.Add(value.ToString(CultureInfo.InvariantCulture));
				break;
			}
		}

		object? room = GetPropertyValue(runState, "CurrentRoom", "Room");
		parts.Add(room?.GetType().Name ?? "UnknownRoom");
		return string.Join(":", parts);
	}

	private static string NormalizePlayerName(string name)
	{
		if (string.IsNullOrWhiteSpace(name))
			return string.Empty;

		var chars = new List<char>(name.Length);
		foreach (char ch in name.Trim())
		{
			if (ch >= '\uE000' && ch <= '\uF8FF')
				continue;
			chars.Add(ch);
		}

		return new string(chars.ToArray()).Trim();
	}

	private static bool TryResolveRunState(object? instance, object[]? args, out RunState? runState)
	{
		runState = null;
		if (TryGetRunState(instance, out runState))
			return true;

		if (args == null)
			return false;

		foreach (object? arg in args)
		{
			if (TryGetRunState(arg, out runState))
				return true;
		}

		return false;
	}

	private static bool TryGetRunState(object? source, out RunState? runState)
	{
		runState = null;
		if (source == null)
			return false;
		if (source is RunState rs)
		{
			runState = rs;
			return true;
		}

		object? direct = GetPropertyValue(source, "RunState");
		if (direct is RunState directRun)
		{
			runState = directRun;
			return true;
		}

		object? owner = GetPropertyValue(source, "Owner");
		if (owner != null)
		{
			object? fromOwner = GetPropertyValue(owner, "RunState");
			if (fromOwner is RunState ownerRun)
			{
				runState = ownerRun;
				return true;
			}
		}

		return false;
	}

	private static bool TryReadIntByNames(object source, out int value, params string[] names)
	{
		foreach (string name in names)
		{
			object? raw = GetPropertyValue(source, name);
			if (raw == null)
				continue;
			switch (raw)
			{
				case int i:
					value = i;
					return true;
				case long l when l <= int.MaxValue && l >= int.MinValue:
					value = (int)l;
					return true;
				case short s:
					value = s;
					return true;
				case byte b:
					value = b;
					return true;
			}
		}

		value = 0;
		return false;
	}

	private static string? GetStringProperty(object source, params string[] names)
	{
		foreach (string name in names)
		{
			object? value = GetPropertyValue(source, name);
			if (value is string str)
				return str;
		}

		return null;
	}

	private static object? GetPropertyValue(object source, params string[] names)
	{
		Type type = source.GetType();
		foreach (string name in names)
		{
			PropertyInfo? prop = AccessTools.Property(type, name);
			if (prop != null)
			{
				try
				{
					return prop.GetValue(source);
				}
				catch
				{
					// 反射读取失败时继续尝试下一个候选。
				}
			}

			FieldInfo? field = AccessTools.Field(type, name);
			if (field != null)
			{
				try
				{
					return field.GetValue(source);
				}
				catch
				{
					// 反射读取失败时继续尝试下一个候选。
				}
			}
		}

		return null;
	}
}
