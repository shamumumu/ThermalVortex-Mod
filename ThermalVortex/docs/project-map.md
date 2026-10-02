# 武藤游戏项目与文档导航

整理日期：2026-09-07。角色对外名称为“武藤游戏”；程序集、资源前缀、Mod ID 和现有目录仍为 `ThermalVortex`。本文按当前工作区源码说明模块职责，不用旧目录统计或历史清理意见判断文件是否有用。

## 文档入口

| 要查的内容 | 正式资料 |
| --- | --- |
| 卡牌基础、升级、特殊条件与控制台名称 | [卡牌实际效果说明](card-effects-summary.md) |
| 确实需要改文案时的写作约定；关键词、能力、动态参数及显示绑定 | [关键词与能力说明](keywords-and-powers.md) |
| 角色、怪兽场、额外牌组、奖励池、遗物与界面 | [玩法系统说明](gameplay-systems.md) |
| 可筛选的卡牌对照与玩法参数 | [武藤游戏当前实现总表](../balance/武藤游戏当前实现总表.xlsx) |
| 构建、安装及一次性精准探针 | [开发流程](diagnostics.md) |
| 资源加载、素材来源及加工记录 | [资源与素材说明](assets.md) |
| 本项目操作约定 | [工作约定](../../AGENTS.md) |
| 导爆当前模型、造型和动作制作 | [导爆制作入口](../../outputs/detonation_blender/README.md) |
| 角色母模型、施法与原生动作参考 | [角色动作制作入口](../../outputs/yugi_character_rig/README.md) |

按任务需要选择入口，不要求每次修改通读全部资料。工作簿仅有“卡牌对照”和“数值明细”两个工作表，二者记录当前代码。“数值明细”同时收录当前能力与术语说明、动态参数来源及显示绑定，便于与卡牌实现一起核对。动态值使用公式和适用条件，不把预览默认值当作实际固定值。只同步本次改动导致失实的既有正式说明与工作簿字段；准确内容保持原样，保留用户填写的待调整值和修改意见，不从历史讨论表恢复旧规则。

## 初始化与项目入口

- [MainFile.cs](../ThermalVortexCode/MainFile.cs) 的 `Initialize` 注册怪兽场牌堆、初始化奖励池配置、逐类安装 Harmony 补丁、安装查看卡牌输入路由、初始化探针宿主，并开始灰流丽特效预载。奖励池配置初始化和单个补丁失败会记录日志；不能据此假定全部功能仍可用。
- [ThermalVortex.csproj](../ThermalVortex.csproj) 使用 Godot.NET.Sdk 4.5.1、net9.0。普通构建默认不部署、不打 PCK；直接传入 `DeployMod=true` 会被拒绝。
- [ThermalVortex.json](../ThermalVortex.json) 保存当前 Mod ID、武藤游戏名称、简介、版本、依赖和 DLL/PCK 声明。它属于运行时配置，不是外部说明文件。
- [project.godot](../project.godot) 定义资源项目；[export_presets.cfg](../export_presets.cfg) 定义 Godot 导出。显式安装脚本有自己的资源暂存规则，详见开发流程。

## 模块地图

以下路径均在 `ThermalVortexCode` 中。

| 模块 | 当前职责与主要入口 |
| --- | --- |
| [Character](../ThermalVortexCode/Character) | 角色起始配置、卡／遗物／药水池、千年生成牌无色池、额外牌池、千年积木状态相关立绘选择。具体入口为 `ThermalVortex.cs`、`MillenniumPuzzleCharacterArt.cs` 及各 Pool 类。 |
| [Cards](../ThermalVortexCode/Cards) | `ThermalVortexCard`、`MainDeckCard`、`MonsterCard` 和 `ExtraDeckCard` 构成卡牌层级；具体牌实现效果。圈卡、电子继承、千年计数、素材选择、生成牌和数值预览分别有共享逻辑。一个文件可能定义多张牌，不能按文件数量推算卡牌数量。 |
| [MonsterField](../ThermalVortexCode/MonsterField) | 自定义怪兽场牌堆、容量、入离场事件、生命与承伤、素材／离场原因和升级锁；以 `MonsterFieldService`、`MonsterFieldHealthService`、`MonsterFieldEvents` 为主。 |
| [Powers](../ThermalVortexCode/Powers) | 当前能力及状态，含召唤规则、计数、持续效果、敌方行动响应和多来源清理。部分能力用于内部辅助，不是可见的独立奖励。 |
| [Relics](../ThermalVortexCode/Relics) | `ThermalVortexCore` 管理额外牌组、融合流程、奖励、预览与保存；`AncientThermalVortexCore` 和 `LayeredCoil` 实现另外的遗物效果。 |
| [RewardPools](../ThermalVortexCode/RewardPools) | 候选目录、必选牌、合法性校验、配置、上次构筑、预设保存与解析、奖励授予策略。它与角色起始实际牌组是不同概念。 |
| [CardInspection](../ThermalVortexCode/CardInspection) | 查看卡牌请求、输入路由、视图登记、原生查看界面和相关牌悬浮提示。注册对象是可复用视图，不是新增卡牌定义。 |
| [Patches](../ThermalVortexCode/Patches) | 将模组规则接入原生流程：牌池与奖励、怪兽承伤和离场、卡牌预览与升级、UI、敌人行为、角色图像以及具体牌的事件时机。阅读一张牌时需同时检查相关补丁。 |
| [Commands](../ThermalVortexCode/Commands) | `ThermalVortexCommandCompat` 封装与当前游戏命令签名的兼容调用。 |
| [Extensions](../ThermalVortexCode/Extensions) | 资源路径和卡图分辨率作用域。 |
| [Vfx](../ThermalVortexCode/Vfx) | 各牌特效、怪兽投放动作、玩家受击等视觉节点；既有帧图片，也有代码生成的表现。 |
| [Potions](../ThermalVortexCode/Potions) | 药水抽象基类；角色仍有药水池入口，不能仅因没有具体药水而删除。 |
| [Diagnostics](../ThermalVortexCode/Diagnostics) | `ITaskProbe`、上下文、单次会话宿主及结果结构。只在相应会话标记存在时运行探针。 |

`ExtraDeckCardPool` 有实际消费者，例如 `ExtraDeckTinyCardColorPatch` 读取该池；它不是可按旧说明删除的占位文件。千年生成牌的无色池、奖励目录和额外牌组也是不同集合，不能合并解释。

## 开发环境与文件用途

| 文件或目录 | 用途 |
| --- | --- |
| [Directory.Build.props](../Directory.Build.props) | 本机游戏与 Godot 路径。路径值是当前机器配置，不是其他机器的安装要求。 |
| [Sts2PathDiscovery.props](../Sts2PathDiscovery.props) | 推导不同系统的游戏数据与 ModsPath，检查依赖目录。 |
| [NuGet.Config](../../NuGet.Config) 与 `.tools` | 包源、本机 .NET 和依赖缓存。继续开发需要它们，不属于旧文档垃圾。 |
| [scripts](../scripts) | `build-mod.ps1` 是共用构建入口，`install-mod.ps1` 管安装准备及提交，`playtest.ps1` 管单次测试与无探针恢复，`ModLifecycle.psm1` 管同一真实 Mod 目录的互斥与受保护文件操作。构建互斥与真实 Mod 互斥分别管理。 |
| `ThermalVortex/ThermalVortex` | 实际本地化、图片和其他游戏资源；[资源说明](assets.md)解释入口。 |
| `outputs`、`.codex-temp`、`diagnostics` | 混合存放交付物、中间文件、素材和诊断记录，目录名本身不能证明内容可删。 |

## 资料维护

当前代码是“已经实现什么”的依据。文档记录代码能够证明的行为；运行中受外部补丁或宿主影响的部分需如实注明。读取源码不等于进游戏验证，旧 PASS 或旧卡牌统计不能证明当前版本。

**现有文案仍准确，就保持原样；代码发生变化本身不构成修改文案的理由。** 基础与升级卡面、黄字解释、能力和动态／预览提示，以及文档、工作簿内的对应文案都适用。debug、边界条件、特效速度／等待时间和实现对齐既定规则的时序修复，不自动触发文案修改，也不要求新增修复记录。

用户明确要求改文案，或真实玩法、数值、条件、生效回合变化使原文失准时，只改失准的数字、词语或语句，保留其他措辞。已有动态模板能显示新值时，只改参数或绑定。同步资料只更新因此失实的实现说明、数值、升级对照或文案字段；信息不足先保留原文，只有影响玩法决定才询问。写作风格及示例见[文案写作约定](keywords-and-powers.md#中文文案写作约定)。

修改构建／安装／测试工具时更新开发流程中失实的操作说明；更换生产素材时更新受影响的资源入口，保留有用的原图映射。任务的构建、安装和测试范围按工作约定及用户当前要求执行。
