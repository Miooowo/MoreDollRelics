# MoreDollRelics 0.7-fix (2026-07-09)

## Fixed
- 修复「玩偶的邀请函」在涅奥选项与图鉴加载时可能触发池查询异常并导致崩溃的问题。
- 修复旧版 `DollRoomSpawnPatch` / `Act1EliteCounterPatch` 在部分游戏版本因找不到目标方法导致模组初始化失败的问题。
- 修复图鉴先古分页中「涅奥先古遗物」未稳定显示邀请函的问题。

## Changed
- 邀请函补充 `EventRelicPool` 注册，兼容动态描述与悬浮提示的池查询路径。
- 涅奥邀请函选项改为安全 HoverTip 构造流程，避免触发不兼容的描述解析路径。
