using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.HoverTips;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Localization.DynamicVars;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
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
    /// 替换原版第三个选项“仔细检查然后挑选最好的那个”的行为：
    /// 伤害不变，但之后展示 3 个原版玩偶 + 3 个模组玩偶可供选择。
    /// </summary>
    [HarmonyPrefix]
    [HarmonyPatch("Examine")]
    private static bool Examine_Replace(DollRoom __instance)
    {
        _ = CustomExamine(__instance);
        return false; // 跳过原方法
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

    /// <summary>
    /// 子页面：展示模组 3 个玩偶（薇斯塔、王筱巫、絔狼）+ 选项「失去5点生命再看几个」。
    /// </summary>
    private static async Task ShowExtraDollChoices(DollRoom dollRoom)
    {
        var options = new List<EventOption>
        {
            MakeTakeOption(
                dollRoom,
                ModelDb.Relic<VistaDoll>(),
                "NEW_DOLL_ROOM.pages.VISTA_DOLL.description"
            ),
            MakeTakeOption(
                dollRoom,
                ModelDb.Relic<wxwDoll>(),
                "NEW_DOLL_ROOM.pages.WXW_DOLL.description"
            ),
            MakeTakeOption(
                dollRoom,
                ModelDb.Relic<BaizealerDoll>(),
                "NEW_DOLL_ROOM.pages.BAIZEALER_DOLL.description"
            ),
            new EventOption(
                dollRoom,
                () => ShowMoreDollChoices(dollRoom),
                "NEW_DOLL_ROOM.pages.EXTRA.options.MORE"
            )
        };

        var desc = new LocString("events", "NEW_DOLL_ROOM.pages.EXTRA.description");
        SetEventStateMethod.Invoke(dollRoom, new object[] { desc, options });
        await Task.CompletedTask;
    }

    /// <summary>
    /// 失去 5 血后进入的页面：只展示加洛普、狗王两个玩偶。
    /// </summary>
    private static async Task ShowMoreDollChoices(DollRoom dollRoom)
    {
        if (dollRoom.Owner?.Creature != null)
        {
            await CreatureCmd.Damage(
                new ThrowingPlayerChoiceContext(),
                dollRoom.Owner.Creature,
                5m,
                ValueProp.Unblockable | ValueProp.Unpowered,
                null,
                null
            );
        }

        var options = new List<EventOption>
        {
            MakeTakeOption(
                dollRoom,
                ModelDb.Relic<GallopDoll>(),
                "NEW_DOLL_ROOM.pages.GALLOP_DOLL.description"
            ),
            MakeTakeOption(
                dollRoom,
                ModelDb.Relic<DogkingDoll>(),
                "NEW_DOLL_ROOM.pages.DOGKING_DOLL.description"
            )
        };

        var desc = new LocString("events", "NEW_DOLL_ROOM.pages.MORE.description");
        SetEventStateMethod.Invoke(dollRoom, new object[] { desc, options });
        await Task.CompletedTask;
    }

    private static EventOption MakeTakeOption(DollRoom dollRoom, RelicModel relic, string doneKey)
    {
        LocString title = relic.Title;
        LocString desc = new LocString("events", "NEW_DOLL_ROOM.pages.TAKE.options.TAKE.description");
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

