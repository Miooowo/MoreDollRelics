using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Relics;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.Extensions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.ValueProps;
using MoreDollRelics.src.Relics;

namespace MoreDollRelics.src.Patches;

[HarmonyPatch(typeof(DollRoom))]
internal static class DollRoomExtraDollsPatch
{
    private static readonly MethodInfo SetEventStateMethod =
        AccessTools.Method(typeof(EventModel), "SetEventState", new[] { typeof(LocString), typeof(IReadOnlyList<EventOption>) });

    private static readonly MethodInfo SetEventFinishedMethod =
        AccessTools.Method(typeof(EventModel), "SetEventFinished", new[] { typeof(LocString) });

    /// <summary>
    /// 选项1「随便抓一尊」：从原版3个+模组5个中随机获得一个玩偶。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("ChooseRandom")]
    private static bool ChooseRandom_Replace(DollRoom __instance)
    {
        _ = CustomChooseRandom(__instance);
        return false;
    }

    /// <summary>
    /// 选项2「花点时间慢慢看」：从原版3个+模组5个中随机抽2个供选择。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("TakeSomeTime")]
    private static bool TakeSomeTime_Replace(DollRoom __instance)
    {
        _ = CustomTakeSomeTime(__instance);
        return false;
    }

    /// <summary>
    /// 替换原版第三个选项“仔细检查然后挑选最好的那个”的行为：
    /// 伤害不变，但之后展示 3 个原版玩偶 + 模组玩偶子页可供选择。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("Examine")]
    private static bool Examine_Replace(DollRoom __instance)
    {
        _ = CustomExamine(__instance);
        return false; // 跳过原方法
    }

    /// <summary>原版 3 个玩偶（选项1/2 与模组混合用）。</summary>
    private static readonly (RelicModel relic, string descriptionKey)[] VanillaDollPool =
    {
        (ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.DaughterOfTheWind>(), "DOLL_ROOM.pages.DAUGHTER_OF_WIND.description"),
        (ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.MrStruggles>(), "DOLL_ROOM.pages.MR_STRUGGLES.description"),
        (ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.BingBong>(), "DOLL_ROOM.pages.FABLE.description"),
    };

    private static async Task CustomChooseRandom(DollRoom dollRoom)
    {
        if (dollRoom.Owner == null)
            return;
        var pool = new List<(RelicModel relic, string descriptionKey)>(VanillaDollPool.Length + ModDollPool.Length);
        pool.AddRange(VanillaDollPool);
        pool.AddRange(ModDollPool);
        Rng rng = dollRoom.Owner.RunState?.Rng?.Niche ?? Rng.Chaotic;
        var chosen = rng.NextItem(pool);
        await RelicCmd.Obtain(chosen.relic.ToMutable(), dollRoom.Owner);
        var done = new LocString("events", chosen.descriptionKey);
        SetEventFinishedMethod.Invoke(dollRoom, new object[] { done });
    }

    private static async Task CustomTakeSomeTime(DollRoom dollRoom)
    {
        if (dollRoom.Owner?.Creature == null)
            return;
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            dollRoom.Owner.Creature,
            (DamageVar)dollRoom.DynamicVars["TakeTimeHpLoss"],
            null,
            null
        );

        var combined = new List<(RelicModel relic, string descriptionKey)>(VanillaDollPool.Length + ModDollPool.Length);
        combined.AddRange(VanillaDollPool);
        combined.AddRange(ModDollPool);

        Rng rng = dollRoom.Owner.RunState?.Rng?.Niche ?? Rng.Chaotic;
        var indices = new List<int>(combined.Count);
        for (int i = 0; i < combined.Count; i++)
            indices.Add(i);
        indices.UnstableShuffle(rng);

        int take = System.Math.Min(2, combined.Count);
        var options = new List<EventOption>();
        for (int i = 0; i < take; i++)
        {
            var (relic, doneKey) = combined[indices[i]];
            options.Add(MakeTakeOption(dollRoom, relic, doneKey, useVanillaOptionText: true));
        }

        var desc = new LocString("events", "DOLL_ROOM.pages.TAKE_SOME_TIME.description");
        SetEventStateMethod.Invoke(dollRoom, new object[] { desc, options });
    }

    private static async Task CustomExamine(DollRoom dollRoom)
    {
        // 按原版逻辑先掉血（使用同一个 DynamicVar 名称）
        await CreatureCmd.Damage(
            new ThrowingPlayerChoiceContext(),
            dollRoom.Owner!.Creature,
            (DamageVar)dollRoom.DynamicVars["ExamineHpLoss"],
            null,
            null
        );

        // 3 个原版玩偶（保持原有描述 key）
        var options = new List<EventOption>
        {
            MakeTakeOption(
                dollRoom,
                ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.DaughterOfTheWind>(),
                "DOLL_ROOM.pages.DAUGHTER_OF_WIND.description"
            ),
            MakeTakeOption(
                dollRoom,
                ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.MrStruggles>(),
                "DOLL_ROOM.pages.MR_STRUGGLES.description"
            ),
            MakeTakeOption(
                dollRoom,
                ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.BingBong>(),
                "DOLL_ROOM.pages.FABLE.description"
            )
        };

        // 第 4 个选项：进入模组玩偶子页面
        options.Add(new EventOption(
            dollRoom,
            () => ShowExtraDollChoices(dollRoom),
            "NEW_DOLL_ROOM.pages.INITIAL.options.EXTRA"
        ));

        var desc = new LocString("events", "DOLL_ROOM.pages.EXAMINE.description");
        SetEventStateMethod.Invoke(dollRoom, new object[] { desc, options });
        await Task.CompletedTask;
    }

    private const int MoreDollsHpCost = 5;
    private const int ModDollsShownCount = 3;

    /// <summary>模组玩偶池：7 个玩偶，用于随机抽 3 个展示。</summary>
    private static readonly (RelicModel relic, string descriptionKey)[] ModDollPool =
    {
        (ModelDb.Relic<VistaDoll>(), "NEW_DOLL_ROOM.pages.VISTA_DOLL.description"),
        (ModelDb.Relic<wxwDoll>(), "NEW_DOLL_ROOM.pages.WXW_DOLL.description"),
        (ModelDb.Relic<BaizealerDoll>(), "NEW_DOLL_ROOM.pages.BAIZEALER_DOLL.description"),
        (ModelDb.Relic<GallopDoll>(), "NEW_DOLL_ROOM.pages.GALLOP_DOLL.description"),
        (ModelDb.Relic<DogkingDoll>(), "NEW_DOLL_ROOM.pages.DOGKING_DOLL.description"),
        (ModelDb.Relic<RhineDoll>(), "NEW_DOLL_ROOM.pages.RHINE_DOLL.description"),
        (ModelDb.Relic<PansyDoll>(), "NEW_DOLL_ROOM.pages.PANSY_DOLL.description"),
    };

    /// <summary>
    /// 子页面：随机展示 3 个模组玩偶 + 选项「失去5点生命刷新」（参考 SlipperyBridge 的 HoldOn 刷新）。
    /// </summary>
    private static async Task ShowExtraDollChoices(DollRoom dollRoom)
    {
        BuildAndShowExtraDollChoices(dollRoom, payHpFirst: false);
        await Task.CompletedTask;
    }

    /// <summary>
    /// 失去 5 血后刷新：扣血并重新随机 3 个玩偶再展示同一页面。
    /// </summary>
    private static async Task ShowMoreDollChoices(DollRoom dollRoom)
    {
        if (dollRoom.Owner?.Creature != null)
        {
            await CreatureCmd.Damage(
                new ThrowingPlayerChoiceContext(),
                dollRoom.Owner.Creature,
                (decimal)MoreDollsHpCost,
                ValueProp.Unblockable | ValueProp.Unpowered,
                null,
                null
            );
        }

        BuildAndShowExtraDollChoices(dollRoom, payHpFirst: true);
        await Task.CompletedTask;
    }

    private static void BuildAndShowExtraDollChoices(DollRoom dollRoom, bool payHpFirst)
    {
        Rng rng = dollRoom.Owner?.RunState?.Rng?.Niche;
        if (rng == null)
        {
            rng = Rng.Chaotic;
        }

        var indices = new List<int>(ModDollPool.Length);
        for (int i = 0; i < ModDollPool.Length; i++)
            indices.Add(i);
        indices.UnstableShuffle(rng);

        var options = new List<EventOption>();
        int take = System.Math.Min(ModDollsShownCount, ModDollPool.Length);
        for (int i = 0; i < take; i++)
        {
            var (relic, descriptionKey) = ModDollPool[indices[i]];
            options.Add(MakeTakeOption(dollRoom, relic, descriptionKey));
        }

        options.Add(new EventOption(
            dollRoom,
            () => ShowMoreDollChoices(dollRoom),
            "NEW_DOLL_ROOM.pages.EXTRA.options.MORE"
        ));

        string descKey = payHpFirst ? "NEW_DOLL_ROOM.pages.MORE.description" : "NEW_DOLL_ROOM.pages.EXTRA.description";
        var desc = new LocString("events", descKey);
        SetEventStateMethod.Invoke(dollRoom, new object[] { desc, options });
    }

    private static EventOption MakeTakeOption(DollRoom dollRoom, RelicModel relic, string doneKey, bool useVanillaOptionText = false)
    {
        LocString title = relic.Title;
        string optionDescKey = useVanillaOptionText ? "DOLL_ROOM.pages.TAKE.options.TAKE.description" : "NEW_DOLL_ROOM.pages.TAKE.options.TAKE.description";
        LocString desc = new LocString("events", optionDescKey);
        desc.Add("RelicName", relic.Title);

        return new EventOption(
                dollRoom,
                async () =>
                {
                    if (dollRoom.Owner != null)
                    {
                        await RelicCmd.Obtain(relic.ToMutable(), dollRoom.Owner);
                    }

                    var done = new LocString("events", doneKey);
                    SetEventFinishedMethod.Invoke(dollRoom, new object[] { done });
                },
                title,
                desc,
                relic.Title.GetRawText(),
                HoverTipFactory.FromRelic(relic)
            )
            .WithOverridenHistoryName(relic.Title);
    }
}

