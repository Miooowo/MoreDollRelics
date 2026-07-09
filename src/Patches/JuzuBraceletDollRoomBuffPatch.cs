using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Models.Relics;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Runs;

namespace MoreDollRelics.src.Patches;

/// <summary>
/// 暗改：持有佛珠手链时，第二幕每个未触发玩偶室的问号房会使下一个问号房触发玩偶室几率 +15%（可叠加）。
/// </summary>
[HarmonyPatch]
internal static class JuzuBraceletDollRoomBuffPatch
{
	private const float ChancePerMiss = 0.15f;
	private static readonly ConditionalWeakTable<RunState, SpawnState> StateByRun = new();

	private sealed class SpawnState
	{
		public int MissedUnknownRooms;
		public bool DollRoomResolvedForAct2;
		public string? LastResolvedRoomKey;
	}

	[HarmonyTargetMethods]
	private static IEnumerable<MethodBase> TargetEventSelectionMethods()
	{
		foreach (string typeName in new[]
		         {
			         "MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer",
			         "MegaCrit.Sts2.Core.Odds.UnknownMapPointOdds",
			         "MegaCrit.Sts2.Core.Events.EventOdds",
			         "MegaCrit.Sts2.Core.Events.EventSelector"
		         })
		{
			Type? type = AccessTools.TypeByName(typeName);
			if (type == null)
				continue;

			foreach (MethodInfo method in AccessTools.GetDeclaredMethods(type))
			{
				if (!typeof(EventModel).IsAssignableFrom(method.ReturnType))
					continue;
				if (method.Name.Contains("Event", StringComparison.OrdinalIgnoreCase)
				    || method.Name.Contains("Roll", StringComparison.OrdinalIgnoreCase)
				    || method.Name.Contains("Pick", StringComparison.OrdinalIgnoreCase)
				    || method.Name.Contains("Choose", StringComparison.OrdinalIgnoreCase)
				    || method.Name.Contains("Select", StringComparison.OrdinalIgnoreCase))
					yield return method;
			}
		}
	}

	[HarmonyPostfix]
	[HarmonyPriority(Priority.Last)]
	private static void EventSelectionPostfix(object __instance, object[] __args, MethodBase __originalMethod, ref EventModel __result)
	{
		if (__result == null)
			return;
		if (!TryResolveRunState(__instance, __args, out RunState? runState) || runState == null)
			return;
		if (!IsAct2(runState))
			return;
		if (!IsSafeToForceInCurrentRoom(runState, __instance, __originalMethod))
			return;
		if (!HasJuzuBracelet(runState))
			return;

		SpawnState state = StateByRun.GetOrCreateValue(runState);
		if (__result is DollRoom || HasVisitedDollRoom(runState))
		{
			state.MissedUnknownRooms = 0;
			state.DollRoomResolvedForAct2 = true;
			return;
		}
		if (state.DollRoomResolvedForAct2)
			return;
		string roomKey = BuildRoomKey(runState);
		if (string.Equals(state.LastResolvedRoomKey, roomKey, StringComparison.Ordinal))
			return;
		state.LastResolvedRoomKey = roomKey;

		float chance = Math.Min(1f, state.MissedUnknownRooms * ChancePerMiss);
		if (chance > 0f && RollSucceeded(runState, chance))
		{
			__result = ModelDb.Event<DollRoom>();
			state.MissedUnknownRooms = 0;
			state.DollRoomResolvedForAct2 = true;
			return;
		}

		state.MissedUnknownRooms++;
	}

	private static bool RollSucceeded(RunState runState, float chance)
	{
		Rng? rng = runState.Rng?.UnknownMapPoint ?? runState.Rng?.Niche;
		if (rng == null)
			return false;
		return rng.NextFloat() < chance;
	}

	private static bool HasVisitedDollRoom(RunState runState)
	{
		return runState.VisitedEventIds.Contains(ModelDb.Event<DollRoom>().Id);
	}

	private static bool HasJuzuBracelet(RunState runState)
	{
		foreach (object? player in EnumeratePlayers(runState))
		{
			if (player == null)
				continue;
			if (GetPropertyValue(player, "Relics", "AllRelics") is not IEnumerable relics)
				continue;
			foreach (object? item in relics)
			{
				if (item is JuzuBracelet)
					return true;
			}
		}

		return false;
	}

	private static bool IsSafeToForceInCurrentRoom(RunState runState, object instance, MethodBase originalMethod)
	{
		if (!IsUnknownNodeContext(runState))
			return false;

		object? currentRoom = GetPropertyValue(runState, "CurrentRoom", "Room");
		if (currentRoom == null)
			return true;

		object? activeEvent = GetPropertyValue(currentRoom, "Event", "EventModel", "CurrentEvent");
		if (activeEvent != null)
			return false;

		string roomTypeName = currentRoom.GetType().FullName ?? currentRoom.GetType().Name;
		if (roomTypeName.Contains("Unknown", StringComparison.OrdinalIgnoreCase)
		    || roomTypeName.Contains("Question", StringComparison.OrdinalIgnoreCase))
			return true;

		string instanceTypeName = instance.GetType().FullName ?? instance.GetType().Name;
		string methodTypeName = originalMethod.DeclaringType?.FullName ?? string.Empty;
		return instanceTypeName.Contains("UnknownMapPointOdds", StringComparison.OrdinalIgnoreCase)
		       || methodTypeName.Contains("UnknownMapPointOdds", StringComparison.OrdinalIgnoreCase)
		       || instanceTypeName.Contains("EventSelector", StringComparison.OrdinalIgnoreCase)
		       || methodTypeName.Contains("EventSelector", StringComparison.OrdinalIgnoreCase);
	}

	private static bool IsUnknownNodeContext(RunState runState)
	{
		object? node = GetPropertyValue(runState, "CurrentNode", "CurrentMapNode", "CurrentMapPoint", "MapNode");
		if (node == null)
			return false;

		object? nodeType = GetPropertyValue(node, "RoomType", "NodeType", "MapPointType", "PointType", "Type", "Kind");
		string nodeText = nodeType?.ToString() ?? string.Empty;
		if (nodeText.Contains("Unknown", StringComparison.OrdinalIgnoreCase)
		    || nodeText.Contains("Question", StringComparison.OrdinalIgnoreCase))
			return true;

		string runtimeType = node.GetType().FullName ?? node.GetType().Name;
		return runtimeType.Contains("Unknown", StringComparison.OrdinalIgnoreCase)
		       || runtimeType.Contains("Question", StringComparison.OrdinalIgnoreCase);
	}

	private static IEnumerable EnumeratePlayers(RunState runState)
	{
		if (GetPropertyValue(runState, "Players", "AllPlayers", "PartyPlayers") is IEnumerable players)
			return players;
		object? owner = GetPropertyValue(runState, "Owner", "Player");
		if (owner == null)
			return Array.Empty<object>();
		return new[] { owner };
	}

	private static bool IsAct2(RunState runState)
	{
		if (TryReadIntByNames(runState, out int explicitAct, "CurrentAct", "Act", "ActNumber"))
			return explicitAct == 2;
		return runState.CurrentActIndex == 1;
	}

	private static string BuildRoomKey(RunState runState)
	{
		var parts = new List<string>(5)
		{
			runState.CurrentActIndex.ToString(CultureInfo.InvariantCulture)
		};
		if (TryReadIntByNames(runState, out int floor, "CurrentFloor", "Floor", "CurrentMapY", "MapY", "ActFloor"))
			parts.Add(floor.ToString(CultureInfo.InvariantCulture));
		if (TryReadIntByNames(runState, out int x, "CurrentMapX", "MapX", "CurrentNodeX"))
			parts.Add(x.ToString(CultureInfo.InvariantCulture));
		object? room = GetPropertyValue(runState, "CurrentRoom", "Room");
		parts.Add(room?.GetType().Name ?? "UnknownRoom");
		return string.Join(":", parts);
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
		if (direct is RunState run)
		{
			runState = run;
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
					// ignore and continue fallback probing
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
					// ignore and continue fallback probing
				}
			}
		}

		return null;
	}
}
