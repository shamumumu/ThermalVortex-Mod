# 武藤游戏卡牌当前实现

本页按当前工作树中的卡牌类、能力、召唤流程和补丁重新整理，共 **119 张**。名称取当前中文资源，实际效果、条件与数值以代码结算为准；简短卡面省略的条件在此保留。核对日期：2026-09-07。

配套资料：[玩法系统](gameplay-systems.md)、[关键词与能力](keywords-and-powers.md)、[项目入口](project-map.md)。唯一正式 Excel 为 [武藤游戏当前实现总表.xlsx](../balance/武藤游戏当前实现总表.xlsx)，两张工作表与本页使用同一份逐卡实现记录。


卡面文本补齐日期：2026-09-08。每张牌新增基础与升级卡面文本，采用当前中文资源原句，代入对应升级等级的数值，并包含游戏自动添加的原生关键词。颜色标签已去除，原句与换行保留。

卡面文本按无战斗、无临时效果、无附魔、未复制的新卡状态整理；依赖目标或素材的预览保留资源中的公式分支，不编造实战数值。额外牌组“本场剩余”属于实时状态，未写固定值。五种封印部件及两种翼神龙生成形态明确标注“不可升级”。下方实际效果与条件继续说明卡面省略的结算细节。

卡面显示与解释绑定同步：2026-09-08。本次按完整词义为规则词、原生词和引用卡名的每次出现补齐标黄，并补齐卡旁文字解释或卡片预览；电子龙自身的同名自指保持普通文字。原生说明绑定只提供说明，不赋予消耗等真实玩法关键词。素材条件新增基础电子龙预览，既有生成／变身预览继续按原绑定匹配升级。本页去除颜色标签后的基础／升级卡文、效果条件和数值沿用原记录；显示与绑定细节见[关键词与能力](keywords-and-powers.md)。

千年体系重构同步：2026-09-10。五部件与相关支援牌采用本轮定稿；完整共同规则见[千年体系](gameplay-systems.md#千年部件与循环)。

中文文案整理同步：2026-09-13。同步本轮确认的卡面句式、黄字短定义和能力说明；右腿两处“千年融合”均标黄。“金币换部件”“千年奖励怪兽”改为卡面直接表述，千年计数解释显示公式，并在战斗中附加目前千年计数。卡面省略的重复选择、快照时机与继承限制继续保留在实现补充。后续新增／修改文案遵循[中文文案写作约定](keywords-and-powers.md#中文文案写作约定)。

## 读取方式

- 基础指 0 级；升级通常指 1 级。电磁圈可升至 2 级，升级栏同时列出 1 级与 2 级。
- 费用为卡牌自身实际能量费用。额外牌组卡的能量费用为 0；其角标显示的素材数与召唤限制另外列明。战斗中的临时费用变化由对应效果决定。
- 生命列表示卡牌自身的生命或生成/召唤公式；非怪兽写“不适用”。当前生命、所选素材、目标历史伤害、触发次数等动态量只列计算规则。
- “固有关键词”只列代码声明的原生关键词；解释性黄色词和能力、预览绑定在关键词文档中逐项记录。
- 主牌奖励候选表示可供奖励池配置选择，不代表本局必定纳入奖励池。奖励候选、固定起始与生成专用的完整过滤规则见玩法系统。

## 卡牌目录与内部名称

控制台使用下表中的完整 `THERMALVORTEX-…` ID，例如 `card THERMALVORTEX-INTERNAL_COMBUSTION`。命令解析由游戏原生命令负责；本项目对候选排序进行补丁处理。额外牌组卡即使取得实体，也仍受核心的专用召唤授权与素材检查约束。

| 名称 | 内部名称 | 所属牌池 | 类型 | 稀有度 |
|---|---|---|---|---|
| 防御 | THERMALVORTEX-DEFEND | 固定起始主牌 | 技能 | 基础 |
| α号电圈 | THERMALVORTEX-INTERNAL_COMBUSTION | 固定起始主牌 | 攻击 | 罕见 |
| α号磁圈 | THERMALVORTEX-SECTION_POLE | 固定起始主牌 | 技能 | 普通 |
| 打击 | THERMALVORTEX-STRIKE | 固定起始主牌 | 攻击 | 基础 |
| 融合 | THERMALVORTEX-XYZ_SUMMON | 固定起始主牌 | 技能 | 基础 |
| 爱丽丝梦游仙境 | THERMALVORTEX-ALICE_IN_WONDERLAND | 主牌奖励可选候选 | 能力 | 稀有 |
| 灰流丽 | THERMALVORTEX-ASH_BLOSSOM | 主牌奖励可选候选 | 技能 | 罕见 |
| 觉醒的千年原人 | THERMALVORTEX-AWAKENED_MILLENNIUM_PRIMITIVE | 主牌奖励可选候选 | 技能 | 罕见 |
| 青眼白龙 | THERMALVORTEX-BLUE_EYES_WHITE_DRAGON | 主牌奖励可选候选 | 攻击 | 罕见 |
| 手札抹杀 | THERMALVORTEX-CARD_DESTRUCTION | 主牌奖励可选候选 | 技能 | 稀有 |
| 混沌幻影 | THERMALVORTEX-CHAOS_PHANTOM | 主牌奖励可选候选 | 技能（复制后随来源） | 稀有 |
| 中华锅 | THERMALVORTEX-CHINESE_WOK | 主牌奖励可选候选 | 技能 | 稀有 |
| 死之卡组破坏病毒 | THERMALVORTEX-CRUSH_CARD_VIRUS | 主牌奖励可选候选 | 技能 | 稀有 |
| 电子灯塔 | THERMALVORTEX-CYBER_BEACON | 主牌奖励可选候选 | 能力 | 罕见 |
| 电子龙核 | THERMALVORTEX-CYBER_DRAGON_CORE | 主牌奖励可选候选 | 技能 | 普通 |
| 电子龙芯 | THERMALVORTEX-CYBER_DRAGON_HERZ | 主牌奖励可选候选 | 技能 | 普通 |
| 电子紧急呼救 | THERMALVORTEX-CYBER_EMERGENCY | 主牌奖励可选候选 | 技能 | 普通 |
| 电子次代龙 | THERMALVORTEX-CYBER_NEXT_DRAGON | 主牌奖励可选候选 | 攻击 | 罕见 |
| 电子修理工厂 | THERMALVORTEX-CYBER_REPAIR_PLANT | 主牌奖励可选候选 | 技能 | 罕见 |
| 电子革命系统 | THERMALVORTEX-CYBER_REVOLUTION_SYSTEM | 主牌奖励可选候选 | 能力 | 罕见 |
| 电子废品站 | THERMALVORTEX-CYBER_SCRAP_YARD | 主牌奖励可选候选 | 能力 | 罕见 |
| 虫龙相生 | THERMALVORTEX-CYBER_SYMBIOSIS | 主牌奖励可选候选 | 技能 | 稀有 |
| 力量焊接 | THERMALVORTEX-CYBER_WELDING | 主牌奖励可选候选 | 技能 | 罕见 |
| 电子负载融合 | THERMALVORTEX-CYBERLOAD_FUSION | 主牌奖励可选候选 | 技能 | 稀有 |
| 黑暗电子世界 | THERMALVORTEX-DARK_CYBER_WORLD | 主牌奖励可选候选 | 能力 | 稀有 |
| 黑暗决斗 | THERMALVORTEX-DARK_DUEL | 主牌奖励可选候选 | 能力 | 稀有 |
| 黑魔导 | THERMALVORTEX-DARK_MAGICIAN | 主牌奖励可选候选 | 技能 | 罕见 |
| 暗黑界的取引 | THERMALVORTEX-DARK_WORLD_DEALINGS | 主牌奖励可选候选 | 技能 | 普通 |
| 次元扩张 | THERMALVORTEX-DIMENSIONAL_EXPANSION | 主牌奖励可选候选 | 能力 | 罕见 |
| 教导的惩罚 | THERMALVORTEX-DOGMATIKA_PUNISHMENT | 主牌奖励可选候选 | 技能 | 罕见 |
| 旧神 努茨 | THERMALVORTEX-ELDER_ENTITY_NTSS | 主牌奖励可选候选 | 技能 | 罕见 |
| β号磁圈 | THERMALVORTEX-ELECTRODE | 主牌奖励可选候选 | 技能 | 稀有 |
| β号电圈 | THERMALVORTEX-EXTERNAL_COMBUSTION | 主牌奖励可选候选 | 攻击 | 罕见 |
| 禁忌的圣杯 | THERMALVORTEX-FORBIDDEN_CHALICE | 主牌奖励可选候选 | 技能 | 罕见 |
| 禁忌的一滴 | THERMALVORTEX-FORBIDDEN_DROPLET | 主牌奖励可选候选 | 技能 | 稀有 |
| 熔炉启动 | THERMALVORTEX-FURNACE_STARTUP | 主牌奖励可选候选 | 能力 | 稀有 |
| 融合命运 | THERMALVORTEX-FUSION_DESTINY | 主牌奖励可选候选 | 能力 | 罕见 |
| 融合之门 | THERMALVORTEX-FUSION_GATE | 主牌奖励可选候选 | 能力 | 罕见 |
| 融合的事前准备 | THERMALVORTEX-FUSION_PREPARATION | 主牌奖励可选候选 | 技能 | 普通 |
| 未来融合 | THERMALVORTEX-FUTURE_FUSION | 主牌奖励可选候选 | 技能 | 罕见 |
| 封印的黄金柜 | THERMALVORTEX-GOLD_SARCOPHAGUS | 主牌奖励可选候选 | 技能 | 罕见 |
| 天使的施舍 | THERMALVORTEX-GRACEFUL_CHARITY | 主牌奖励可选候选 | 技能 | 罕见 |
| 红莲魔兽 | THERMALVORTEX-GREN_MAJU_DA_EIZA | 主牌奖励可选候选 | 攻击 | 罕见 |
| 英雄到来 | THERMALVORTEX-HERO_ARRIVAL | 主牌奖励可选候选 | 技能 | 罕见 |
| 通往活路的希望 | THERMALVORTEX-HOPE_FOR_ESCAPE | 主牌奖励可选候选 | 技能 | 稀有 |
| 无限泡影 | THERMALVORTEX-INFINITE_IMPERMANENCE | 主牌奖励可选候选 | 技能 | 稀有 |
| 限制解除 | THERMALVORTEX-LIMITER_REMOVAL | 主牌奖励可选候选 | 技能 | 稀有 |
| 宏观宇宙 | THERMALVORTEX-MACRO_COSMOS | 主牌奖励可选候选 | 能力 | 稀有 |
| 魔术礼帽 | THERMALVORTEX-MAGICAL_HATS | 主牌奖励可选候选 | 技能 | 罕见 |
| 增殖的G | THERMALVORTEX-MAXX_C | 主牌奖励可选候选 | 能力 | 稀有 |
| 千年的伏兵 | THERMALVORTEX-MILLENNIUM_CARRIER | 主牌奖励可选候选 | 攻击 | 普通 |
| 千年契约书 | THERMALVORTEX-MILLENNIUM_CONTRACT_BOOK | 主牌奖励可选候选 | 能力 | 罕见 |
| 千年十字 | THERMALVORTEX-MILLENNIUM_CROSS | 主牌奖励可选候选 | 技能 | 稀有 |
| 千年守墓人 | THERMALVORTEX-MILLENNIUM_GRAVEKEEPER | 主牌奖励可选候选 | 技能 | 普通 |
| 千年供奉 | THERMALVORTEX-MILLENNIUM_OFFERING | 主牌奖励可选候选 | 技能 | 普通 |
| 千年的伙伴 | THERMALVORTEX-MILLENNIUM_PARTNER | 主牌奖励可选候选 | 能力 | 稀有 |
| 千年王朝之盾 | THERMALVORTEX-MILLENNIUM_SHIELD | 主牌奖励可选候选 | 技能 | 普通 |
| 千年沉睡石板 | THERMALVORTEX-MILLENNIUM_SLEEPING_TABLET | 主牌奖励可选候选 | 技能 | 罕见 |
| 千年的石板 | THERMALVORTEX-MILLENNIUM_TABLET | 主牌奖励可选候选 | 技能 | 普通 |
| 千年神殿 | THERMALVORTEX-MILLENNIUM_TEMPLE | 主牌奖励可选候选 | 能力 | 稀有 |
| 千年宝物守护巨像 | THERMALVORTEX-MILLENNIUM_TREASURE_GOLEM | 主牌奖励可选候选 | 技能 | 罕见 |
| 奇迹融合 | THERMALVORTEX-MIRACLE_FUSION | 主牌奖励可选候选 | 技能 | 罕见 |
| 神圣防护罩 反射镜力 | THERMALVORTEX-MIRROR_FORCE | 主牌奖励可选候选 | 技能 | 稀有 |
| 死者苏生 | THERMALVORTEX-MONSTER_REBORN | 主牌奖励可选候选 | 技能 | 稀有 |
| 王车易位 | THERMALVORTEX-MONSTER_SWAP | 主牌奖励可选候选 | 技能 | 普通 |
| 多多密友 呼哇罗丝 | THERMALVORTEX-MULCHARMY_FUWALOS | 主牌奖励可选候选 | 能力 | 罕见 |
| 多多密友 喵喵露丝 | THERMALVORTEX-MULCHARMY_MEOWLS | 主牌奖励可选候选 | 能力 | 罕见 |
| 多多密友 噗噜利亚 | THERMALVORTEX-MULCHARMY_PURULIA | 主牌奖励可选候选 | 能力 | 罕见 |
| 过热电圈 | THERMALVORTEX-OVERHEATED_COIL | 主牌奖励可选候选 | 攻击 | 普通 |
| 过载抽取 | THERMALVORTEX-OVERLOAD_DRAW | 主牌奖励可选候选 | 技能 | 罕见 |
| 超载怪兽位 | THERMALVORTEX-OVERLOADED_SUMMON_SLOT | 主牌奖励可选候选 | 技能 | 罕见 |
| 大欲之壶 | THERMALVORTEX-POT_OF_AVARICE | 主牌奖励可选候选 | 技能 | 稀有 |
| 强欲而贪婪之壶 | THERMALVORTEX-POT_OF_DESIRES | 主牌奖励可选候选 | 技能 | 稀有 |
| 强欲而金满之壶 | THERMALVORTEX-POT_OF_EXTRAVAGANCE | 主牌奖励可选候选 | 技能 | 稀有 |
| 强欲之壶 | THERMALVORTEX-POT_OF_GREED | 主牌奖励可选候选 | 技能 | 稀有 |
| 保护核心 | THERMALVORTEX-PROTECT_CORE | 主牌奖励可选候选 | 技能 | 普通 |
| 生死与共 | THERMALVORTEX-SHARED_FATE | 主牌奖励可选候选 | 能力 | 稀有 |
| 地盘沉下 | THERMALVORTEX-SINKING_LAND | 主牌奖励可选候选 | 能力 | 罕见 |
| 神之宣告 | THERMALVORTEX-SOLEMN_JUDGMENT | 主牌奖励可选候选 | 技能 | 稀有 |
| 神之通告 | THERMALVORTEX-SOLEMN_STRIKE | 主牌奖励可选候选 | 技能 | 罕见 |
| 神之警告 | THERMALVORTEX-SOLEMN_WARNING | 主牌奖励可选候选 | 技能 | 罕见 |
| 备用装甲 | THERMALVORTEX-SPARE_ARMOR | 主牌奖励可选候选 | 技能 | 罕见 |
| 稳压磁圈 | THERMALVORTEX-STABILIZED_MAGNETIC_COIL | 主牌奖励可选候选 | 技能 | 普通 |
| 星球改造 | THERMALVORTEX-TERRAFORMING | 主牌奖励可选候选 | 技能 | 罕见 |
| 激流葬 | THERMALVORTEX-TORRENTIAL_TRIBUTE | 主牌奖励可选候选 | 技能 | 罕见 |
| 玩具盒 | THERMALVORTEX-TOY_BOX | 主牌奖励可选候选 | 能力 | 稀有 |
| 落穴 | THERMALVORTEX-TRAP_HOLE | 主牌奖励可选候选 | 技能 | 普通 |
| 成金哥布林 | THERMALVORTEX-UPSTART_GOBLIN | 主牌奖励可选候选 | 技能 | 罕见 |
| 电磁圈 | THERMALVORTEX-ELECTROMAGNETIC_CIRCLE | 先古合成专属 | 攻击 | 先古 |
| 原初之神 法拉 | THERMALVORTEX-PRIMAL_GOD_FARA | 先古专属（DustyTome）；普通奖励候选排除 | 技能 | 先古 |
| 独步千年的大义贼 | THERMALVORTEX-MILLENNIUM_GRAND_THIEF | 额外牌组奖励可选候选 | 技能 | 基础 |
| 千年召唤神·艾库佐尼亚 | THERMALVORTEX-EXODIA_SUMMONER | 额外牌组奖励可选候选 | 技能 | 基础 |
| 千年宝库的密钥 | THERMALVORTEX-MILLENNIUM_MASTER_KEY | 额外牌组奖励可选候选 | 技能 | 基础 |
| 千年邪神·艾库佐尼亚 | THERMALVORTEX-EVIL_EXODIA | 额外牌组奖励可选候选 | 攻击 | 基础 |
| 千年守护神·艾库佐尼亚 | THERMALVORTEX-EXODIA_GUARDIAN | 额外牌组奖励可选候选 | 技能 | 基础 |
| 励辉重启骑士 | THERMALVORTEX-BRILLIANT_REBOOT_KNIGHT | 额外牌组奖励可选候选 | 攻击 | 基础 |
| 电子嵌合龙 | THERMALVORTEX-CHIMERATECH_OVERDRAGON | 额外牌组奖励可选候选 | 攻击 | 稀有 |
| 断路护符兽 | THERMALVORTEX-CIRCUIT_TALISMAN_BEAST | 额外牌组奖励可选候选 | 技能 | 基础 |
| 电子龙无限 | THERMALVORTEX-CYBER_DRAGON_INFINITY | 额外牌组奖励可选候选 | 攻击 | 稀有 |
| 电子终结龙 | THERMALVORTEX-CYBER_END_DRAGON | 额外牌组奖励可选候选 | 技能 | 稀有 |
| 导爆 | THERMALVORTEX-DETONATION | 固定起始额外牌 | 攻击 | 基础 |
| 天霆炉神 | THERMALVORTEX-DIVINE_ARSENAL_FURNACE_GOD | 额外牌组奖励可选候选 | 攻击 | 基础 |
| 休眠磁场兽 | THERMALVORTEX-DORMANT_MAGNETIC_FIELD_BEAST | 额外牌组奖励可选候选 | 技能 | 基础 |
| 全装甲雷枪 | THERMALVORTEX-FULL_ARMOR_THUNDER_LANCE | 额外牌组奖励可选候选 | 技能 | 基础 |
| 纳祭魔 | THERMALVORTEX-RELINQUISHED | 额外牌组奖励可选候选 | 技能 | 稀有 |
| 涡旋 | THERMALVORTEX-VORTEX | 额外牌组奖励可选候选 | 技能 | 稀有 |
| 拉之翼神龙-球形体 | THERMALVORTEX-WINGED_DRAGON_OF_RA_SPHERE_MODE | 额外牌组奖励可选候选 | 技能 | 稀有 |
| 电子龙 | THERMALVORTEX-CYBER_DRAGON | 生成专用；效果生成 | 攻击 | 普通 |
| 电子虫 | THERMALVORTEX-CYBER_LARVA | 生成专用；效果生成 | 状态 | 状态 |
| 被封印千年的左手 | THERMALVORTEX-EXODIA_LEFT_ARM | 生成专用；效果生成 | 技能 | 普通 |
| 被封印千年的左腿 | THERMALVORTEX-EXODIA_LEFT_LEG | 生成专用；效果生成 | 技能 | 普通 |
| 被封印千年的右手 | THERMALVORTEX-EXODIA_RIGHT_ARM | 生成专用；效果生成 | 技能 | 普通 |
| 被封印千年的右腿 | THERMALVORTEX-EXODIA_RIGHT_LEG | 生成专用；效果生成 | 技能 | 普通 |
| 被封印千年的躯干 | THERMALVORTEX-EXODIA_TORSO | 生成专用；效果生成 | 技能 | 罕见 |
| 幻之召唤神 艾克佐迪亚 | THERMALVORTEX-PHANTOM_SUMMONING_GOD_EXODIA | 生成专用；（千年十字） | 技能 | 衍生 |
| 炉渣 | THERMALVORTEX-SLAG | 生成专用；（过载抽取） | 状态 | 状态 |
| 玩具怪兽 | THERMALVORTEX-TOY_BOX_TOKEN | 生成专用；（玩具盒） | 技能 | 衍生 |
| 拉之翼神龙 | THERMALVORTEX-WINGED_DRAGON_OF_RA | 生成专用；（不死鸟形态转换） | 攻击 | 稀有 |
| 拉之翼神龙-不死鸟 | THERMALVORTEX-WINGED_DRAGON_OF_RA_PHOENIX | 生成专用；（球形体形态转换） | 技能 | 稀有 |

## 固定起始牌（5 张）

### 防御 · `Defend`

内部名称：`THERMALVORTEX-DEFEND`。所属牌池：固定起始主牌；类型：技能；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 获得5点格挡。  

**升级卡面文本：**

> 获得8点格挡。  

卡文来源：[当前中文资源:7](<../ThermalVortex/localization/zhs/cards.json:7>)，键 `THERMALVORTEX-DEFEND.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 获得5点格挡。

**升级实际效果：** 获得8点格挡。

**必要条件与实现补充：** 固定起始主牌，带防御标签，不进入后续主牌奖励目录。

**实现位置：** [Defend.cs:9](<../ThermalVortexCode/Cards/Defend.cs:9>)；[Defend.cs:15](<../ThermalVortexCode/Cards/Defend.cs:15>)；[RewardPoolCatalog.cs:48](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:48>)

### α号电圈 · `InternalCombustion`

内部名称：`THERMALVORTEX-INTERNAL_COMBUSTION`。所属牌池：固定起始主牌；类型：攻击；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 2 | 5 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：2。  
> 造成4点伤害。  
> 可从手牌或抽牌堆召唤1只磁圈。  

**升级卡面文本：**

> 怪兽。生命：5。  
> 造成6点伤害。  
> 可从手牌或抽牌堆召唤1只磁圈。  

卡文来源：[当前中文资源:9](<../ThermalVortex/localization/zhs/cards.json:9>)，键 `THERMALVORTEX-INTERNAL_COMBUSTION.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 造成4点攻击伤害并召唤2生命电圈；可从手牌或抽牌堆选择1只合法磁圈免费立即打出并召唤。

**升级实际效果：** 造成6点攻击伤害并召唤5生命电圈；可从手牌或抽牌堆选择1只合法磁圈免费立即打出并召唤。

**必要条件与实现补充：** 固定起始主牌，不进入普通奖励候选。连锁可空选跳过，保留所选牌原升级状态；磁圈包括双身份电磁圈，受怪兽位及其他可打出条件限制。

**实现位置：** [InternalCombustion.cs:9](<../ThermalVortexCode/Cards/InternalCombustion.cs:9>)；[InternalCombustion.cs:18](<../ThermalVortexCode/Cards/InternalCombustion.cs:18>)；[PoleMonsterCard.cs:24](<../ThermalVortexCode/Cards/PoleMonsterCard.cs:24>)；[RewardPoolCatalog.cs:48](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:48>)

### α号磁圈 · `SectionPole`

内部名称：`THERMALVORTEX-SECTION_POLE`。所属牌池：固定起始主牌；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 2 | 5 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：2。  
> 获得3点格挡。  
> 可从手牌或抽牌堆召唤1只电圈。  

**升级卡面文本：**

> 怪兽。生命：5。  
> 获得6点格挡。  
> 可从手牌或抽牌堆召唤1只电圈。  

卡文来源：[当前中文资源:13](<../ThermalVortex/localization/zhs/cards.json:13>)，键 `THERMALVORTEX-SECTION_POLE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 获得3格挡，召唤自身；可以从手牌或抽牌堆选1张其他电圈类怪兽立即特殊打出。

**升级实际效果：** 获得6格挡，召唤自身；可以从手牌或抽牌堆选1张其他电圈类怪兽立即特殊打出。

**必要条件与实现补充：** 固定起始牌之一；联动可放弃。候选须满足怪兽位和各自可立即打出的条件；攻击目标重新选择，不能沿用外层目标。联动是完整打出，可能引发后续卡牌自己的联动。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池且属于奖励池构筑必选项，不占可选奖励名额。

**实现位置：** [SectionPole.cs:8](<../ThermalVortexCode/Cards/SectionPole.cs:8>)；[PoleMonsterCard.cs:40](<../ThermalVortexCode/Cards/PoleMonsterCard.cs:40>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 打击 · `Strike`

内部名称：`THERMALVORTEX-STRIKE`。所属牌池：固定起始主牌；类型：攻击；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 造成6点伤害。  

**升级卡面文本：**

> 造成9点伤害。  

卡文来源：[当前中文资源:5](<../ThermalVortex/localization/zhs/cards.json:5>)，键 `THERMALVORTEX-STRIKE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 对1名敌人造成6点伤害。

**升级实际效果：** 对1名敌人造成9点伤害。

**必要条件与实现补充：** 固定起始牌4张；带 Strike 卡牌标签。 登记在角色主牌池且属于奖励池构筑必选项，不占可选奖励名额。

**实现位置：** [Strike.cs:10](<../ThermalVortexCode/Cards/Strike.cs:10>)；[ThermalVortex.cs:27](<../ThermalVortexCode/Character/ThermalVortex.cs:27>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 融合 · `XyzSummon`

内部名称：`THERMALVORTEX-XYZ_SUMMON`。所属牌池：固定起始主牌；类型：技能；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 以场上的怪兽作为融合素材，融合召唤1只融合怪兽。  

**升级卡面文本：**

> 以场上或手牌的怪兽作为融合素材，融合召唤1只融合怪兽。  

卡文来源：[当前中文资源:85](<../ThermalVortex/localization/zhs/cards.json:85>)，键 `THERMALVORTEX-XYZ_SUMMON.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择额外牌组1只可召唤怪兽，以合法场上怪兽作为素材送往弃牌堆，召唤并结算所选怪兽。

**升级实际效果：** 选择额外牌组1只可召唤怪兽，以合法场上及手牌怪兽作为素材送往弃牌堆，召唤并结算所选怪兽。

**必要条件与实现补充：** 须持有角色核心遗物且额外牌组有满足条件的怪兽，材料必须满足目标专属要求，移走材料后有合法位置。打出融合后即预判素材、腾位后的怪兽位、打出限制与有效目标；无可召唤目标时提示“无法融合召唤。”并结束，不打开素材选择、不支付素材、不提取额外怪兽，进入素材选择前计划失效也提示退出。满场仍允许用合法场上素材腾位；需要玩家选择时提示选择必须的场上素材，唯一选项等可自动确定时不额外弹窗。额外怪兽不额外支付能量。素材带消耗时改送消耗堆，素材离场触发照常结算。召唤动画前及执行召唤前复查怪兽位、打出条件与有效目标，已知无法召唤时直接停止；同步选择目标后暂存于结算区（Play），等待实际入场。素材回调已结算的效果不回滚，保留未成功留场的额外条目回收。专属素材要求优先：纳祭魔必须场上恰好1只及爪牙目标，球形体须满场全部素材等；升级只扩充普通素材来源，不放松这些要求。 登记在角色主牌池且属于奖励池构筑必选项，不占可选奖励名额。

**实现位置：** [XyzSummon.cs:11](<../ThermalVortexCode/Cards/XyzSummon.cs:11>)；[ThermalVortexCore.cs:2899](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2899>)；[ThermalVortexCore.cs:2924](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2924>)；[ThermalVortexCore.cs:3123](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3123>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)


## 主牌奖励候选（83 张）

### 爱丽丝梦游仙境 · `AliceInWonderland`

内部名称：`THERMALVORTEX-ALICE_IN_WONDERLAND`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 虚无 | 虚无 |

**基础卡面文本：**

> 虚无。  
> 你的回合内，怪兽牌被消耗时，失去3点生命并召唤它。每张牌每回合每层限1次。  

**升级卡面文本：**

> 虚无。  
> 你的回合内，怪兽牌被消耗时，失去3点生命并召唤它。每张牌每回合每层限1次。  

卡文来源：[当前中文资源:137](<../ThermalVortex/localization/zhs/cards.json:137>)，键 `THERMALVORTEX-ALICE_IN_WONDERLAND.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本场战斗，在你自己的回合内，每当本方怪兽牌被消耗，失去3点生命，然后尝试免费重新召唤该实体；每张实体每个自己的回合的尝试上限等于当前能力层数。

**升级实际效果：** 费用2。本场战斗，在你自己的回合内，每当本方怪兽牌被消耗，失去3点生命，然后尝试免费重新召唤该实体；每张实体每个自己的回合的尝试上限等于当前能力层数。

**必要条件与实现补充：** 仅在拥有者自己的回合内触发，包含开回合、结束阶段与额外回合；敌方回合及他人的专属额外回合不触发、不扣血、不占次数，也不延期补发。重复使用叠加层数，每实体每回合的尝试上限等于当前层数。先计次再支付3生命；已经开始的尝试即使因死亡、满场或目标失效失败，仍扣血计次。异步过程中复查同一战斗、拥有者回合、卡牌及能力有效性。允许额外怪兽，不重复支付融合素材；球形体除外。原实体保留素材数、升级、吞噬成长及本回合已用次数；复制或新生成实体额度独立。按拥有者实际回合编号惰性重置，不依赖能力回调顺序。手牌已满时不把复活牌移入弃牌堆，原实体留在消耗堆；本次已支付生命和次数不退还。

**实现位置：** [AliceInWonderland.cs:10](<../ThermalVortexCode/Cards/AliceInWonderland.cs:10>)；[AliceInWonderland.cs:16](<../ThermalVortexCode/Cards/AliceInWonderland.cs:16>)；[AliceInWonderlandPower.cs:33](<../ThermalVortexCode/Powers/AliceInWonderlandPower.cs:33>)；[ThermalVortexCard.cs:36](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:36>)；[AliceMonsterCloneQuotaPatch.cs:1](<../ThermalVortexCode/Patches/AliceMonsterCloneQuotaPatch.cs:1>)

### 灰流丽 · `AshBlossom`

内部名称：`THERMALVORTEX-ASH_BLOSSOM`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 3 | 3 |
| 固有关键词 | 消耗 | 消耗、保留 |

**基础卡面文本：**

> 怪兽。生命：3。  
> 使1名敌人的下次非攻击行为无效。  
> 消耗。  

**升级卡面文本：**

> 保留。  
> 怪兽。生命：3。  
> 使1名敌人的下次非攻击行为无效。  
> 消耗。  

卡文来源：[当前中文资源:21](<../ThermalVortex/localization/zhs/cards.json:21>)，键 `THERMALVORTEX-ASH_BLOSSOM.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤3生命怪兽，选择1名敌人施加1层灰流丽。该敌人下次在敌方回合触发受拦截的非攻击效果时，该敌方回合其后续受支持的非攻击效果一并无效，攻击照常。

**升级实际效果：** 召唤3生命怪兽，选择1名敌人施加1层灰流丽。该敌人下次在敌方回合触发受拦截的非攻击效果时，该敌方回合其后续受支持的非攻击效果一并无效，攻击照常。

**必要条件与实现补充：** 只有入场结算成功才施加。选定目标离场后不自动改目标。每个触发的敌方回合末消耗1层；未触发保留，来源怪兽离场不撤销。拦截包括能力变化、状态牌、格挡、治疗、召唤、逃跑、直接生命变化及部分卡牌/资源变化；按补丁识别的敌方来源生效。敌方行动期间触发的我方响应按响应卡牌或能力的实际来源执行；能力自身清理正常完成。原生效果回调与怪兽场监听分别建立来源边界，避免把抽牌响应或离场解绑误判为敌方效果。原生临时力量、敏捷与集中到期时，其移除与精确对应的属性还原正常执行，不触发灰流丽。

**结算补充：** 对偷牌敌人的无效在原生偷牌候选读取处生效，保留同一招式的攻击；没有可偷牌时不消耗灰流丽。神之通告已阻止偷牌时不再额外消费灰流丽。玩家不死鸟复活按卡牌自身来源结算，不继承敌方攻击的无效作用域。

**实现位置：** [AshBlossom.cs:14](<../ThermalVortexCode/Cards/AshBlossom.cs:14>)；[AshBlossom.cs:25](<../ThermalVortexCode/Cards/AshBlossom.cs:25>)；[AshBlossomPower.cs:21](<../ThermalVortexCode/Powers/AshBlossomPower.cs:21>)；[InfiniteImpermanenceMonsterMovePatch.cs:268](<../ThermalVortexCode/Patches/InfiniteImpermanenceMonsterMovePatch.cs:268>)；[AshBlossomAdditionalEffectPatch.cs:14](<../ThermalVortexCode/Patches/AshBlossomAdditionalEffectPatch.cs:14>)；[SolemnStrikeNonDamagePatch.cs:1](<../ThermalVortexCode/Patches/SolemnStrikeNonDamagePatch.cs:1>)；[AshBlossomEnemyTurnPatch.cs:1](<../ThermalVortexCode/Patches/AshBlossomEnemyTurnPatch.cs:1>)

### 觉醒的千年原人 · `AwakenedMillenniumPrimitive`

内部名称：`THERMALVORTEX-AWAKENED_MILLENNIUM_PRIMITIVE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：5。  
> 在场时，回合开始给予所有敌人1层易伤。  

**升级卡面文本：**

> 怪兽。生命：5。  
> 在场时，回合开始给予所有敌人2层易伤。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-AWAKENED_MILLENNIUM_PRIMITIVE.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 召唤5生命千年怪兽。后续玩家回合开始时，每只在场原人给予所有敌人1层易伤。

**升级实际效果：** 召唤5生命千年怪兽。后续玩家回合开始时，每只在场原人给予所有敌人2层易伤。

**必要条件与实现补充：** 入场安装维持能力；同名在场实体贡献相加，来源不在场便不贡献。自身能力界面为单一容器。维持效果可作为吞噬继承效果；混沌完整复制计入对应机制。

**实现位置：** [MillenniumMonsters.cs:79](<../ThermalVortexCode/Cards/MillenniumMonsters.cs:79>)；[MillenniumPowers.cs:109](<../ThermalVortexCode/Powers/MillenniumPowers.cs:109>)；[MillenniumSeries.cs:24](<../ThermalVortexCode/Cards/MillenniumSeries.cs:24>)；[CyberSeries.cs:870](<../ThermalVortexCode/Cards/CyberSeries.cs:870>)

### 青眼白龙 · `BlueEyesWhiteDragon`

内部名称：`THERMALVORTEX-BLUE_EYES_WHITE_DRAGON`。所属牌池：主牌奖励可选候选；类型：攻击；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 6 | 6 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：6。  
> 造成8点伤害。  

**升级卡面文本：**

> 怪兽。生命：6。  
> 造成14点伤害。  

卡文来源：[当前中文资源:31](<../ThermalVortex/localization/zhs/cards.json:31>)，键 `THERMALVORTEX-BLUE_EYES_WHITE_DRAGON.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤6生命怪兽，对选择的敌人造成8点攻击伤害。

**升级实际效果：** 召唤6生命怪兽，对选择的敌人造成14点攻击伤害。

**必要条件与实现补充：** 生命不随升级变化；普通手动召唤遵守怪兽位规则。

**实现位置：** [BlueEyesWhiteDragon.cs:9](<../ThermalVortexCode/Cards/BlueEyesWhiteDragon.cs:9>)；[BlueEyesWhiteDragon.cs:18](<../ThermalVortexCode/Cards/BlueEyesWhiteDragon.cs:18>)

### 手札抹杀 · `CardDestruction`

内部名称：`THERMALVORTEX-CARD_DESTRUCTION`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗、固有 |

**基础卡面文本：**

> 丢弃所有手牌，抽等量的牌。  
> 消耗。  

**升级卡面文本：**

> 固有。  
> 丢弃所有手牌，抽等量的牌。  
> 消耗。  

卡文来源：[当前中文资源:122](<../ThermalVortex/localization/zhs/cards.json:122>)，键 `THERMALVORTEX-CARD_DESTRUCTION.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 弃掉除本牌外的全部手牌，再抽取弃牌前手牌快照数量的牌。

**升级实际效果：** 弃掉除本牌外的全部手牌，再抽取弃牌前手牌快照数量的牌。

**必要条件与实现补充：** 无其他手牌时不弃不抽。抽牌数取弃牌前数量，不按各类弃牌触发后的手牌或实际弃入数量重算。

**实现位置：** [CardDestruction.cs:9](<../ThermalVortexCode/Cards/CardDestruction.cs:9>)；[CardDestruction.cs:15](<../ThermalVortexCode/Cards/CardDestruction.cs:15>)

### 混沌幻影 · `ChaosPhantom`

内部名称：`THERMALVORTEX-CHAOS_PHANTOM`。所属牌池：主牌奖励可选候选；类型：技能（复制后随来源）；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 未复制时1；复制后为来源当前有效最大生命（至少1） | 未复制时1；复制后为来源当前有效最大生命（至少1） |
| 固有关键词 | 未复制时无；复制后继承来源当前关键词 | 未复制时无；复制后继承来源当前关键词 |

**基础卡面文本：**

> 怪兽。生命：？。  
> 选择弃牌堆中1张怪兽作为当前怪兽使用。  

**升级卡面文本：**

> 怪兽。生命：？。  
> 选择弃牌堆中1张怪兽作为当前怪兽使用。  

卡文来源：[当前中文资源:250](<../ThermalVortex/localization/zhs/cards.json:250>)，键 `THERMALVORTEX-CHAOS_PHANTOM.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 从弃牌堆选择1只怪兽，复制其名字、当前升级状态的牌型、目标、关键词、有效最大生命、系列身份及打出与生命周期效果，以满生命召唤混沌幻影。自身费用保持3。

**升级实际效果：** 从弃牌堆选择1只怪兽，复制其名字、当前升级状态的牌型、目标、关键词、有效最大生命、系列身份及打出与生命周期效果，以满生命召唤混沌幻影。自身费用保持2。

**必要条件与实现补充：** 弃牌堆来源不移动、不消耗。复制当前/最大生命均取来源有效上限，不照抄剩余生命；包含可追踪的生命增长与吞噬成长。支持复制已有复制状态的混沌；无状态混沌会继续选来源，防止循环。来源效果通过其克隆 OnPlay 调用，继承入场/维持/素材/离场能力；无有效目标或未请求召唤则清除。离场效果结算完成后恢复本体，只清除借来的复制形态、属性及能力；自己实际吞噬获得的生命、攻击、继承效果与来源记录持续保留。再次复制时，当前借用形态与自身成长各计算一次，持续能力统一合并登记。只在场上或场上检视显示复制。

**结算补充：** 完整复制不死鸟时，同一攻击分段实际击毁本体并令玩家致死，可以恢复同一混沌实体及其离场前复制形态。复制生命基础单独保存，自身吞噬成长沿用该实体的实时记录，各计算一次；离场被阻止、已重新入场或不同后续攻击段不使用旧复活资格。

**实现位置：** [ChaosPhantomAndRaCards.cs:23](<../ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:23>)；[ChaosPhantomAndRaCards.cs:52](<../ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:52>)；[ChaosPhantomAndRaCards.cs:278](<../ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:278>)；[ChaosPhantomAndRaCards.cs:436](<../ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:436>)；[ChaosPhantomAndRaCards.cs:568](<../ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:568>)；[ChaosPhantomAndRaCards.cs:674](<../ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:674>)；[MonsterFieldHealthService.cs:96](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:96>)；[MonsterIdentity.cs:1](<../ThermalVortexCode/Cards/MonsterIdentity.cs:1>)

### 中华锅 · `ChineseWok`

内部名称：`THERMALVORTEX-CHINESE_WOK`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 选择1只场上怪兽，恢复等同于其当前生命的生命，并将其送入弃牌堆。  
> 消耗。  

**升级卡面文本：**

> 选择1只场上怪兽，恢复等同于其当前生命的生命，并将其送入弃牌堆。  
> 消耗。  

卡文来源：[当前中文资源:141](<../ThermalVortex/localization/zhs/cards.json:141>)，键 `THERMALVORTEX-CHINESE_WOK.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择自身场上1只有效怪兽作为素材；实际使用成功且原战斗仍有效时，回复等同其使用前当前生命的玩家生命。

**升级实际效果：** 选择自身场上1只有效怪兽作为素材；实际使用成功且原战斗仍有效时，回复等同其使用前当前生命的玩家生命。

**必要条件与实现补充：** 须有自身场上怪兽。选择结束后仍须为自身场上的有效原实体；实际素材支付成功且原战斗仍有效才治疗，合法素材复活不撤销已成功支付的事实。回复量在素材离场前快照；触发原有及继承的素材/离场效果。默认素材进弃牌堆，额外怪兽或带消耗的素材进消耗堆。回复不为正时不治疗。

**实现位置：** [ChineseWok.cs:16](<../ThermalVortexCode/Cards/ChineseWok.cs:16>)；[ChineseWok.cs:26](<../ThermalVortexCode/Cards/ChineseWok.cs:26>)；[MonsterFieldService.cs:569](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:569>)

### 死之卡组破坏病毒 · `CrushCardVirus`

内部名称：`THERMALVORTEX-CRUSH_CARD_VIRUS`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 选择1只场上的怪兽，所有敌人和所选怪兽失去该怪兽最大生命值的生命。  

**升级卡面文本：**

> 选择至少1只场上的怪兽，所有敌人和所选怪兽失去所选怪兽最大生命值总和的生命。  

卡文来源：[当前中文资源:127](<../ThermalVortex/localization/zhs/cards.json:127>)，键 `THERMALVORTEX-CRUSH_CARD_VIRUS.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择1只场上怪兽，快照其最大生命；该怪兽及每名敌人分别失去该数值的生命。

**升级实际效果：** 可选择至少1只、至多全部场上怪兽，改以所选怪兽最大生命总和结算：每只所选怪兽和每名敌人各失去该总和。

**必要条件与实现补充：** 须有场上怪兽；选择后只保留仍在场的互不重复实体。数值先快照，再对怪兽和敌人结算；敌人直接设置生命，不经过格挡。所选怪兽以清场原因离场，不触发素材或战斗破坏专属效果。

**实现位置：** [CrushCardVirus.cs:12](<../ThermalVortexCode/Cards/CrushCardVirus.cs:12>)；[CrushCardVirus.cs:18](<../ThermalVortexCode/Cards/CrushCardVirus.cs:18>)；[CrushCardVirus.cs:55](<../ThermalVortexCode/Cards/CrushCardVirus.cs:55>)；[MonsterFieldHealthService.cs:396](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:396>)；[MonsterFieldEvents.cs:1](<../ThermalVortexCode/MonsterField/MonsterFieldEvents.cs:1>)

### 电子灯塔 · `CyberBeacon`

内部名称：`THERMALVORTEX-CYBER_BEACON`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 将1张电子虫加入弃牌堆。  
> 回合开始时，将1张电子龙洗入抽牌堆。  

**升级卡面文本：**

> 将1张电子虫+加入弃牌堆。  
> 回合开始时，将1张电子龙+洗入抽牌堆。  

卡文来源：[当前中文资源:181](<../ThermalVortex/localization/zhs/cards.json:181>)，键 `THERMALVORTEX-CYBER_BEACON.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 在弃牌堆生成1张电子虫。本场战斗每个后续玩家回合开始，在抽牌堆随机位置生成1张电子龙。

**升级实际效果：** 在弃牌堆生成1张电子虫+。本场战斗每个后续玩家回合开始，在抽牌堆随机位置生成1张电子龙+。

**必要条件与实现补充：** 重复获得不增加每回合数量；一旦打出升级版，该能力以后始终生成升级龙。生成虫计入本战斗虫生成计数。

**实现位置：** [CyberBeacon.cs:11](<../ThermalVortexCode/Cards/CyberBeacon.cs:11>)；[CyberBeacon.cs:17](<../ThermalVortexCode/Cards/CyberBeacon.cs:17>)；[CyberBeaconPower.cs:16](<../ThermalVortexCode/Powers/CyberBeaconPower.cs:16>)；[CyberSeries.cs:72](<../ThermalVortexCode/Cards/CyberSeries.cs:72>)

### 电子龙核 · `CyberDragonCore`

内部名称：`THERMALVORTEX-CYBER_DRAGON_CORE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 以1只场上怪兽作为素材，召唤1只电子龙。  
> 召唤成功后，将1张电子虫加入弃牌堆。  

**升级卡面文本：**

> 以1只场上怪兽作为素材，召唤1只电子龙+。  
> 召唤成功后，将1张电子虫+加入弃牌堆。  

卡文来源：[当前中文资源:177](<../ThermalVortex/localization/zhs/cards.json:177>)，键 `THERMALVORTEX-CYBER_DRAGON_CORE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择1只合法场上怪兽作为素材，免费生成并立即打出1张电子龙；成功提交打出后，在弃牌堆生成1张电子虫。

**升级实际效果：** 选择1只合法场上怪兽作为素材，免费生成并立即打出1张电子龙+；成功提交打出后，在弃牌堆生成1张电子虫+。

**必要条件与实现补充：** 先检查使用素材后的空位及合法敌方目标，再提交素材。素材触发其素材/离场效果，去向依通用规则。电子龙本次设为本回合0费；未提交的临时龙清理。

**实现位置：** [CyberDragonCore.cs:13](<../ThermalVortexCode/Cards/CyberDragonCore.cs:13>)；[CyberDragonCore.cs:19](<../ThermalVortexCode/Cards/CyberDragonCore.cs:19>)；[CyberDragonCore.cs:50](<../ThermalVortexCode/Cards/CyberDragonCore.cs:50>)；[CyberDragonCore.cs:108](<../ThermalVortexCode/Cards/CyberDragonCore.cs:108>)；[EffectTargeting.cs:1](<../ThermalVortexCode/Cards/EffectTargeting.cs:1>)

### 电子龙芯 · `CyberDragonHerz`

内部名称：`THERMALVORTEX-CYBER_DRAGON_HERZ`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 0 |
| 怪兽生命 | 1 | 1 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 召唤时，将1张电子虫加入弃牌堆。  
> 下回合开始时，变为电子龙。  

**升级卡面文本：**

> 怪兽。生命：1。  
> 召唤时，将1张电子虫+加入弃牌堆。  
> 下回合开始时，变为电子龙+。  

卡文来源：[当前中文资源:179](<../ThermalVortex/localization/zhs/cards.json:179>)，键 `THERMALVORTEX-CYBER_DRAGON_HERZ.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤1生命电子怪兽；成功入场后在弃牌堆生成1张电子虫。下个玩家回合开始，仍在场时消耗自身，免费打出电子龙并尽量恢复原怪兽位。

**升级实际效果：** 召唤1生命电子怪兽；成功入场后在弃牌堆生成1张电子虫+。下个玩家回合开始，仍在场时消耗自身，免费打出电子龙+并尽量恢复原怪兽位。

**必要条件与实现补充：** 转换不额外计入出牌上限。新形态实际入场时提交吞噬成长迁移，旧卡以后回收不再拥有该份成长；入场后立即离场仍算成功。未入场则回退本次迁移，保留回调期间另行获得的成长。转换提交失败且本体仍在场时，下个玩家回合继续尝试；已离场不再转换。吞噬只继承入场生成虫效果；完整复制另外保留形态转换。

**实现位置：** [CyberDragonHerz.cs:12](<../ThermalVortexCode/Cards/CyberDragonHerz.cs:12>)；[CyberDragonHerz.cs:25](<../ThermalVortexCode/Cards/CyberDragonHerz.cs:25>)；[CyberDragonHerzPower.cs:43](<../ThermalVortexCode/Powers/CyberDragonHerzPower.cs:43>)；[CyberDragonHerzPower.cs:73](<../ThermalVortexCode/Powers/CyberDragonHerzPower.cs:73>)；[ChaosPhantomAndRaCards.cs:756](<../ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:756>)

### 电子紧急呼救 · `CyberEmergency`

内部名称：`THERMALVORTEX-CYBER_EMERGENCY`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 将1张电子龙加入手牌，1张电子虫加入弃牌堆。  

**升级卡面文本：**

> 将1张电子龙+加入手牌，1张电子虫+加入弃牌堆。  

卡文来源：[当前中文资源:170](<../ThermalVortex/localization/zhs/cards.json:170>)，键 `THERMALVORTEX-CYBER_EMERGENCY.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 在手牌生成1张电子龙，在弃牌堆生成1张电子虫。

**升级实际效果：** 在手牌生成1张电子龙+，在弃牌堆生成1张电子虫+。

**必要条件与实现补充：** 电子龙由其自身条件判断0费，本卡不额外授予本场永久0费。生成虫增加本战斗累计生成数。

**实现位置：** [CyberEmergency.cs:8](<../ThermalVortexCode/Cards/CyberEmergency.cs:8>)；[CyberEmergency.cs:14](<../ThermalVortexCode/Cards/CyberEmergency.cs:14>)；[CyberSeries.cs:72](<../ThermalVortexCode/Cards/CyberSeries.cs:72>)；[CyberSeries.cs:92](<../ThermalVortexCode/Cards/CyberSeries.cs:92>)

### 电子次代龙 · `CyberNextDragon`

内部名称：`THERMALVORTEX-CYBER_NEXT_DRAGON`。所属牌池：主牌奖励可选候选；类型：攻击；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：5。  
> 将场上全部非电子怪兽用作素材，至少1只。  
> 造成7点伤害X次，X为素材数。  

**升级卡面文本：**

> 怪兽。生命：5。  
> 将场上全部非电子怪兽用作素材，至少1只。  
> 造成10点伤害X次，X为素材数。  

卡文来源：[当前中文资源:168](<../ThermalVortex/localization/zhs/cards.json:168>)，键 `THERMALVORTEX-CYBER_NEXT_DRAGON.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 将场上全部非电子怪兽作为素材使用，召唤5生命电子次代龙。每用1只素材，对选择的敌人造成1段7点攻击伤害；继承攻击力只额外加入第一段。

**升级实际效果：** 将场上全部非电子怪兽作为素材使用，召唤5生命电子次代龙。每用1只素材，对选择的敌人造成1段10点攻击伤害；继承攻击力只额外加入第一段。

**必要条件与实现补充：** 至少有1只非电子素材且使用后可召唤才可打出。素材数在使用前快照。素材使用触发对应效果，去向按弃牌/消耗规则。

**结算补充：** 带吞噬继承攻击时，首段与余段虽然分为两条攻击命令，仍作为同一次主攻击共享力量焊接倍率；全部主攻击完成后才更新后续减伤。素材继承效果独立结算。

**实现位置：** [CyberNextDragon.cs:15](<../ThermalVortexCode/Cards/CyberNextDragon.cs:15>)；[CyberNextDragon.cs:17](<../ThermalVortexCode/Cards/CyberNextDragon.cs:17>)；[CyberNextDragon.cs:81](<../ThermalVortexCode/Cards/CyberNextDragon.cs:81>)；[CyberNextDragon.cs:144](<../ThermalVortexCode/Cards/CyberNextDragon.cs:144>)

### 电子修理工厂 · `CyberRepairPlant`

内部名称：`THERMALVORTEX-CYBER_REPAIR_PLANT`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 将弃牌堆中的1张电子怪兽洗回牌组。  
> 将1张电子虫加入弃牌堆。  

**升级卡面文本：**

> 将弃牌堆中的所有电子怪兽洗回牌组。  
> 将1张电子虫+加入弃牌堆。  

卡文来源：[当前中文资源:172](<../ThermalVortex/localization/zhs/cards.json:172>)，键 `THERMALVORTEX-CYBER_REPAIR_PLANT.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择自身弃牌堆1只电子怪兽，主牌怪兽放到抽牌堆随机位置，额外怪兽返回本场额外牌组；实际回收成功后在弃牌堆生成1张电子虫。

**升级实际效果：** 回收自身弃牌堆所有电子怪兽，主牌怪兽逐一放到抽牌堆随机位置，额外怪兽返回本场额外牌组；实际回收至少1张后，在弃牌堆生成1张电子虫+。

**必要条件与实现补充：** 基础版与升级版均要求自身弃牌堆至少有1张电子怪兽才能打出。只回收结算时仍在自身弃牌堆且符合电子身份的原实体或其对应额外条目；保留原卡升级、附魔与本场吞噬成长，不新增永久拥有副本。实际回收至少1张且原战斗仍有效才生成1张对应升级等级的电子虫。电子身份沿用现有复制判定；不升级所返还的原牌，也不执行整副抽牌堆洗牌。

**实现位置：** [CyberRepairPlant.cs:17](<../ThermalVortexCode/Cards/CyberRepairPlant.cs:17>)；[CyberRepairPlant.cs:24](<../ThermalVortexCode/Cards/CyberRepairPlant.cs:24>)；[CyberRepairPlant.cs:27](<../ThermalVortexCode/Cards/CyberRepairPlant.cs:27>)；[CardSelectionHelper.cs:271](<../ThermalVortexCode/Cards/CardSelectionHelper.cs:271>)；[CyberSeries.cs:46](<../ThermalVortexCode/Cards/CyberSeries.cs:46>)

### 电子革命系统 · `CyberRevolutionSystem`

内部名称：`THERMALVORTEX-CYBER_REVOLUTION_SYSTEM`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 本场战斗，电子虫的费用变为0。  
> 将1张电子虫加入弃牌堆。  

**升级卡面文本：**

> 本场战斗，电子虫的费用变为0。  
> 将1张电子虫+加入弃牌堆。  

卡文来源：[当前中文资源:175](<../ThermalVortex/localization/zhs/cards.json:175>)，键 `THERMALVORTEX-CYBER_REVOLUTION_SYSTEM.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本场战斗使你的电子虫及升级版费用变为0，并在弃牌堆生成1张电子虫。

**升级实际效果：** 本场战斗使你的电子虫及升级版费用变为0，并在弃牌堆生成1张电子虫+。

**必要条件与实现补充：** 单一能力不叠加；处理现有战斗牌堆、后来进入/生成/移动的虫及打出前状态，只降低正费用至0。

**实现位置：** [CyberRevolutionSystem.cs:11](<../ThermalVortexCode/Cards/CyberRevolutionSystem.cs:11>)；[CyberRevolutionSystem.cs:17](<../ThermalVortexCode/Cards/CyberRevolutionSystem.cs:17>)；[CyberRevolutionSystemPower.cs:26](<../ThermalVortexCode/Powers/CyberRevolutionSystemPower.cs:26>)；[CyberRevolutionSystemPower.cs:70](<../ThermalVortexCode/Powers/CyberRevolutionSystemPower.cs:70>)

### 电子废品站 · `CyberScrapYard`

内部名称：`THERMALVORTEX-CYBER_SCRAP_YARD`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 将1张电子虫加入弃牌堆。  
> 每回合开始时，将1张电子龙加入弃牌堆。  

**升级卡面文本：**

> 将1张电子虫+加入弃牌堆。  
> 每回合开始时，将1张电子龙+加入弃牌堆。  

卡文来源：[当前中文资源:183](<../ThermalVortex/localization/zhs/cards.json:183>)，键 `THERMALVORTEX-CYBER_SCRAP_YARD.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 在弃牌堆生成1张电子虫。本场战斗每个后续玩家回合开始，在弃牌堆生成1张电子龙。

**升级实际效果：** 在弃牌堆生成1张电子虫+。本场战斗每个后续玩家回合开始，在弃牌堆生成1张电子龙+。

**必要条件与实现补充：** 重复获得不增加每回合数量；打出过升级版后，后续始终生成升级龙。

**实现位置：** [CyberScrapYard.cs:11](<../ThermalVortexCode/Cards/CyberScrapYard.cs:11>)；[CyberScrapYard.cs:17](<../ThermalVortexCode/Cards/CyberScrapYard.cs:17>)；[CyberScrapYardPower.cs:16](<../ThermalVortexCode/Powers/CyberScrapYardPower.cs:16>)

### 虫龙相生 · `CyberSymbiosis`

内部名称：`THERMALVORTEX-CYBER_SYMBIOSIS`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 3 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 无 |

**基础卡面文本：**

> 本场此前每生成1张电子虫：将1张电子龙加入手牌。最后将1张电子虫加入弃牌堆。  
> 消耗。  

**升级卡面文本：**

> 本场此前每生成1张电子虫：将1张电子龙+加入手牌。最后将1张电子虫+加入弃牌堆。  

卡文来源：[当前中文资源:191](<../ThermalVortex/localization/zhs/cards.json:191>)，键 `THERMALVORTEX-CYBER_SYMBIOSIS.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 先读取本场战斗累计生成电子虫数K，在手牌生成K张电子龙；最后固定在弃牌堆生成1张电子虫。

**升级实际效果：** 先读取本场战斗累计生成电子虫数K，在手牌生成K张电子龙+；最后固定在弃牌堆生成1张电子虫+。

**必要条件与实现补充：** K只统计专用生成电子虫入口的累计数量，不是当前虫数；本次最后生成的虫不计入本次K。K为0仍生成1虫。本卡不赋予生成龙额外恒定0费。

**实现位置：** [CyberSymbiosis.cs:9](<../ThermalVortexCode/Cards/CyberSymbiosis.cs:9>)；[CyberSymbiosis.cs:15](<../ThermalVortexCode/Cards/CyberSymbiosis.cs:15>)；[CyberSymbiosis.cs:25](<../ThermalVortexCode/Cards/CyberSymbiosis.cs:25>)；[CyberSeries.cs:54](<../ThermalVortexCode/Cards/CyberSeries.cs:54>)；[CyberSeries.cs:124](<../ThermalVortexCode/Cards/CyberSeries.cs:124>)

### 力量焊接 · `CyberWelding`

内部名称：`THERMALVORTEX-CYBER_WELDING`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 无 |

**基础卡面文本：**

> 本回合，下一张怪兽造成的伤害提高50%。触发后，本回合所有攻击伤害降低50%。  
> 消耗。  

**升级卡面文本：**

> 本回合，下一张怪兽造成的伤害提高50%。触发后，本回合所有攻击伤害降低50%。  

卡文来源：[当前中文资源:185](<../ThermalVortex/localization/zhs/cards.json:185>)，键 `THERMALVORTEX-CYBER_WELDING.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本回合每次使用增加1次焊接累计使用数并等待下一次合格怪兽输出。该次完整输出乘以1.5的累计使用次数次方，忽略此前减伤；结算后本方后续攻击按已结算累计次数每次降低50%，最多降低100%。

**升级实际效果：** 本回合每次使用增加1次焊接累计使用数并等待下一次合格怪兽输出。该次完整输出乘以1.5的累计使用次数次方，忽略此前减伤；结算后本方后续攻击按已结算累计次数每次降低50%，最多降低100%。

**必要条件与实现补充：** 合格身份为本方任意怪兽，包含普通怪兽与融合怪兽，不再要求电子身份。怪兽效果伤害及已接入的直接扣血也应用倍率并消费机会；非怪兽的非攻击效果伤害不参与。只有真实正值敌方输出开始才消费等待标记，单纯打出或召唤而未造成伤害不消费；完成后才更新后续倍率，一次命令的多段输出保持同倍率；次代龙首段与余段共同组成一次主攻击输出，全部完成后才结算减伤，素材继承效果独立结算。回合末失效。

**实现位置：** [CyberWelding.cs:11](<../ThermalVortexCode/Cards/CyberWelding.cs:11>)；[CyberWelding.cs:17](<../ThermalVortexCode/Cards/CyberWelding.cs:17>)；[CyberWeldingPower.cs:32](<../ThermalVortexCode/Powers/CyberWeldingPower.cs:32>)；[CyberWeldingPower.cs:130](<../ThermalVortexCode/Powers/CyberWeldingPower.cs:130>)；[CyberWeldingPower.cs:156](<../ThermalVortexCode/Powers/CyberWeldingPower.cs:156>)；[CyberWeldingTurnEndPatch.cs:1](<../ThermalVortexCode/Patches/CyberWeldingTurnEndPatch.cs:1>)；[MonsterFieldService.cs:30](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:30>)

### 电子负载融合 · `CyberloadFusion`

内部名称：`THERMALVORTEX-CYBERLOAD_FUSION`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 以场上或消耗堆的怪兽作为融合素材召唤1只融合怪兽。然后将所有素材怪兽放回抽牌堆和额外牌组。  
> 消耗。  

**升级卡面文本：**

> 以场上或消耗堆的怪兽作为融合素材召唤1只融合怪兽。然后将所有素材怪兽放回抽牌堆和额外牌组。  
> 消耗。  

卡文来源：[当前中文资源:37](<../ThermalVortex/localization/zhs/cards.json:37>)，键 `THERMALVORTEX-CYBERLOAD_FUSION.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 从场上或消耗堆选择符合条件的素材，融合召唤1只本场额外牌组怪兽。主牌组素材返回抽牌堆并洗牌；额外怪兽素材返回本场额外牌组。

**升级实际效果：** 从场上或消耗堆选择符合条件的素材，融合召唤1只本场额外牌组怪兽。主牌组素材返回抽牌堆并洗牌；额外怪兽素材返回本场额外牌组。

**必要条件与实现补充：** 必须具有核心遗物、合法目标/素材。不同额外怪兽的指定素材条件仍适用；不能召唤仅允许普通融合入口的球形体。素材依然触发素材效果；若自身效果已把额外素材重新召唤在场，返还步骤保留其在场，不强制收回。只更新本战斗额外条目，不增加永久拥有。

**实现位置：** [CyberloadFusion.cs:9](<../ThermalVortexCode/Cards/CyberloadFusion.cs:9>)；[CyberloadFusion.cs:15](<../ThermalVortexCode/Cards/CyberloadFusion.cs:15>)；[ThermalVortexCore.cs:493](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:493>)；[ThermalVortexCore.cs:2907](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2907>)；[ThermalVortexCore.cs:2944](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2944>)；[ThermalVortexCore.cs:2147](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2147>)

### 黑暗电子世界 · `DarkCyberWorld`

内部名称：`THERMALVORTEX-DARK_CYBER_WORLD`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 将1张电子虫加入弃牌堆。  
> 每回合结束时，弃牌堆中随机1张电子怪兽吞噬该牌堆中的另外1张随机电子怪兽。若只有1张，则消耗它。  

**升级卡面文本：**

> 理智残存。  
> 将1张电子虫+加入弃牌堆。  
> 每回合结束时，弃牌堆中随机1张电子怪兽吞噬该牌堆中的另外1张随机电子怪兽。  

卡文来源：[当前中文资源:187](<../ThermalVortex/localization/zhs/cards.json:187>)，键 `THERMALVORTEX-DARK_CYBER_WORLD.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 立即在弃牌堆生成1张电子虫。每个本方回合末，弃牌堆至少有2只电子怪兽时随机选1只吞噬随机另一只；恰有1只则消耗该只。

**升级实际效果：** 立即在弃牌堆生成1张电子虫+。每个本方回合末，弃牌堆至少有2只电子怪兽时随机选1只吞噬随机另一只；恰有1只则停止，不消耗该只（理智残存）。

**必要条件与实现补充：** 能力不叠加。每次回合末只结算一对随机电子怪兽，不是所有电子怪兽各吞1只；0只不处理。目标保留在弃牌堆时才提交，先吸收后消耗；吞噬者留在弃牌堆。升级标记一旦启用不会因后来打出未升级版丢失。

**实现位置：** [DarkCyberWorld.cs:12](<../ThermalVortexCode/Cards/DarkCyberWorld.cs:12>)；[DarkCyberWorld.cs:18](<../ThermalVortexCode/Cards/DarkCyberWorld.cs:18>)；[DarkCyberWorldPower.cs:20](<../ThermalVortexCode/Powers/DarkCyberWorldPower.cs:20>)；[CyberSeries.cs:149](<../ThermalVortexCode/Cards/CyberSeries.cs:149>)

### 黑暗决斗 · `DarkDuel`

内部名称：`THERMALVORTEX-DARK_DUEL`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 本场战斗，你的怪兽生命清零后仍可留场，直到你的下一个回合结束。  

**升级卡面文本：**

> 本场战斗，你的怪兽生命清零后仍可留场，直到你的下一个回合结束。  

卡文来源：[当前中文资源:223](<../ThermalVortex/localization/zhs/cards.json:223>)，键 `THERMALVORTEX-DARK_DUEL.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本场战斗持续保护你的全部场上怪兽，包括之后召唤的怪兽。每只怪兽生命清零后，保留至其下一个己方回合结束；届时仍为0生命才破坏。

**升级实际效果：** 费用1。本场战斗持续保护你的全部场上怪兽，包括之后召唤的怪兽。每只怪兽生命清零后，保留至其下一个己方回合结束；届时仍为0生命才破坏。

**必要条件与实现补充：** 每个实体独立记录生命由正数降至0时的期限；在己方回合内清零也保留到下一个己方回合结束，不在当前回合结束时结算。恢复至正生命后取消该次期限，再次清零重新计时；仍为0时再次受伤不延长期限。能力不会因某只怪兽结算或回合结束而移除，重复使用不叠加。0生命保护不等于回复生命，主动素材、消耗与明确破坏仍可令其离场；到期破坏不再次获得零血保护。与纳祭魔共享生命沿用同一保护和死亡结算。

**实现位置：** [NewMainDeckCards.cs:72](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:72>)；[NewCardPowers.cs:307](<../ThermalVortexCode/Powers/NewCardPowers.cs:307>)；[MonsterFieldHealthService.cs:178](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:178>)；[MonsterFieldHealthService.cs:156](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:156>)

### 黑魔导 · `DarkMagician`

内部名称：`THERMALVORTEX-DARK_MAGICIAN`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 8 | 8 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：8。  
> 获得6点格挡。  

**升级卡面文本：**

> 怪兽。生命：8。  
> 获得10点格挡。  

卡文来源：[当前中文资源:45](<../ThermalVortex/localization/zhs/cards.json:45>)，键 `THERMALVORTEX-DARK_MAGICIAN.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤8生命怪兽，获得6点格挡。

**升级实际效果：** 召唤8生命怪兽，获得10点格挡。

**必要条件与实现补充：** 格挡由正常卡牌格挡命令结算，受其修正。

**实现位置：** [DarkMagician.cs:9](<../ThermalVortexCode/Cards/DarkMagician.cs:9>)；[DarkMagician.cs:18](<../ThermalVortexCode/Cards/DarkMagician.cs:18>)

### 暗黑界的取引 · `DarkWorldDealings`

内部名称：`THERMALVORTEX-DARK_WORLD_DEALINGS`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 抽1张牌，丢弃1张牌。  

**升级卡面文本：**

> 抽2张牌，丢弃2张牌。  

卡文来源：[当前中文资源:149](<../ThermalVortex/localization/zhs/cards.json:149>)，键 `THERMALVORTEX-DARK_WORLD_DEALINGS.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 抽1张牌，再选择弃掉1张除本牌外的手牌。

**升级实际效果：** 抽2张牌，再选择弃掉2张除本牌外的手牌。

**必要条件与实现补充：** 弃牌时手牌不足目标数量则弃掉全部候选；无候选不再选牌。

**实现位置：** [DarkWorldDealings.cs:10](<../ThermalVortexCode/Cards/DarkWorldDealings.cs:10>)；[DarkWorldDealings.cs:16](<../ThermalVortexCode/Cards/DarkWorldDealings.cs:16>)

### 次元扩张 · `DimensionalExpansion`

内部名称：`THERMALVORTEX-DIMENSIONAL_EXPANSION`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 本场战斗增加1个怪兽位。  

**升级卡面文本：**

> 本场战斗增加1个怪兽位。  

卡文来源：[当前中文资源:241](<../ThermalVortex/localization/zhs/cards.json:241>)，键 `THERMALVORTEX-DIMENSIONAL_EXPANSION.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本场战斗增加1个怪兽位。

**升级实际效果：** 费用1，本场战斗增加1个怪兽位。

**必要条件与实现补充：** 计数型能力，多次使用的容量增加可叠加。

**实现位置：** [NewMainDeckCards.cs:309](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:309>)；[NewCardPowers.cs:150](<../ThermalVortexCode/Powers/NewCardPowers.cs:150>)

### 教导的惩罚 · `DogmatikaPunishment`

内部名称：`THERMALVORTEX-DOGMATIKA_PUNISHMENT`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 选择1名敌人，消耗1张最大生命高于其当前生命的额外牌组融合怪兽，击杀该敌人。  

**升级卡面文本：**

> 选择1名敌人，消耗1张最大生命高于其当前生命的额外牌组融合怪兽，击杀该敌人。  

卡文来源：[当前中文资源:124](<../ThermalVortex/localization/zhs/cards.json:124>)，键 `THERMALVORTEX-DOGMATIKA_PUNISHMENT.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择1名敌人，消耗本场额外牌组中生命上限严格大于其当前生命的1只怪兽，直接击杀该敌人。

**升级实际效果：** 费用1。选择1名敌人，消耗本场额外牌组中生命上限严格大于其当前生命的1只怪兽，直接击杀该敌人。

**必要条件与实现补充：** 目标必须存活、可命中、属于同一场战斗，且有合格额外牌才能打出/提交。额外牌生命使用其当前预览初始上限（包含模型及成长规则）；消耗前重新核对目标及比较。目标生命减少至0不是本卡的伤害公式，而是 Kill 命令。消耗可触发断路护符兽格挡。

**实现位置：** [DogmatikaPunishment.cs:12](<../ThermalVortexCode/Cards/DogmatikaPunishment.cs:12>)；[DogmatikaPunishment.cs:18](<../ThermalVortexCode/Cards/DogmatikaPunishment.cs:18>)；[DogmatikaPunishment.cs:37](<../ThermalVortexCode/Cards/DogmatikaPunishment.cs:37>)；[ThermalVortexCore.cs:761](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:761>)；[MonsterFieldHealthService.cs:120](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:120>)

### 旧神 努茨 · `ElderEntityNtss`

内部名称：`THERMALVORTEX-ELDER_ENTITY_NTSS`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 4 | 4 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：4。  
> 作为素材使用时，对1名敌人造成10点伤害。  

**升级卡面文本：**

> 怪兽。生命：4。  
> 作为素材使用时，对1名敌人造成15点伤害。  

卡文来源：[当前中文资源:158](<../ThermalVortex/localization/zhs/cards.json:158>)，键 `THERMALVORTEX-ELDER_ENTITY_NTSS.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤4生命怪兽。作为素材使用时选择1名敌人，造成10点非强化型效果伤害。

**升级实际效果：** 召唤4生命怪兽。作为素材使用时选择1名敌人，造成15点非强化型效果伤害。

**必要条件与实现补充：** 作为普通素材或融合素材均可触发；手牌/抽牌堆等非场上素材也通过统一素材事件触发。单纯消耗、战斗破坏不触发此素材效果。该效果可被吞噬继承。

**实现位置：** [ElderEntityNtss.cs:13](<../ThermalVortexCode/Cards/ElderEntityNtss.cs:13>)；[ElderEntityNtss.cs:15](<../ThermalVortexCode/Cards/ElderEntityNtss.cs:15>)；[ElderEntityNtss.cs:41](<../ThermalVortexCode/Cards/ElderEntityNtss.cs:41>)；[MonsterFieldService.cs:562](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:562>)；[CyberSeries.cs:916](<../ThermalVortexCode/Cards/CyberSeries.cs:916>)

### β号磁圈 · `Electrode`

内部名称：`THERMALVORTEX-ELECTRODE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 2 | 5 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：2。  
> 获得2点格挡。  
> 召唤时，可免费打出手牌或抽牌堆中的1张能力牌；然后可从手牌或抽牌堆中召唤1只电圈。  

**升级卡面文本：**

> 怪兽。生命：5。  
> 获得5点格挡。  
> 召唤时，可免费打出手牌或抽牌堆中的1张能力牌；然后可从手牌或抽牌堆中召唤1只电圈。  

卡文来源：[当前中文资源:19](<../ThermalVortex/localization/zhs/cards.json:19>)，键 `THERMALVORTEX-ELECTRODE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 获得2点格挡并召唤2生命磁圈。可从手牌或抽牌堆选择1张能力牌免费立即打出，再可从这两个牌堆选择1只电圈免费召唤。

**升级实际效果：** 获得5点格挡并召唤5生命磁圈。可从手牌或抽牌堆选择1张能力牌免费立即打出，再可从这两个牌堆选择1只电圈免费召唤。

**必要条件与实现补充：** 两次选择都可空选跳过，单候选也不强制自动选择。只提供当时能立即打出的候选；电圈包括双身份电磁圈。连锁受怪兽位及原牌其他可打出条件限制，保留候选原升级状态。

**实现位置：** [Electrode.cs:11](<../ThermalVortexCode/Cards/Electrode.cs:11>)；[Electrode.cs:20](<../ThermalVortexCode/Cards/Electrode.cs:20>)；[Electrode.cs:32](<../ThermalVortexCode/Cards/Electrode.cs:32>)；[PoleMonsterCard.cs:40](<../ThermalVortexCode/Cards/PoleMonsterCard.cs:40>)；[EffectTargeting.cs:1](<../ThermalVortexCode/Cards/EffectTargeting.cs:1>)

### β号电圈 · `ExternalCombustion`

内部名称：`THERMALVORTEX-EXTERNAL_COMBUSTION`。所属牌池：主牌奖励可选候选；类型：攻击；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 2 | 5 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：2。  
> 造成2点伤害。  
> 可消耗1张额外牌组怪兽，从手牌或抽牌堆召唤1只磁圈。  

**升级卡面文本：**

> 怪兽。生命：5。  
> 造成5点伤害。  
> 可消耗1张额外牌组怪兽，从手牌或抽牌堆召唤1只磁圈。  

卡文来源：[当前中文资源:11](<../ThermalVortex/localization/zhs/cards.json:11>)，键 `THERMALVORTEX-EXTERNAL_COMBUSTION.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 造成2点攻击伤害并召唤2生命电圈。可从手牌或抽牌堆选择1只磁圈，再选择并消耗本场额外牌组1只怪兽，随后免费立即打出所选磁圈。

**升级实际效果：** 造成5点攻击伤害并召唤5生命电圈；仍可选择磁圈、消耗1只额外怪兽，再免费立即打出该磁圈。

**必要条件与实现补充：** 必须有额外牌和合法磁圈候选才进入连锁；磁圈可空选跳过，额外选择可返回重选。消耗前复查磁圈仍在原候选区域且能立即打出；当前代码在实际连锁打出之前提交额外消耗，不能表述为仅成功入场后支付。消耗额外牌不是融合素材，不触发素材专属效果。

**实现位置：** [ExternalCombustion.cs:11](<../ThermalVortexCode/Cards/ExternalCombustion.cs:11>)；[ExternalCombustion.cs:20](<../ThermalVortexCode/Cards/ExternalCombustion.cs:20>)；[ExternalCombustion.cs:38](<../ThermalVortexCode/Cards/ExternalCombustion.cs:38>)；[ExternalCombustion.cs:73](<../ThermalVortexCode/Cards/ExternalCombustion.cs:73>)；[PoleMonsterCard.cs:59](<../ThermalVortexCode/Cards/PoleMonsterCard.cs:59>)；[ThermalVortexCore.cs:1](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:1>)

### 禁忌的圣杯 · `ForbiddenChalice`

内部名称：`THERMALVORTEX-FORBIDDEN_CHALICE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 无 |

**基础卡面文本：**

> 将1名敌人的下次行动替换为获得3点力量。  
> 消耗。  

**升级卡面文本：**

> 将1名敌人的下次行动替换为获得3点力量。  

卡文来源：[当前中文资源:152](<../ThermalVortex/localization/zhs/cards.json:152>)，键 `THERMALVORTEX-FORBIDDEN_CHALICE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 使选择的敌人下一次行动改为获得3点力量。

**升级实际效果：** 移除消耗；使选择的敌人下一次行动改为获得3点力量。

**必要条件与实现补充：** 单一能力不叠加；原行动被视为已执行并推进状态机，替代效果后移除。若到其所属回合结束仍未触发也移除。被控制等更前置的行动替换分支可能优先生效。

**实现位置：** [ForbiddenChalice.cs:11](<../ThermalVortexCode/Cards/ForbiddenChalice.cs:11>)；[ForbiddenChalice.cs:17](<../ThermalVortexCode/Cards/ForbiddenChalice.cs:17>)；[ForbiddenChalicePower.cs:18](<../ThermalVortexCode/Powers/ForbiddenChalicePower.cs:18>)；[InfiniteImpermanenceMonsterMovePatch.cs:32](<../ThermalVortexCode/Patches/InfiniteImpermanenceMonsterMovePatch.cs:32>)

### 禁忌的一滴 · `ForbiddenDroplet`

内部名称：`THERMALVORTEX-FORBIDDEN_DROPLET`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 消耗任意张手牌，每消耗1张，使1名随机敌人失去2点力量。  
> 消耗。  

**升级卡面文本：**

> 消耗任意张手牌，每消耗1张，使1名随机敌人失去3点力量。  
> 消耗。  

卡文来源：[当前中文资源:68](<../ThermalVortex/localization/zhs/cards.json:68>)，键 `THERMALVORTEX-FORBIDDEN_DROPLET.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择任意数量的其他手牌消耗；每消耗1张，随机使1名可命中敌人失去2点力量。

**升级实际效果：** 选择任意数量的其他手牌消耗；每消耗1张，随机使1名可命中敌人失去3点力量。

**必要条件与实现补充：** 可选择0张并取消；逐牌消耗后各自重新随机敌人，同一敌人可被多次选中。无敌人时仍会消耗所选牌，但没有减力量目标。

**实现位置：** [ForbiddenDroplet.cs:13](<../ThermalVortexCode/Cards/ForbiddenDroplet.cs:13>)；[ForbiddenDroplet.cs:19](<../ThermalVortexCode/Cards/ForbiddenDroplet.cs:19>)；[ForbiddenDroplet.cs:29](<../ThermalVortexCode/Cards/ForbiddenDroplet.cs:29>)

### 熔炉启动 · `FurnaceStartup`

内部名称：`THERMALVORTEX-FURNACE_STARTUP`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 每回合第一次召唤怪兽时，抽1张牌。  

**升级卡面文本：**

> 每回合第一次召唤怪兽时，抽1张牌。  

卡文来源：[当前中文资源:71](<../ThermalVortex/localization/zhs/cards.json:71>)，键 `THERMALVORTEX-FURNACE_STARTUP.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本场战斗，每个玩家回合内第一次成功召唤本方怪兽时，按当前能力层数抽牌，每层抽1张。

**升级实际效果：** 费用1。本场战斗，每个玩家回合内第一次成功召唤本方怪兽时，按当前能力层数抽牌，每层抽1张。

**必要条件与实现补充：** 重复使用叠加抽牌层数，每回合仍只在第一次成功召唤时触发。只有实体成功在场才消费触发机会；先置触发标记，避免嵌套召唤重复抽牌。按拥有者实际回合编号重置机会，包含额外回合，不依赖能力获得与回调顺序；手动、卡牌效果和额外召唤均可触发。本回合已触发后再增加层数，不重新获得本回合触发机会。

**实现位置：** [FurnaceStartup.cs:9](<../ThermalVortexCode/Cards/FurnaceStartup.cs:9>)；[FurnaceStartup.cs:15](<../ThermalVortexCode/Cards/FurnaceStartup.cs:15>)；[FurnaceStartupPower.cs:25](<../ThermalVortexCode/Powers/FurnaceStartupPower.cs:25>)

### 融合命运 · `FusionDestiny`

内部名称：`THERMALVORTEX-FUSION_DESTINY`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 本场战斗，所有“融合牌”费用降低1点。  

**升级卡面文本：**

> 本场战斗，所有“融合牌”费用降低1点。  

卡文来源：[当前中文资源:41](<../ThermalVortex/localization/zhs/cards.json:41>)，键 `THERMALVORTEX-FUSION_DESTINY.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本场战斗使你的融合牌每层费用降低1点，包括之后生成或进入战斗的牌。

**升级实际效果：** 费用2。本场战斗使你的融合牌每层费用降低1点，包括之后生成或进入战斗的牌。

**必要条件与实现补充：** 计数型能力，多次施加累加。融合判定检查当前显示名称或类名包含“融合”或“Fusion”，不限制牌型；每张牌只补加尚未应用的累计减费差值，不因反复换牌堆重复减同一层。

**实现位置：** [FusionDestiny.cs:9](<../ThermalVortexCode/Cards/FusionDestiny.cs:9>)；[FusionDestiny.cs:15](<../ThermalVortexCode/Cards/FusionDestiny.cs:15>)；[FusionDestinyPower.cs:79](<../ThermalVortexCode/Powers/FusionDestinyPower.cs:79>)

### 融合之门 · `FusionGate`

内部名称：`THERMALVORTEX-FUSION_GATE`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 每回合开始时，失去3点生命，消耗场上或手牌的怪兽融合召唤一只融合怪兽。  
> 若无法召唤，则再失去3点生命。  

**升级卡面文本：**

> 每回合开始时，失去2点生命，消耗场上或手牌的怪兽融合召唤一只融合怪兽。  
> 若无法召唤，则再失去2点生命。  

卡文来源：[当前中文资源:43](<../ThermalVortex/localization/zhs/cards.json:43>)，键 `THERMALVORTEX-FUSION_GATE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 每个后续玩家回合开始先失去3生命，再以场上或手牌怪兽消耗为素材尝试融合召唤1只额外怪兽；未成功且你仍存活时，再失去3生命。

**升级实际效果：** 每个后续玩家回合开始先失去2生命，再以场上或手牌怪兽消耗为素材尝试融合召唤1只额外怪兽；未成功且你仍存活时，再失去2生命。

**必要条件与实现补充：** 能力单份，不随重复获得增加每回合次数；两次生命支付取最后打出的版本。没有合法召唤也先支付，再按失败追加；不能召唤球形体。素材在尝试中按融合素材消耗，可触发对应效果。

**实现位置：** [FusionGate.cs:9](<../ThermalVortexCode/Cards/FusionGate.cs:9>)；[FusionGate.cs:15](<../ThermalVortexCode/Cards/FusionGate.cs:15>)；[FusionGatePower.cs:22](<../ThermalVortexCode/Powers/FusionGatePower.cs:22>)；[ThermalVortexCore.cs:487](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:487>)；[ThermalVortexCore.cs:2911](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2911>)

### 融合的事前准备 · `FusionPreparation`

内部名称：`THERMALVORTEX-FUSION_PREPARATION`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 下回合开始时，将1张融合加入手牌，获得1点能量。  

**升级卡面文本：**

> 下回合开始时，将1张融合加入手牌，获得2点能量。  

卡文来源：[当前中文资源:231](<../ThermalVortex/localization/zhs/cards.json:231>)，键 `THERMALVORTEX-FUSION_PREPARATION.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 下个玩家回合开始，在手牌生成1张未升级融合并获得1点能量。

**升级实际效果：** 下个玩家回合开始，在手牌生成1张未升级融合并获得2点能量。

**必要条件与实现补充：** 每次使用独立记一份预约，多个预约可叠加：每份生成1张融合，能量按份相加。升级来源也生成未升级融合；全部预约在一次下回合结算后清除。

**实现位置：** [NewMainDeckCards.cs:171](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:171>)；[NewCardPowers.cs:17](<../ThermalVortexCode/Powers/NewCardPowers.cs:17>)

### 未来融合 · `FutureFusion`

内部名称：`THERMALVORTEX-FUTURE_FUSION`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 消耗抽牌堆里至多2张怪兽作为融合素材。  
> 下回合开始时，融合召唤1只额外牌组怪兽。  

**升级卡面文本：**

> 消耗抽牌堆里至多3张怪兽作为融合素材。  
> 下回合开始时，融合召唤1只额外牌组怪兽。  

卡文来源：[当前中文资源:145](<../ThermalVortex/localization/zhs/cards.json:145>)，键 `THERMALVORTEX-FUTURE_FUSION.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 从抽牌堆选择符合额外怪兽条件的至多2只怪兽，立即将其消耗为融合素材，预约下个玩家回合开始召唤所选本场额外怪兽。

**升级实际效果：** 从抽牌堆选择符合额外怪兽条件的至多3只怪兽，立即消耗为融合素材，预约下个玩家回合开始召唤所选本场额外怪兽。

**必要条件与实现补充：** 准备时先移出对应额外条目并触发素材效果，预约记录实体身份、类型、升级/附魔、完整吞噬成长快照及素材数；多次预约可叠加。受实际素材下限、指定来源约束；要求场上指定素材的怪兽不能用纯抽牌堆素材规避，球形体和纳祭魔预约不适用。到期重新检查空位与可打出条件，失败按额外召唤失败回收流程返还条目，不自动延期预约。

**结算补充：** 预约到期后独立选择目标，再直接进入出牌结算堆，不经过手牌，满手不会使召唤落入弃牌堆。生成钩子只执行一次；真实入场即提交，即使随后立即离场也不重复返还额外条目。

**实现位置：** [FutureFusion.cs:10](<../ThermalVortexCode/Cards/FutureFusion.cs:10>)；[FutureFusion.cs:16](<../ThermalVortexCode/Cards/FutureFusion.cs:16>)；[ThermalVortexCore.cs:790](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:790>)；[ThermalVortexCore.cs:580](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:580>)；[ThermalVortexCore.cs:2913](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2913>)；[FutureFusionPower.cs:22](<../ThermalVortexCode/Powers/FutureFusionPower.cs:22>)

### 封印的黄金柜 · `GoldSarcophagus`

内部名称：`THERMALVORTEX-GOLD_SARCOPHAGUS`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 无 |

**基础卡面文本：**

> 选择抽牌堆中的1张牌消耗，2回合后将其加入手牌。  
> 消耗。  

**升级卡面文本：**

> 选择抽牌堆中的1张牌消耗，2回合后将其加入手牌。  

卡文来源：[当前中文资源:113](<../ThermalVortex/localization/zhs/cards.json:113>)，键 `THERMALVORTEX-GOLD_SARCOPHAGUS.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 从自身抽牌堆选择1张牌，实际消耗成功后预约；第2个后续玩家回合开始，该原牌仍在消耗堆时将其加入手牌。

**升级实际效果：** 从自身抽牌堆选择1张牌，实际消耗成功后预约；第2个后续玩家回合开始，该原牌仍在消耗堆时将其加入手牌。

**必要条件与实现补充：** 需要自身抽牌堆有牌才能打出。选择结束后目标须仍在自身抽牌堆；本次操作新增同一原实体的消耗记录且原战斗仍有效才预约，合法立即复活不撤销已消耗的事实。每个预约独立倒计时；到期时原实体已离开消耗堆则不生成替代牌，也不继续等候。每张封印牌显示独立能力图标，说明使用该牌实际名称及升级标记，数字为该牌剩余回合；同名牌分别显示，各自从2个后续玩家回合开始倒计时。

**实现位置：** [GoldSarcophagus.cs:21](<../ThermalVortexCode/Cards/GoldSarcophagus.cs:21>)；[GoldSarcophagus.cs:29](<../ThermalVortexCode/Cards/GoldSarcophagus.cs:29>)；[GoldSarcophagusReturnPower.cs:25](<../ThermalVortexCode/Powers/GoldSarcophagusReturnPower.cs:25>)

### 天使的施舍 · `GracefulCharity`

内部名称：`THERMALVORTEX-GRACEFUL_CHARITY`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 抽3张牌，丢弃2张牌。  

**升级卡面文本：**

> 抽4张牌，丢弃3张牌。  

卡文来源：[当前中文资源:119](<../ThermalVortex/localization/zhs/cards.json:119>)，键 `THERMALVORTEX-GRACEFUL_CHARITY.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 抽3张牌，再选择弃掉2张其他手牌。

**升级实际效果：** 抽4张牌，再选择弃掉3张其他手牌。

**必要条件与实现补充：** 弃牌时手牌不足所需数量则弃掉全部候选；无候选直接结束。

**实现位置：** [GracefulCharity.cs:10](<../ThermalVortexCode/Cards/GracefulCharity.cs:10>)；[GracefulCharity.cs:16](<../ThermalVortexCode/Cards/GracefulCharity.cs:16>)

### 红莲魔兽 · `GrenMajuDaEiza`

内部名称：`THERMALVORTEX-GREN_MAJU_DA_EIZA`。所属牌池：主牌奖励可选候选；类型：攻击；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 6 | 6 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：6。  
> 造成2点伤害x次，x为消耗堆牌数。  

**升级卡面文本：**

> 怪兽。生命：6。  
> 造成3点伤害x次，x为消耗堆牌数。  

卡文来源：[当前中文资源:33](<../ThermalVortex/localization/zhs/cards.json:33>)，键 `THERMALVORTEX-GREN_MAJU_DA_EIZA.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤6生命怪兽，对选择的敌人造成2点攻击伤害X次，X为结算时消耗堆牌数。

**升级实际效果：** 召唤6生命怪兽，对选择的敌人造成3点攻击伤害X次，X为结算时消耗堆牌数。

**必要条件与实现补充：** 召唤请求后读取当前消耗堆计数；X为0不攻击。每段独立按攻击修正处理，而不是合成一段2X/3X伤害。

**实现位置：** [GrenMajuDaEiza.cs:9](<../ThermalVortexCode/Cards/GrenMajuDaEiza.cs:9>)；[GrenMajuDaEiza.cs:19](<../ThermalVortexCode/Cards/GrenMajuDaEiza.cs:19>)；[GrenMajuDaEiza.cs:28](<../ThermalVortexCode/Cards/GrenMajuDaEiza.cs:28>)

### 英雄到来 · `HeroArrival`

内部名称：`THERMALVORTEX-HERO_ARRIVAL`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 查看抽牌堆顶3张牌，免费打出其中1只可召唤的怪兽。  
> 消耗。  

**升级卡面文本：**

> 查看抽牌堆顶5张牌，免费打出其中1只可召唤的怪兽。  
> 消耗。  

卡文来源：[当前中文资源:228](<../ThermalVortex/localization/zhs/cards.json:228>)，键 `THERMALVORTEX-HERO_ARRIVAL.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 查看抽牌堆顶3张中的合法怪兽，选择1只免费立即打出并召唤。

**升级实际效果：** 查看抽牌堆顶5张中的合法怪兽，选择1只免费立即打出并召唤。

**必要条件与实现补充：** 若抽牌堆不足N张，先保持现有抽牌堆顺序，将弃牌堆洗混后补至抽牌堆底部；抽牌堆与弃牌堆合计仍不足时查看全部。随后取实际顶部N张，再从中筛选可入场且可立即打出的怪兽；不将更深牌补齐为N只候选。没有候选也可打出但无效果。每次结算只补洗一次，所选原牌仅设为本回合0费，不升级，不移动未选择牌。

**实现位置：** [NewMainDeckCards.cs:127](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:127>)；[EffectTargeting.cs:1](<../ThermalVortexCode/Cards/EffectTargeting.cs:1>)

### 通往活路的希望 · `HopeForEscape`

内部名称：`THERMALVORTEX-HOPE_FOR_ESCAPE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 每缺失最大生命的10%，抽1张牌。  
> 然后失去最大生命10%的生命。  
> 消耗。  

**升级卡面文本：**

> 每缺失最大生命的10%，抽1张牌。  
> 然后失去最大生命10%的生命。  
> 消耗。  

卡文来源：[当前中文资源:193](<../ThermalVortex/localization/zhs/cards.json:193>)，键 `THERMALVORTEX-HOPE_FOR_ESCAPE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 按你缺失最大生命的比例，每缺失完整10%抽1张牌；之后直接失去最大生命10%向下取整的生命。

**升级实际效果：** 按你缺失最大生命的比例，每缺失完整10%抽1张牌；之后直接失去最大生命10%向下取整的生命。

**必要条件与实现补充：** 抽牌前快照最大生命（至少1）及当前生命；本卡本次失去生命不增加本次抽牌数。抽牌和生命损失均向下取整，生命损失可致死。

**实现位置：** [HopeForEscape.cs:10](<../ThermalVortexCode/Cards/HopeForEscape.cs:10>)；[HopeForEscape.cs:16](<../ThermalVortexCode/Cards/HopeForEscape.cs:16>)；[HopeForEscape.cs:22](<../ThermalVortexCode/Cards/HopeForEscape.cs:22>)

### 无限泡影 · `InfiniteImpermanence`

内部名称：`THERMALVORTEX-INFINITE_IMPERMANENCE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0，每次使用后本场+1 | 0，每次使用后本场+1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 仅可在本回合尚未打出牌时使用本牌，使1名敌人下次行动无效。  
> 每次打出后，本场战斗该牌费用加1。  

**升级卡面文本：**

> 使1名敌人下次行动无效。  
> 每次打出后，本场战斗该牌费用加1。  

卡文来源：[当前中文资源:73](<../ThermalVortex/localization/zhs/cards.json:73>)，键 `THERMALVORTEX-INFINITE_IMPERMANENCE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本玩家回合尚未打出计数牌时可用。使所选敌人下一次完整行动无效，本牌本场费用增加1。

**升级实际效果：** 取消尚未出牌的使用条件；使所选敌人下一次完整行动无效，本牌本场费用增加1。

**必要条件与实现补充：** 未升级条件在可打出阶段判断；记录器缺失时视为0。跟踪的额外召唤和明确豁免的自动打出不计数。敌人行动无效后推进其状态机并移除能力；未触发时于敌方回合末移除。费用增加属于本牌实体本场修正。

**实现位置：** [InfiniteImpermanence.cs:10](<../ThermalVortexCode/Cards/InfiniteImpermanence.cs:10>)；[InfiniteImpermanence.cs:16](<../ThermalVortexCode/Cards/InfiniteImpermanence.cs:16>)；[TurnCardPlayTrackerPower.cs:18](<../ThermalVortexCode/Powers/TurnCardPlayTrackerPower.cs:18>)；[InfiniteImpermanenceMonsterMovePatch.cs:46](<../ThermalVortexCode/Patches/InfiniteImpermanenceMonsterMovePatch.cs:46>)；[CardPlayCountExemptionPatch.cs:1](<../ThermalVortexCode/Patches/CardPlayCountExemptionPatch.cs:1>)

### 限制解除 · `LimiterRemoval`

内部名称：`THERMALVORTEX-LIMITER_REMOVAL`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 直至回合结束，召唤无怪兽位上限。  

**升级卡面文本：**

> 直至回合结束，召唤无怪兽位上限。  

卡文来源：[当前中文资源:143](<../ThermalVortex/localization/zhs/cards.json:143>)，键 `THERMALVORTEX-LIMITER_REMOVAL.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本方回合结束前召唤不受怪兽位数量限制；回合结束移除效果，恢复容量限制，并从最右侧开始破坏超出容量的怪兽。

**升级实际效果：** 费用1；本方回合结束前召唤不受怪兽位数量限制，结束时恢复限制并从最右侧破坏超额怪兽。

**必要条件与实现补充：** 单一能力不叠加；仅放宽容量，其他可打出条件仍有效。回合末清理由战斗破坏原因执行，可触发相应战斗破坏效果。

**实现位置：** [LimiterRemoval.cs:11](<../ThermalVortexCode/Cards/LimiterRemoval.cs:11>)；[LimiterRemoval.cs:17](<../ThermalVortexCode/Cards/LimiterRemoval.cs:17>)；[LimiterRemovalPower.cs:15](<../ThermalVortexCode/Powers/LimiterRemovalPower.cs:15>)；[MonsterFieldService.cs:268](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:268>)；[MonsterFieldHealthService.cs:281](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:281>)

### 宏观宇宙 · `MacroCosmos`

内部名称：`THERMALVORTEX-MACRO_COSMOS`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 本场战斗，你的所有牌获得消耗。  

**升级卡面文本：**

> 本场战斗，你的所有牌获得消耗。  

卡文来源：[当前中文资源:3](<../ThermalVortex/localization/zhs/cards.json:3>)，键 `THERMALVORTEX-MACRO_COSMOS.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本场战斗给自己的所有战斗卡牌添加消耗，之后进入战斗、生成、移动或打出的己方卡牌也添加消耗。

**升级实际效果：** 本场战斗给自己的所有战斗卡牌添加消耗，之后进入战斗、生成、移动或打出的己方卡牌也添加消耗。

**必要条件与实现补充：** 单实例能力；覆盖手牌、抽牌堆、弃牌堆、消耗堆、打出区和怪兽场。添加的关键词不会在能力移除时由本实现撤回；怪兽带消耗仍先进入怪兽场，按怪兽离场规则改送消耗堆。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MacroCosmos.cs:9](<../ThermalVortexCode/Cards/MacroCosmos.cs:9>)；[MacroCosmosPower.cs:26](<../ThermalVortexCode/Powers/MacroCosmosPower.cs:26>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 魔术礼帽 · `MagicalHats`

内部名称：`THERMALVORTEX-MAGICAL_HATS`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 无 |

**基础卡面文本：**

> 从抽牌堆各选1张能力牌、技能牌及攻击牌，随机将其中1张加入手牌。  
> 消耗。  

**升级卡面文本：**

> 从抽牌堆各选1张能力牌、技能牌及攻击牌，随机将其中1张加入手牌。  

卡文来源：[当前中文资源:130](<../ThermalVortex/localization/zhs/cards.json:130>)，键 `THERMALVORTEX-MAGICAL_HATS.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 从抽牌堆分别选1张能力、1张技能、1张攻击，再随机将其中1张加入手牌。消耗。

**升级实际效果：** 从抽牌堆分别选1张能力、1张技能、1张攻击，再随机将其中1张加入手牌。

**必要条件与实现补充：** 抽牌堆必须同时有三种类型，按能力→技能→攻击选定并最终复核；未选中的两张留在抽牌堆。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MagicalHats.cs:10](<../ThermalVortexCode/Cards/MagicalHats.cs:10>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 增殖的G · `MaxxC`

内部名称：`THERMALVORTEX-MAXX_C`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 2 | 2 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：2。  
> 在场时，敌人每行动一次，抽1张牌。  

**升级卡面文本：**

> 怪兽。生命：2。  
> 在场时，敌人每行动一次，抽1张牌。  

卡文来源：[当前中文资源:23](<../ThermalVortex/localization/zhs/cards.json:23>)，键 `THERMALVORTEX-MAXX_C.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤自身；在场时，敌方每个有效行动分段令你抽1张牌。

**升级实际效果：** 召唤自身；在场时，敌方每个有效行动分段令你抽1张牌。

**必要条件与实现补充：** 仅在该来源仍在场的实际触发分段锁定抽牌数，敌方整次行动结束后统一抽取；来源中途离场保留此前锁定的抽牌，后续分段不再计入。同名多个在场来源累加；被完全无效的行动不进入正常行动抽牌。攻击逐段、实际召唤逐单位、能力／状态／其他被拦截命令逐成功效果计算；未分类且未被实际效果替代的意图在行动结束时作为兜底计数。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MaxxC.cs:10](<../ThermalVortexCode/Cards/MaxxC.cs:10>)；[EnemyActionDrawPower.cs:87](<../ThermalVortexCode/Powers/EnemyActionDrawPower.cs:87>)；[EnemyActionDrawPatch.cs:188](<../ThermalVortexCode/Patches/EnemyActionDrawPatch.cs:188>)；[MonsterFieldDamageGuardPatch.cs:640](<../ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:640>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 千年的伏兵 · `MillenniumCarrier`

内部名称：`THERMALVORTEX-MILLENNIUM_CARRIER`。所属牌池：主牌奖励可选候选；类型：攻击；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 2 | 3 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：2。  
> 造成6点伤害。  

**升级卡面文本：**

> 怪兽。生命：3。  
> 造成10点伤害。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-MILLENNIUM_CARRIER.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 召唤自身，对1名敌人造成6点伤害。

**升级实际效果：** 召唤自身，对1名敌人造成10点伤害。

**必要条件与实现补充：** 本体属于千年卡、千年怪兽。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MillenniumMonsters.cs:144](<../ThermalVortexCode/Cards/MillenniumMonsters.cs:144>)；[MillenniumSeries.cs:50](<../ThermalVortexCode/Cards/MillenniumSeries.cs:50>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 千年契约书 · `MillenniumContractBook`

内部名称：`THERMALVORTEX-MILLENNIUM_CONTRACT_BOOK`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 1 | 1 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 在场时，回合开始获得2点能量，少抽1张牌。  

**升级卡面文本：**

> 怪兽。生命：1。  
> 在场时，回合开始获得2点能量，少抽0张牌。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-MILLENNIUM_CONTRACT_BOOK.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 召唤自身；在场时，每次自己的回合开始获得2能量，回合自然抽牌减少1张。

**升级实际效果：** 召唤自身；在场时，每次自己的回合开始获得2能量，回合自然抽牌减少0张。

**必要条件与实现补充：** 减抽仅作用于 fromHandDraw=true 的自然抽牌，最低0张；不减少卡牌效果抽牌。多个在场来源累加，离场即撤销对应来源。契约书按中文名称归属千年系列：手牌中的每张契约书计1张千年卡，场上的每只计1只千年怪兽；玩家拥有的千年契约书能力计1种千年能力，同名能力即使有多层也只计1，不按来源数量或层数重复增加千年计数。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [NewMainDeckCards.cs:236](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:236>)；[NewCardPowers.cs:53](<../ThermalVortexCode/Powers/NewCardPowers.cs:53>)；[NewCardRulesPatch.cs:18](<../ThermalVortexCode/Patches/NewCardRulesPatch.cs:18>)；[MillenniumSeries.cs:52](<../ThermalVortexCode/Cards/MillenniumSeries.cs:52>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 千年十字 · `MillenniumCross`

内部名称：`THERMALVORTEX-MILLENNIUM_CROSS`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 无 |

**基础卡面文本：**

> 千年计数至少为5时，召唤1只幻之召唤神 艾克佐迪亚。  
> 怪兽位已满时无法使用。  
> 消耗。  

**升级卡面文本：**

> 千年计数至少为5时，召唤1只幻之召唤神 艾克佐迪亚+。  
> 怪兽位已满时无法使用。  

卡文来源：[当前中文资源:198](<../ThermalVortex/localization/zhs/cards.json:198>)，键 `THERMALVORTEX-MILLENNIUM_CROSS.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 千年计数至少5且有合法怪兽位时，生成并立即打出幻之召唤神 艾克佐迪亚，为新生成的神记录本次使用前锁定的千年计数N；神对所有敌人造成N点无强化伤害，共N段。消耗。

**升级实际效果：** 千年计数至少5且有合法怪兽位时，生成并立即打出幻之召唤神 艾克佐迪亚，为新生成的神记录本次使用前锁定的千年计数N；神对所有敌人造成N点无强化伤害，共N段。

**显示补充：** 战斗中查看卡牌时，黄色“千年计数”解释末尾实时显示当前拥有的千年计数，卡面不另列当前值；无战斗状态时不显示占位0。

**必要条件与实现补充：** 千年计数=手牌中千年卡数量+场上千年怪兽数量+玩家拥有的千年能力种数；同名能力不论层数只计1。系列按中文标准名称含“千年”确定，切换显示语言不改变归属；千年契约书计入，幻之召唤神 艾克佐迪亚本身不计入。使用前锁定包含手牌中本张十字的千年计数；该牌离手及本次抽弃牌、怪兽入场或能力变化均不改变本次锁值。满场时整张十字不可打出，不支付费用、不生成神或造成伤害；手动和自动打出均检查空位，结算前再次复查。每次打出十字都在使用前重新锁定，只给本次新生成的神记录新数值，已经生成的神保持原记录。生成神匹配十字升级，伤害系数不变，基础与升级神均带消耗。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。 神只提交一个基础段数为N的原生群攻命令，敌人数不增加段数；仍接受原生攻击次数修正，无存活目标时可提前结束。未计格挡、伤害及次数修正时，承受全部N段的单名敌人理论总伤害为N²。

**计数示例：** 第一次打出十字时计数为5，新生成的神造成5点伤害5次；之后计数变为7，再打出十字时新生成的神造成7点伤害7次，之前那只仍记录5。

**实现位置：** [MillenniumCross.cs:13](<../ThermalVortexCode/Cards/MillenniumCross.cs:13>)；[MillenniumSeries.cs:52](<../ThermalVortexCode/Cards/MillenniumSeries.cs:52>)；[MillenniumCross.cs:86](<../ThermalVortexCode/Cards/MillenniumCross.cs:86>)；[SummonRulesPower.cs:144](<../ThermalVortexCode/Powers/SummonRulesPower.cs:144>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[MillenniumCross.cs:96](<../ThermalVortexCode/Cards/MillenniumCross.cs:96>)；[MillenniumCross.cs:125](<../ThermalVortexCode/Cards/MillenniumCross.cs:125>)；[ThermalVortexCombatVfx.cs:31](<../ThermalVortexCode/Vfx/ThermalVortexCombatVfx.cs:31>)

### 千年守墓人 · `MillenniumGravekeeper`

内部名称：`THERMALVORTEX-MILLENNIUM_GRAVEKEEPER`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 4 | 6 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：4。  
> 获得4点格挡。  

**升级卡面文本：**

> 怪兽。生命：6。  
> 获得6点格挡。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-MILLENNIUM_GRAVEKEEPER.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 召唤自身，获得4格挡。

**升级实际效果：** 召唤自身，获得6格挡。

**必要条件与实现补充：** 本体属于千年卡、千年怪兽。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MillenniumMonsters.cs:117](<../ThermalVortexCode/Cards/MillenniumMonsters.cs:117>)；[MillenniumSeries.cs:50](<../ThermalVortexCode/Cards/MillenniumSeries.cs:50>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 千年供奉 · `MillenniumOffering`

内部名称：`THERMALVORTEX-MILLENNIUM_OFFERING`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 用2只场上怪兽作素材，选择1张被封印千年部件加入手牌。  

**升级卡面文本：**

> 用2只场上怪兽作素材，选择1张被封印千年部件加入手牌。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-MILLENNIUM_OFFERING.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 成功支付自身场上2只怪兽作为素材后，从五种部件中自选1张加入手牌。

**升级实际效果：** 成功支付自身场上2只怪兽作为素材后，从五种部件中自选1张加入手牌。

**必要条件与实现补充：** 必须有至少2只自身场上的合法素材；必须选满2只，选择后逐只复查实体，两只素材均支付成功且战斗仍有效才自选部件。素材正常走原素材去向，合法复活不撤销支付；部件不能升级。本卡无消耗，升级只减费用。

**实现位置：** [MillenniumOffering.cs:14](<../ThermalVortexCode/Cards/MillenniumOffering.cs:14>)；[MillenniumOffering.cs:25](<../ThermalVortexCode/Cards/MillenniumOffering.cs:25>)；[MillenniumSeries.cs:97](<../ThermalVortexCode/Cards/MillenniumSeries.cs:97>)；[MonsterFieldService.cs:569](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:569>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 千年的伙伴 · `MillenniumPartner`

内部名称：`THERMALVORTEX-MILLENNIUM_PARTNER`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 回合开始时，将1张随机怪兽加入手牌。  

**升级卡面文本：**

> 回合开始时，将1张随机怪兽加入手牌。  

卡文来源：[当前中文资源:215](<../ThermalVortex/localization/zhs/cards.json:215>)，键 `THERMALVORTEX-MILLENNIUM_PARTNER.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 获得能力：每次自己的回合开始，随机生成1张武藤游戏可选主牌奖励候选中的怪兽加入手牌。

**升级实际效果：** 获得能力：每次自己的回合开始，随机生成1张武藤游戏可选主牌奖励候选中的怪兽加入手牌。

**必要条件与实现补充：** 单实例，不按重复打出次数增加生成量。候选来自角色完整主牌奖励候选中的 MonsterCard，不受本局手选奖励池子集限制；排除固定起始牌、仅生成、先古专属和额外牌。生成未升级怪兽。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MillenniumMonsters.cs:210](<../ThermalVortexCode/Cards/MillenniumMonsters.cs:210>)；[MillenniumPowers.cs:63](<../ThermalVortexCode/Powers/MillenniumPowers.cs:63>)；[MillenniumSeries.cs:124](<../ThermalVortexCode/Cards/MillenniumSeries.cs:124>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 千年王朝之盾 · `MillenniumShield`

内部名称：`THERMALVORTEX-MILLENNIUM_SHIELD`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 15 | 20 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：15。  

**升级卡面文本：**

> 怪兽。生命：20。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-MILLENNIUM_SHIELD.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 召唤自身。

**升级实际效果：** 召唤自身。

**必要条件与实现补充：** 本体属于千年卡、千年怪兽。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MillenniumMonsters.cs:16](<../ThermalVortexCode/Cards/MillenniumMonsters.cs:16>)；[MillenniumSeries.cs:50](<../ThermalVortexCode/Cards/MillenniumSeries.cs:50>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 千年沉睡石板 · `MillenniumSleepingTablet`

内部名称：`THERMALVORTEX-MILLENNIUM_SLEEPING_TABLET`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 1 | 1 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 下个己方回合开始时，若自身仍在场，进入弃牌堆，将1张随机被封印千年部件加入手牌。  

**升级卡面文本：**

> 怪兽。生命：1。  
> 下个己方回合开始时，若自身仍在场，进入弃牌堆，将1张自选被封印千年部件加入手牌。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-MILLENNIUM_SLEEPING_TABLET.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 召唤1生命怪兽；下个己方回合开始时若仍在场，原实体进入弃牌堆，将1个随机部件加入手牌。

**升级实际效果：** 召唤1生命怪兽；下个己方回合开始时若仍在场，原实体进入弃牌堆，将1个自选部件加入手牌。

**必要条件与实现补充：** 下个己方回合开始时，仍在场的原牌进入弃牌堆，并将1个部件加入手牌。普通版从五种部件等概率随机，升级版自选。该离场不是素材支付或消耗；完整复制可以继承此转化，吞噬不能。 原实体可重新抽到再使用；再次入场重新预约。普通版允许生成已有部件，无最少数量偏向。完整复制转化的是复制宿主，其离场按正常复制清理规则处理。旧生命光环已移除。

**实现位置：** [MillenniumMonsters.cs:171](<../ThermalVortexCode/Cards/MillenniumMonsters.cs:171>)；[MillenniumPowers.cs:136](<../ThermalVortexCode/Powers/MillenniumPowers.cs:136>)；[MillenniumSeries.cs:143](<../ThermalVortexCode/Cards/MillenniumSeries.cs:143>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 千年的石板 · `MillenniumTablet`

内部名称：`THERMALVORTEX-MILLENNIUM_TABLET`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 选择1张被封印千年部件加入手牌。  

**升级卡面文本：**

> 选择1张被封印千年部件加入手牌。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-MILLENNIUM_TABLET.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 立即从五种部件中自选1张加入手牌。

**升级实际效果：** 立即从五种部件中自选1张加入手牌。

**必要条件与实现补充：** 无需消耗手牌，也不生成持续能力；卡牌无消耗，使用后正常进入弃牌堆。升级只减费用，生成部件不可升级。五个选择项均为当前战斗创建的未入堆实例，选中的同一实例加入手牌；不把仅供数据库／图鉴读取的规范牌对象送入战斗选择流程。

**实现位置：** [MillenniumTablet.cs:10](<../ThermalVortexCode/Cards/MillenniumTablet.cs:10>)；[MillenniumPowers.cs:38](<../ThermalVortexCode/Powers/MillenniumPowers.cs:38>)；[MillenniumSeries.cs:97](<../ThermalVortexCode/Cards/MillenniumSeries.cs:97>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 千年神殿 · `MillenniumTemple`

内部名称：`THERMALVORTEX-MILLENNIUM_TEMPLE`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 回合结束时，获得千年计数×2点格挡。  

**升级卡面文本：**

> 回合结束时，获得千年计数×3点格挡。  

卡文来源：[当前中文资源:195](<../ThermalVortex/localization/zhs/cards.json:195>)，键 `THERMALVORTEX-MILLENNIUM_TEMPLE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 获得能力：每次己方回合结束时，获得当前千年计数×2的无强化格挡。

**升级实际效果：** 获得能力：每次己方回合结束时，获得当前千年计数×3的无强化格挡。

**显示补充：** 战斗中，卡牌与已生效能力附带的黄色“千年计数”解释末尾实时显示当前拥有的千年计数，卡面与能力正文不另列当前值；无战斗状态时不显示占位0。

**必要条件与实现补充：** 千年计数=手牌中千年卡数量+场上千年怪兽数量+玩家身上实现 IMillenniumPower 的能力实例种数；能力层数不乘算。神殿自身能力计1个千年能力。回合末能力每次触发时读取当时的计数，不固定为施放神殿卡时的数值。单实例，重复施放取已生效与本张系数较大值，不把系数叠加。回合末格挡保持无强化；励辉士专属本回合锁有效时不会获得。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MillenniumTemple.cs:10](<../ThermalVortexCode/Cards/MillenniumTemple.cs:10>)；[MillenniumPowers.cs:17](<../ThermalVortexCode/Powers/MillenniumPowers.cs:17>)；[MillenniumSeries.cs:50](<../ThermalVortexCode/Cards/MillenniumSeries.cs:50>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 千年宝物守护巨像 · `MillenniumTreasureGolem`

内部名称：`THERMALVORTEX-MILLENNIUM_TREASURE_GOLEM`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：5。  
> 在场时，回合开始给予所有敌人1层虚弱。  

**升级卡面文本：**

> 怪兽。生命：5。  
> 在场时，回合开始给予所有敌人2层虚弱。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-MILLENNIUM_TREASURE_GOLEM.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 召唤自身；在场时，每次自己的回合开始给所有可命中敌人1层虚弱。

**升级实际效果：** 召唤自身；在场时，每次自己的回合开始给所有可命中敌人2层虚弱。

**必要条件与实现补充：** 普通本体同名在场巨像的虚弱量求和；能力下次回合开始查无在场巨像则移除。本体属于千年卡、千年怪兽，持续能力属于千年能力。吞噬后的继承效果由继承系统执行。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MillenniumMonsters.cs:41](<../ThermalVortexCode/Cards/MillenniumMonsters.cs:41>)；[MillenniumPowers.cs:82](<../ThermalVortexCode/Powers/MillenniumPowers.cs:82>)；[MillenniumSeries.cs:50](<../ThermalVortexCode/Cards/MillenniumSeries.cs:50>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 奇迹融合 · `MiracleFusion`

内部名称：`THERMALVORTEX-MIRACLE_FUSION`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 消耗场上或弃牌堆的怪兽作为融合素材，融合召唤1只额外牌组怪兽。  

**升级卡面文本：**

> 消耗场上或弃牌堆的怪兽作为融合素材，融合召唤1只额外牌组怪兽。  

卡文来源：[当前中文资源:39](<../ThermalVortex/localization/zhs/cards.json:39>)，键 `THERMALVORTEX-MIRACLE_FUSION.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 以场上与弃牌堆的合法怪兽为素材，消耗这些素材，召唤1张额外牌组怪兽并结算其效果。

**升级实际效果：** 以场上与弃牌堆的合法怪兽为素材，消耗这些素材，召唤1张额外牌组怪兽并结算其效果。

**必要条件与实现补充：** 须持有角色核心遗物且额外牌组有满足条件的怪兽，材料必须满足目标专属要求，移走材料后有合法位置。额外怪兽不额外支付能量。本召唤方式不能选择拉之翼神龙-球形体。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MiracleFusion.cs:9](<../ThermalVortexCode/Cards/MiracleFusion.cs:9>)；[ThermalVortexCore.cs:2899](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2899>)；[ThermalVortexCore.cs:2924](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2924>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 神圣防护罩 反射镜力 · `MirrorForce`

内部名称：`THERMALVORTEX-MIRROR_FORCE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 保留、消耗 | 保留、消耗 |

**基础卡面文本：**

> 保留。  
> 反弹本回合所有敌人的攻击伤害。  
> 消耗。  

**升级卡面文本：**

> 保留。  
> 反弹本回合所有敌人的攻击伤害。  
> 消耗。  

卡文来源：[当前中文资源:61](<../ThermalVortex/localization/zhs/cards.json:61>)，键 `THERMALVORTEX-MIRROR_FORCE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 保留。消耗。本回合内，无效所有敌人每次含攻击意图的行动，并分别对攻击者造成等于该次预告攻击总伤害的无强化伤害。

**升级实际效果：** 保留。消耗。本回合内，无效所有敌人每次含攻击意图的行动，并分别对攻击者造成等于该次预告攻击总伤害的无强化伤害。

**显示补充：** 卡面“反弹”标黄并附解释：“使敌人的攻击行动无效，并对攻击者造成等同于该次攻击总伤害的伤害。多段攻击按总伤害计算。”

**必要条件与实现补充：** 单实例，触发后持续保留，覆盖每名敌人的每次攻击行动，直到敌方回合结束移除。取消的是包含攻击的整次行动，反射值是该行动全部 AttackIntent 对玩家目标计算的总伤害（含多段），不等于玩家实际失血；存在更高优先级的控制／无效时可能先由它们处理。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MirrorForce.cs:11](<../ThermalVortexCode/Cards/MirrorForce.cs:11>)；[MirrorForcePower.cs:9](<../ThermalVortexCode/Powers/MirrorForcePower.cs:9>)；[InfiniteImpermanenceMonsterMovePatch.cs:134](<../ThermalVortexCode/Patches/InfiniteImpermanenceMonsterMovePatch.cs:134>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 死者苏生 · `MonsterReborn`

内部名称：`THERMALVORTEX-MONSTER_REBORN`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 将弃牌堆中的1张怪兽加入手牌，本回合费用为0。  
> 消耗。  

**升级卡面文本：**

> 将弃牌堆中的1张怪兽加入手牌，本回合费用为0。  
> 消耗。  

卡文来源：[当前中文资源:65](<../ThermalVortex/localization/zhs/cards.json:65>)，键 `THERMALVORTEX-MONSTER_REBORN.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择弃牌堆1张怪兽加入手牌，并使该实体本回合免费。消耗。

**升级实际效果：** 选择弃牌堆1张怪兽加入手牌，并使该实体本回合免费。消耗。

**必要条件与实现补充：** 必须有弃牌堆怪兽；不从消耗堆选择，不直接召唤，不复制卡牌。移动成功且同一卡实体确实进入手牌后才设置免费。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MonsterReborn.cs:13](<../ThermalVortexCode/Cards/MonsterReborn.cs:13>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 王车易位 · `MonsterSwap`

内部名称：`THERMALVORTEX-MONSTER_SWAP`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 交换2只场上怪兽的位置。  

**升级卡面文本：**

> 交换2只场上怪兽的位置。  

卡文来源：[当前中文资源:163](<../ThermalVortex/localization/zhs/cards.json:163>)，键 `THERMALVORTEX-MONSTER_SWAP.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择场上2只怪兽，互换它们的位置。

**升级实际效果：** 选择场上2只怪兽，互换它们的位置。

**必要条件与实现补充：** 场上至少2只怪兽；这属于卡牌效果移动。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MonsterSwap.cs:10](<../ThermalVortexCode/Cards/MonsterSwap.cs:10>)；[MonsterFieldService.cs:664](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:664>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 多多密友 呼哇罗丝 · `MulcharmyFuwalos`

内部名称：`THERMALVORTEX-MULCHARMY_FUWALOS`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 0 |
| 怪兽生命 | 4 | 4 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：4。  
> 在场时，敌人每段攻击使你抽1张牌。  

**升级卡面文本：**

> 怪兽。生命：4。  
> 在场时，敌人每段攻击使你抽1张牌。  

卡文来源：[当前中文资源:25](<../ThermalVortex/localization/zhs/cards.json:25>)，键 `THERMALVORTEX-MULCHARMY_FUWALOS.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤自身；在场时，敌方向你结算每一段攻击均令你抽1张牌。

**升级实际效果：** 召唤自身；在场时，敌方向你结算每一段攻击均令你抽1张牌。

**必要条件与实现补充：** 仅在该来源仍在场的实际触发分段锁定抽牌数，敌方整次行动结束后统一抽取；来源中途离场保留此前锁定的抽牌，后续分段不再计入。同名多个在场来源累加；被完全无效的行动不进入正常行动抽牌。在该段扣除格挡与怪兽生命前记录，故本身被该段击毁仍可产生该段抽牌；攻击被格挡挡完不取消已发生分段。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MulcharmyFuwalos.cs:10](<../ThermalVortexCode/Cards/MulcharmyFuwalos.cs:10>)；[EnemyActionDrawPower.cs:87](<../ThermalVortexCode/Powers/EnemyActionDrawPower.cs:87>)；[EnemyActionDrawPatch.cs:188](<../ThermalVortexCode/Patches/EnemyActionDrawPatch.cs:188>)；[MonsterFieldDamageGuardPatch.cs:640](<../ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:640>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 多多密友 喵喵露丝 · `MulcharmyMeowls`

内部名称：`THERMALVORTEX-MULCHARMY_MEOWLS`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 0 |
| 怪兽生命 | 7 | 7 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：7。  
> 在场时，每有1名敌人被召唤，你抽1张牌。  

**升级卡面文本：**

> 怪兽。生命：7。  
> 在场时，每有1名敌人被召唤，你抽1张牌。  

卡文来源：[当前中文资源:29](<../ThermalVortex/localization/zhs/cards.json:29>)，键 `THERMALVORTEX-MULCHARMY_MEOWLS.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤自身；在场时，每有1名敌人实际被召唤，令你抽1张牌。

**升级实际效果：** 召唤自身；在场时，每有1名敌人实际被召唤，令你抽1张牌。

**必要条件与实现补充：** 仅在该来源仍在场的实际触发分段锁定抽牌数，敌方整次行动结束后统一抽取；来源中途离场保留此前锁定的抽牌，后续分段不再计入。同名多个在场来源累加；被完全无效的行动不进入正常行动抽牌。按实际召唤并成功加入战斗的敌人数计算，不按一个召唤意图只计1次；一次召唤2名敌人即触发2次。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MulcharmyMeowls.cs:10](<../ThermalVortexCode/Cards/MulcharmyMeowls.cs:10>)；[EnemyActionDrawPower.cs:87](<../ThermalVortexCode/Powers/EnemyActionDrawPower.cs:87>)；[EnemyActionDrawPatch.cs:188](<../ThermalVortexCode/Patches/EnemyActionDrawPatch.cs:188>)；[MonsterFieldDamageGuardPatch.cs:640](<../ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:640>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 多多密友 噗噜利亚 · `MulcharmyPurulia`

内部名称：`THERMALVORTEX-MULCHARMY_PURULIA`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 0 |
| 怪兽生命 | 7 | 7 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：7。  
> 在场时，敌人每次强化、给你施加负面效果或添加状态牌，你抽1张牌。  

**升级卡面文本：**

> 怪兽。生命：7。  
> 在场时，敌人每次强化、给你施加负面效果或添加状态牌，你抽1张牌。  

卡文来源：[当前中文资源:27](<../ThermalVortex/localization/zhs/cards.json:27>)，键 `THERMALVORTEX-MULCHARMY_PURULIA.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤自身；在场时，敌方每成功产生一次受记录的非攻击效果，令你抽1张牌。

**升级实际效果：** 召唤自身；在场时，敌方每成功产生一次受记录的非攻击效果，令你抽1张牌。

**必要条件与实现补充：** 仅在该来源仍在场的实际触发分段锁定抽牌数，敌方整次行动结束后统一抽取；来源中途离场保留此前锁定的抽牌，后续分段不再计入。同名多个在场来源累加；被完全无效的行动不进入正常行动抽牌。包括给敌人增益、给自己减益、加入状态牌，以及补丁覆盖的敌方格挡／治疗／生命提升和对玩家移除格挡／减少生命上限／眩晕等。按实际成功效果分段；不是只看意图图标，也不把攻击正常消耗格挡算成另一效果。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [MulcharmyPurulia.cs:10](<../ThermalVortexCode/Cards/MulcharmyPurulia.cs:10>)；[EnemyActionDrawPower.cs:87](<../ThermalVortexCode/Powers/EnemyActionDrawPower.cs:87>)；[EnemyActionDrawPatch.cs:188](<../ThermalVortexCode/Patches/EnemyActionDrawPatch.cs:188>)；[MonsterFieldDamageGuardPatch.cs:640](<../ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:640>)；[SolemnStrikeNonDamagePatch.cs:16](<../ThermalVortexCode/Patches/SolemnStrikeNonDamagePatch.cs:16>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 过热电圈 · `OverheatedCoil`

内部名称：`THERMALVORTEX-OVERHEATED_COIL`。所属牌池：主牌奖励可选候选；类型：攻击；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 2 | 2 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：2。  
> 造成5点伤害。场上有磁圈时，重复1次。  

**升级卡面文本：**

> 怪兽。生命：2。  
> 造成7点伤害。场上有磁圈时，重复1次。  

卡文来源：[当前中文资源:219](<../ThermalVortex/localization/zhs/cards.json:219>)，键 `THERMALVORTEX-OVERHEATED_COIL.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 对1名敌人造成5点伤害；若场上或待入场区已有磁圈类怪兽，改为攻击2次。随后召唤自身。

**升级实际效果：** 对1名敌人造成7点伤害；若场上或待入场区已有磁圈类怪兽，改为攻击2次。随后召唤自身。

**必要条件与实现补充：** 身份为电圈类 FirePoleCard；自身只召唤自己，没有调用电磁圈基类的联动召唤。磁圈条件按 ThunderPoleCard 实际身份判定。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [NewMainDeckCards.cs:18](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:18>)；[PoleMonsterCard.cs:88](<../ThermalVortexCode/Cards/PoleMonsterCard.cs:88>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 过载抽取 · `OverloadDraw`

内部名称：`THERMALVORTEX-OVERLOAD_DRAW`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 抽2张牌，将1张炉渣加入弃牌堆。  

**升级卡面文本：**

> 抽3张牌，将1张炉渣+加入弃牌堆。  

卡文来源：[当前中文资源:233](<../ThermalVortex/localization/zhs/cards.json:233>)，键 `THERMALVORTEX-OVERLOAD_DRAW.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 抽2张牌，在弃牌堆生成1张炉渣。

**升级实际效果：** 抽3张牌，在弃牌堆生成1张炉渣。

**必要条件与实现补充：** 先抽牌后生成炉渣；生成炉渣匹配本卡可实现的升级等级，炉渣自身升级不改变效果。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [NewMainDeckCards.cs:195](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:195>)；[ThermalVortexGeneratedCards.cs:22](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:22>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 超载怪兽位 · `OverloadedSummonSlot`

内部名称：`THERMALVORTEX-OVERLOADED_SUMMON_SLOT`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 本回合增加1个怪兽位。回合结束时，破坏最右侧2个怪兽位。  

**升级卡面文本：**

> 本回合增加2个怪兽位。回合结束时，破坏最右侧2个怪兽位。  

卡文来源：[当前中文资源:243](<../ThermalVortex/localization/zhs/cards.json:243>)，键 `THERMALVORTEX-OVERLOADED_SUMMON_SLOT.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本回合增加1个怪兽位。己方回合结束时，毁掉最右侧2个怪兽位及其中怪兽；撤去本卡临时增加的怪兽位，实际额外损失保留至本战斗。

**升级实际效果：** 本回合增加2个怪兽位。己方回合结束时，毁掉最右侧2个怪兽位及其中怪兽；撤去本卡临时增加的怪兽位，实际额外损失保留至本战斗。

**必要条件与实现补充：** 每次施放单独登记一次回合末结算，多次依次执行；右端空位也计入2个怪兽位。单次销毁合法怪兽位数最多为当时容量，不产生负容量。若容量充足，一次基础版净减1个怪兽位、升级版净变化为0；实际净变化取决于可销毁容量。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [NewMainDeckCards.cs:326](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:326>)；[NewCardPowers.cs:157](<../ThermalVortexCode/Powers/NewCardPowers.cs:157>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 大欲之壶 · `PotOfAvarice`

内部名称：`THERMALVORTEX-POT_OF_AVARICE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 3 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 将消耗堆中的2张怪兽牌洗回牌组，抽1张牌。  

**升级卡面文本：**

> 将消耗堆中的3张怪兽牌洗回牌组，抽1张牌。  

卡文来源：[当前中文资源:160](<../ThermalVortex/localization/zhs/cards.json:160>)，键 `THERMALVORTEX-POT_OF_AVARICE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择消耗堆恰好2张怪兽，主牌怪兽随机插回抽牌堆，额外怪兽返回本场额外牌组；全部实际回收成功后抽1张牌。

**升级实际效果：** 选择消耗堆恰好3张怪兽，主牌怪兽随机插回抽牌堆，额外怪兽返回本场额外牌组；全部实际回收成功后抽1张牌。

**必要条件与实现补充：** 消耗堆符合条件怪兽不足所需数时不可打出；回收对应实体或额外条目，保留原卡升级、附魔与本场吞噬成长，不新增永久拥有副本，不将全部抽牌堆重新洗牌。每张回收前复查原战斗与消耗堆实体；全部实际回收成功且原战斗仍有效才抽牌。升级增加必须回收数，费用与抽牌数保持不变。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [PotOfAvarice.cs:11](<../ThermalVortexCode/Cards/PotOfAvarice.cs:11>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 强欲而贪婪之壶 · `PotOfDesires`

内部名称：`THERMALVORTEX-POT_OF_DESIRES`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 消耗抽牌堆顶的10张牌，抽2张牌。  

**升级卡面文本：**

> 消耗抽牌堆顶的10张牌，抽2张牌。  

卡文来源：[当前中文资源:55](<../ThermalVortex/localization/zhs/cards.json:55>)，键 `THERMALVORTEX-POT_OF_DESIRES.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 消耗抽牌堆顶部10张牌，然后抽2张牌。

**升级实际效果：** 消耗抽牌堆顶部10张牌，然后抽2张牌。

**必要条件与实现补充：** 抽牌堆与弃牌堆可进入抽牌堆的普通牌合计至少10张才能打出，弃牌堆额外怪兽不计入。若抽牌堆不足10张，结算时保持现有抽牌堆顺序，先将弃牌堆额外怪兽归还本场额外牌组，再将其余弃牌洗混后补至抽牌堆底部，核对并消耗此时顶部10张；可用牌合计不足10张仍不能打出。每次结算只补洗一次；之后抽2张仍遵循游戏的普通抽牌规则。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [PotOfDesires.cs:9](<../ThermalVortexCode/Cards/PotOfDesires.cs:9>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 强欲而金满之壶 · `PotOfExtravagance`

内部名称：`THERMALVORTEX-POT_OF_EXTRAVAGANCE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 随机消耗3张额外牌组卡牌，抽3张牌。  

**升级卡面文本：**

> 随机消耗3张额外牌组卡牌，抽3张牌。  

卡文来源：[当前中文资源:57](<../ThermalVortex/localization/zhs/cards.json:57>)，键 `THERMALVORTEX-POT_OF_EXTRAVAGANCE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 随机消耗额外牌组剩余的3张牌；成功消耗全部3张后抽3张牌。

**升级实际效果：** 随机消耗额外牌组剩余的3张牌；成功消耗全部3张后抽3张牌。

**必要条件与实现补充：** 需持有角色核心且额外牌组剩余至少3张。逐次随机选择并从本场额外牌组移除，生成对应战斗实体进入消耗堆，可触发消耗相关效果；不永久删除局内额外牌组存档。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [PotOfExtravagance.cs:10](<../ThermalVortexCode/Cards/PotOfExtravagance.cs:10>)；[ThermalVortexCore.cs:736](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:736>)；[ThermalVortexCore.cs:1930](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:1930>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 强欲之壶 · `PotOfGreed`

内部名称：`THERMALVORTEX-POT_OF_GREED`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 抽2张牌，获得1点能量。  
> 消耗。  

**升级卡面文本：**

> 抽2张牌，获得2点能量。  
> 消耗。  

卡文来源：[当前中文资源:53](<../ThermalVortex/localization/zhs/cards.json:53>)，键 `THERMALVORTEX-POT_OF_GREED.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 抽2张牌，获得1能量。消耗。

**升级实际效果：** 抽2张牌，获得2能量。消耗。

**必要条件与实现补充：**  登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [PotOfGreed.cs:9](<../ThermalVortexCode/Cards/PotOfGreed.cs:9>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 保护核心 · `ProtectCore`

内部名称：`THERMALVORTEX-PROTECT_CORE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 将1只场上怪兽移至最左侧。  

**升级卡面文本：**

> 将1只场上怪兽移至最左侧。  

卡文来源：[当前中文资源:245](<../ThermalVortex/localization/zhs/cards.json:245>)，键 `THERMALVORTEX-PROTECT_CORE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 选择场上1只怪兽，移动到最左端。

**升级实际效果：** 选择场上1只怪兽，移动到最左端。

**必要条件与实现补充：** 至少1只场上怪兽才能打出；最左侧处于承伤顺序末端，属于卡牌效果移动。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [NewMainDeckCards.cs:355](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:355>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)；[MonsterFieldHealthService.cs:302](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:302>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 生死与共 · `SharedFate`

内部名称：`THERMALVORTEX-SHARED_FATE`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽将要受到致命伤害时，其余怪兽将与其分摊伤害。  

**升级卡面文本：**

> 怪兽将要受到致命伤害时，其余怪兽将与其分摊伤害。  

卡文来源：[当前中文资源:248](<../ThermalVortex/localization/zhs/cards.json:248>)，键 `THERMALVORTEX-SHARED_FATE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 获得能力：敌方攻击由右向左经过怪兽时，只要左侧还有当前生命大于0且可承伤的后备怪兽，本只最多承受到剩1生命，其余伤害继续传给左侧。

**升级实际效果：** 获得能力：敌方攻击由右向左经过怪兽时，只要左侧还有当前生命大于0且可承伤的后备怪兽，本只最多承受到剩1生命，其余伤害继续传给左侧。

**必要条件与实现补充：** 单实例。最后一只能承伤的存活怪兽仍可被击毁，其后剩余伤害交给玩家；不为卡牌直接伤害怪兽或直接摧毁效果保留1生命。不能承伤的法拉不算后备。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [NewMainDeckCards.cs:382](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:382>)；[NewCardPowers.cs:301](<../ThermalVortexCode/Powers/NewCardPowers.cs:301>)；[MonsterFieldHealthService.cs:302](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:302>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 地盘沉下 · `SinkingLand`

内部名称：`THERMALVORTEX-SINKING_LAND`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 破坏最右侧怪兽位。  
> 抽2张牌，获得1点能量。  

**升级卡面文本：**

> 破坏最右侧怪兽位。  
> 抽2张牌，获得2点能量。  

卡文来源：[当前中文资源:239](<../ThermalVortex/localization/zhs/cards.json:239>)，键 `THERMALVORTEX-SINKING_LAND.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 本战斗减少1个怪兽位，清除超出容量的最右怪兽，然后抽2张牌、获得1能量。

**升级实际效果：** 本战斗减少1个怪兽位，清除超出容量的最右怪兽，然后抽2张牌、获得2能量。

**必要条件与实现补充：** 必须至少有1个真实显示容量，投影或临时放置保留不作为可支付位置；容量为0时自动打出也不产生抽牌／能量收益。可多次叠加。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [NewMainDeckCards.cs:274](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:274>)；[NewCardPowers.cs:143](<../ThermalVortexCode/Powers/NewCardPowers.cs:143>)；[MonsterFieldHealthService.cs:281](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:281>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 神之宣告 · `SolemnJudgment`

内部名称：`THERMALVORTEX-SOLEMN_JUDGMENT`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗、保留 |

**基础卡面文本：**

> 失去一半当前生命。  
> 使所有敌人本回合的行动无效。  
> 消耗。  

**升级卡面文本：**

> 保留。  
> 失去一半当前生命。  
> 使所有敌人本回合的行动无效。  
> 消耗。  

卡文来源：[当前中文资源:47](<../ThermalVortex/localization/zhs/cards.json:47>)，键 `THERMALVORTEX-SOLEMN_JUDGMENT.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 支付当前生命的一半（向下取整），使此时所有敌人的行动无效，持续到敌方回合结束。消耗。

**升级实际效果：** 保留。支付当前生命的一半（向下取整），使此时所有敌人的行动无效，持续到敌方回合结束。消耗。

**必要条件与实现补充：** 生命支付直接设置当前生命，不是伤害且不走格挡／怪兽承伤；必须支付后至少剩1生命，当前1生命时支付0。仅施加给结算时已存在的敌人，不覆盖之后新召唤者；持续期内可无效该敌人多次行动。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [SolemnJudgment.cs:11](<../ThermalVortexCode/Cards/SolemnJudgment.cs:11>)；[SolemnCard.cs:26](<../ThermalVortexCode/Cards/SolemnCard.cs:26>)；[SolemnJudgmentPower.cs:9](<../ThermalVortexCode/Powers/SolemnJudgmentPower.cs:9>)；[InfiniteImpermanenceMonsterMovePatch.cs:39](<../ThermalVortexCode/Patches/InfiniteImpermanenceMonsterMovePatch.cs:39>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 神之通告 · `SolemnStrike`

内部名称：`THERMALVORTEX-SOLEMN_STRIKE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗、保留 |

**基础卡面文本：**

> 失去4点生命。  
> 使1名敌人下次行动中的非伤害效果无效。  
> 消耗。  

**升级卡面文本：**

> 保留。  
> 失去4点生命。  
> 使1名敌人下次行动中的非伤害效果无效。  
> 消耗。  

卡文来源：[当前中文资源:51](<../ThermalVortex/localization/zhs/cards.json:51>)，键 `THERMALVORTEX-SOLEMN_STRIKE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 支付4生命，无效指定敌人下一次行动的非攻击部分。消耗。

**升级实际效果：** 保留。支付4生命，无效指定敌人下一次行动的非攻击部分。消耗。

**必要条件与实现补充：** 需至少5当前生命。下一行动无攻击意图则整次取消；含攻击意图则保留攻击，拦截补丁覆盖的增益、减益、状态／诅咒、格挡、治疗、生命上限变化、召唤、削减格挡及眩晕等非攻击效果；处理该次行动后移除。没有统一敌方回合末未触发过期逻辑。支付直接扣生命。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**结算补充：** 混合攻击中的偷牌效果在删除战斗牌和永久牌之前被阻止，攻击部分照常结算；不再依赖取消用于归还卡牌的原生能力来阻止偷牌。

**实现位置：** [SolemnStrike.cs:11](<../ThermalVortexCode/Cards/SolemnStrike.cs:11>)；[SolemnCard.cs:26](<../ThermalVortexCode/Cards/SolemnCard.cs:26>)；[SolemnStrikePower.cs:10](<../ThermalVortexCode/Powers/SolemnStrikePower.cs:10>)；[InfiniteImpermanenceMonsterMovePatch.cs:68](<../ThermalVortexCode/Patches/InfiniteImpermanenceMonsterMovePatch.cs:68>)；[SolemnStrikeNonDamagePatch.cs:16](<../ThermalVortexCode/Patches/SolemnStrikeNonDamagePatch.cs:16>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 神之警告 · `SolemnWarning`

内部名称：`THERMALVORTEX-SOLEMN_WARNING`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗、保留 |

**基础卡面文本：**

> 失去6点生命。  
> 使1名敌人本回合的下次攻击行动无效。  
> 消耗。  

**升级卡面文本：**

> 保留。  
> 失去6点生命。  
> 使1名敌人本回合的下次攻击行动无效。  
> 消耗。  

卡文来源：[当前中文资源:49](<../ThermalVortex/localization/zhs/cards.json:49>)，键 `THERMALVORTEX-SOLEMN_WARNING.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 支付6生命，无效指定敌人的下一次含攻击意图的行动。消耗。

**升级实际效果：** 保留。支付6生命，无效指定敌人的下一次含攻击意图的行动。消耗。

**必要条件与实现补充：** 需至少7当前生命，直接扣生命。取消的是该次混合行动整体，不仅攻击段；触发后移除，未触发也在该敌方回合结束移除。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [SolemnWarning.cs:11](<../ThermalVortexCode/Cards/SolemnWarning.cs:11>)；[SolemnCard.cs:26](<../ThermalVortexCode/Cards/SolemnCard.cs:26>)；[SolemnWarningPower.cs:9](<../ThermalVortexCode/Powers/SolemnWarningPower.cs:9>)；[InfiniteImpermanenceMonsterMovePatch.cs:54](<../ThermalVortexCode/Patches/InfiniteImpermanenceMonsterMovePatch.cs:54>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 备用装甲 · `SpareArmor`

内部名称：`THERMALVORTEX-SPARE_ARMOR`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 获得6点格挡，使1只场上怪兽的当前及最大生命提高3点。  

**升级卡面文本：**

> 获得9点格挡，使1只场上怪兽的当前及最大生命提高5点。  

卡文来源：[当前中文资源:225](<../ThermalVortex/localization/zhs/cards.json:225>)，键 `THERMALVORTEX-SPARE_ARMOR.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 获得6格挡，然后选1只场上有有效生命记录的怪兽，使其当前与最大生命各增加3。

**升级实际效果：** 获得9格挡，然后选1只场上有有效生命记录的怪兽，使其当前与最大生命各增加5。

**必要条件与实现补充：** 没有可选怪兽时仍先获得格挡；增生命不是只治疗到原上限。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [NewMainDeckCards.cs:92](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:92>)；[MonsterFieldHealthService.cs:220](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:220>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 稳压磁圈 · `StabilizedMagneticCoil`

内部名称：`THERMALVORTEX-STABILIZED_MAGNETIC_COIL`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 2 |
| 怪兽生命 | 2 | 2 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：2。  
> 场上每有1只其他怪兽，获得5点格挡。  

**升级卡面文本：**

> 怪兽。生命：2。  
> 场上每有1只其他怪兽，获得7点格挡。  

卡文来源：[当前中文资源:221](<../ThermalVortex/localization/zhs/cards.json:221>)，键 `THERMALVORTEX-STABILIZED_MAGNETIC_COIL.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 按结算时已有场上或前序待入场怪兽数量，每只获得5点无强化格挡，随后召唤自身。

**升级实际效果：** 按结算时已有场上或前序待入场怪兽数量，每只获得7点无强化格挡，随后召唤自身。

**必要条件与实现补充：** 嵌套连锁中，前序怪兽完成自身召唤步骤后虽要等最外层CardPlay结束才实际落场，仍与已经落场的怪兽按实体引用去重后计入本牌的已有怪兽数；相同卡牌ID的不同实体分别计数，不把尚未完成召唤步骤的自身计入数量。复制本效果时以实际效果宿主作为“自身”排除。格挡保持无强化，不受通常格挡数值修正，但玩家已有NoBlockPower或励辉士本回合专属格挡锁时不会获得该格挡。本体为磁圈类，但未调用联动召唤方法，不主动拉出电圈。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [NewMainDeckCards.cs:43](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:43>)；[PoleMonsterCard.cs:95](<../ThermalVortexCode/Cards/PoleMonsterCard.cs:95>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 星球改造 · `Terraforming`

内部名称：`THERMALVORTEX-TERRAFORMING`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 将抽牌堆中的1张能力牌加入手牌。  

**升级卡面文本：**

> 将抽牌堆中的1张能力牌加入手牌。  

卡文来源：[当前中文资源:116](<../ThermalVortex/localization/zhs/cards.json:116>)，键 `THERMALVORTEX-TERRAFORMING.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 从自身抽牌堆选择1张能力牌；选择结束后仍属于自身且在抽牌堆、仍为能力牌时，将该原牌加入手牌。

**升级实际效果：** 从自身抽牌堆选择1张能力牌；选择结束后仍属于自身且在抽牌堆、仍为能力牌时，将该原牌加入手牌。

**必要条件与实现补充：** 自身抽牌堆必须有 CardType.Power；能力类型怪兽也符合，不限场地名或特定系列。选择结束后重新确认目标归属、抽牌堆位置与能力类型；目标失效或原战斗失效则不移动。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [Terraforming.cs:17](<../ThermalVortexCode/Cards/Terraforming.cs:17>)；[Terraforming.cs:25](<../ThermalVortexCode/Cards/Terraforming.cs:25>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 激流葬 · `TorrentialTribute`

内部名称：`THERMALVORTEX-TORRENTIAL_TRIBUTE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 对所有敌人和场上怪兽造成12点伤害。  
> 消耗。  

**升级卡面文本：**

> 对所有敌人和场上怪兽造成16点伤害。  
> 消耗。  

卡文来源：[当前中文资源:59](<../ThermalVortex/localization/zhs/cards.json:59>)，键 `THERMALVORTEX-TORRENTIAL_TRIBUTE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 对所有敌人造成12点伤害，然后对己方所有场上怪兽各造成12点生命伤害。消耗。

**升级实际效果：** 对所有敌人造成16点伤害，然后对己方所有场上怪兽各造成16点生命伤害。消耗。

**必要条件与实现补充：** 己方怪兽伤害走直接怪兽伤害接口，不由玩家格挡抵消；致死以战斗破坏原因离场，可触发玩具盒等。黑暗决斗的0生命保护仍由怪兽生命系统处理；生死与共的攻击传递保1规则不适用。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [TorrentialTribute.cs:11](<../ThermalVortexCode/Cards/TorrentialTribute.cs:11>)；[MonsterFieldHealthService.cs:396](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:396>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 玩具盒 · `ToyBox`

内部名称：`THERMALVORTEX-TOY_BOX`。所属牌池：主牌奖励可选候选；类型：能力；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 2 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 你的怪兽被战斗破坏时，召唤1只玩具怪兽。每回合限1次。  

**升级卡面文本：**

> 你的怪兽被战斗破坏时，召唤1只玩具怪兽+。每回合限1次。  

卡文来源：[当前中文资源:154](<../ThermalVortex/localization/zhs/cards.json:154>)，键 `THERMALVORTEX-TOY_BOX.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 获得1个玩具盒来源；己方怪兽战斗破坏时，本来源每回合可1次生成并立即特殊打出1只未升级玩具怪兽。

**升级实际效果：** 获得1个玩具盒来源；己方怪兽战斗破坏时，本来源每回合可1次生成并立即特殊打出1只升级玩具怪兽。

**必要条件与实现补充：** 每次施放登记独立次数及对应玩具升级，获得时立即可触发，自己回合开始全部恢复。多来源按施放顺序对连续破坏事件逐次消耗；一次离场事件只使用1个可用来源。成功打出才耗次数，失败生成体清除；为召唤暂增1位。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [ToyBox.cs:11](<../ThermalVortexCode/Cards/ToyBox.cs:11>)；[ToyBoxPower.cs:12](<../ThermalVortexCode/Powers/ToyBoxPower.cs:12>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 落穴 · `TrapHole`

内部名称：`THERMALVORTEX-TRAP_HOLE`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 无 |

**基础卡面文本：**

> 若目标意图为攻击，给予其2层虚弱。  
> 消耗。  

**升级卡面文本：**

> 若目标意图为攻击，给予其2层虚弱。  

卡文来源：[当前中文资源:63](<../ThermalVortex/localization/zhs/cards.json:63>)，键 `THERMALVORTEX-TRAP_HOLE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 若指定敌人的下一行动含攻击意图，给予其2层虚弱。消耗。

**升级实际效果：** 若指定敌人的下一行动含攻击意图，给予其2层虚弱。

**必要条件与实现补充：** 可选择任何合法敌人，不因没有攻击意图而禁止打出；条件不满足则不施加。条件按 EnemyActionClassifier 对 NextMove 的 Attack／DeathBlow 分类。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [TrapHole.cs:13](<../ThermalVortexCode/Cards/TrapHole.cs:13>)；[EnemyActionClassifier.cs:9](<../ThermalVortexCode/Patches/EnemyActionClassifier.cs:9>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)

### 成金哥布林 · `UpstartGoblin`

内部名称：`THERMALVORTEX-UPSTART_GOBLIN`。所属牌池：主牌奖励可选候选；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 0 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 抽2张牌，所有敌人回复20点生命。  

**升级卡面文本：**

> 抽3张牌，所有敌人回复30点生命。  

卡文来源：[当前中文资源:147](<../ThermalVortex/localization/zhs/cards.json:147>)，键 `THERMALVORTEX-UPSTART_GOBLIN.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 抽2张牌，再给每名可命中敌人治疗20生命。

**升级实际效果：** 抽3张牌，再给每名可命中敌人治疗30生命。

**必要条件与实现补充：** 治疗对象为结算时 HittableEnemies，按游戏治疗上限；先抽牌后治疗。 登记在角色主牌池，属于可选奖励候选；本局实际奖励仍受本局奖励池构筑筛选。

**实现位置：** [UpstartGoblin.cs:10](<../ThermalVortexCode/Cards/UpstartGoblin.cs:10>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)


## 特殊主牌（普通奖励排除）（2 张）

### 电磁圈 · `ElectromagneticCircle`

内部名称：`THERMALVORTEX-ELECTROMAGNETIC_CIRCLE`。所属牌池：先古合成专属（欧洛巴斯的古老牙齿；普通奖励排除）；类型：攻击；稀有度：先古。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 1级2；2级1 |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：5。  
> 造成6点伤害，获得6点格挡。  
> 可从手牌或抽牌堆召唤1只怪兽。  

**升级卡面文本：**

> 1级：  
> 怪兽。生命：5。  
> 造成6点伤害，获得6点格挡。  
> 可从手牌或抽牌堆召唤1只怪兽。  
>   
> 2级：  
> 怪兽。生命：5。  
> 造成6点伤害，获得6点格挡。  
> 可从手牌或抽牌堆召唤1只怪兽。  

卡文来源：[当前中文资源:15](<../ThermalVortex/localization/zhs/cards.json:15>)，键 `THERMALVORTEX-ELECTROMAGNETIC_CIRCLE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 造成6点攻击伤害，获得6点格挡，召唤5生命电磁圈；可从手牌或抽牌堆选择1只可立即打出的非额外怪兽免费召唤。

**升级实际效果：** 1级费用2，2级费用1；两级均造成6点攻击伤害、获得6点格挡、召唤5生命电磁圈，并可从手牌或抽牌堆免费召唤1只合法非额外怪兽。

**必要条件与实现补充：** 最多升级2次；同时具有电圈和磁圈身份。武藤游戏通过欧洛巴斯的原生古老牙齿选项合成：永久牌组没有电磁圈，且具有可移除的α号电圈与α号磁圈各至少1张时，各选升级最高的1张，平级按永久牌组顺序，移除后加入1张电磁圈。每张已升级素材贡献1级，合成基础等级为0／1／2级，对应基础费用3／2／1；原生入牌效果继续正常结算，可能继续改变结果。选项预览两类素材及按素材贡献等级生成的电磁圈，并沿用原生先古选项结束流程。取得时再次复查条件，失效不移除素材、不生成结果。建筑师结尾不提供合成入口。连锁可跳过，排除本牌和额外怪兽，仍受怪兽位限制。

**实现位置：** [ElectromagneticCircle.cs:13](<../ThermalVortexCode/Cards/ElectromagneticCircle.cs:13>)；[ElectromagneticCircle.cs:15](<../ThermalVortexCode/Cards/ElectromagneticCircle.cs:15>)；[ElectromagneticCircle.cs:45](<../ThermalVortexCode/Cards/ElectromagneticCircle.cs:45>)；[ArchaicToothCompatibilityPatch.cs:84](<../ThermalVortexCode/Patches/ArchaicToothCompatibilityPatch.cs:84>)；[ArchaicToothCompatibilityPatch.cs:120](<../ThermalVortexCode/Patches/ArchaicToothCompatibilityPatch.cs:120>)；[ArchaicToothCompatibilityPatch.cs:161](<../ThermalVortexCode/Patches/ArchaicToothCompatibilityPatch.cs:161>)；[ThermalVortexGeneratedCards.cs:13](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:13>)

### 原初之神 法拉 · `PrimalGodFara`

内部名称：`THERMALVORTEX-PRIMAL_GOD_FARA`。所属牌池：先古专属（DustyTome）；普通奖励候选排除；类型：技能；稀有度：先古。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 3 | 3 |
| 怪兽生命 | 1 | 1 |
| 固有关键词 | 无 | 固有 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 不会成为敌人攻击目标。  
> 本卡作为素材使用时，将立刻复活。  

**升级卡面文本：**

> 固有。  
> 怪兽。生命：1。  
> 不会成为敌人攻击目标。  
> 本卡作为素材使用时，将立刻复活。  

卡文来源：[当前中文资源:17](<../ThermalVortex/localization/zhs/cards.json:17>)，键 `THERMALVORTEX-PRIMAL_GOD_FARA.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤自身；不能成为敌方攻击的承伤目标；每次作为素材离场后尝试特殊召唤回场。

**升级实际效果：** 固有。召唤自身；不能成为敌方攻击的承伤目标；每次作为素材离场后尝试特殊召唤回场。

**必要条件与实现补充：** 来源为先古遗物 DustyTome在武藤游戏角色下的专属卡；不能出现在普通奖励或通用生成。素材回场仍需合法怪兽位；战斗破坏、普通弃置等非素材离场不触发。无每回合次数上限。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记继承角色主牌池，但 IsRewardExcluded 明确排除普通奖励。

**实现位置：** [PrimalGodFara.cs:9](<../ThermalVortexCode/Cards/PrimalGodFara.cs:9>)；[ThermalVortexAncientOptionsPatch.cs:188](<../ThermalVortexCode/Patches/ThermalVortexAncientOptionsPatch.cs:188>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)；[MonsterFieldService.cs:141](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:141>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[ThermalVortexGeneratedCards.cs:13](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:13>)


## 额外牌组（17 张）

### 独步千年的大义贼 · `MillenniumGrandThief`

内部名称：`THERMALVORTEX-MILLENNIUM_GRAND_THIEF`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为2素材） | 0能量（卡面为2素材） |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只千年怪兽。  
> 召唤时，可支付10、100、1000……金币，自选对应的1、2、3……张被封印千年部件加入手牌。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只千年怪兽。  
> 召唤时，可支付7、70、700……金币，自选对应的1、2、3……张被封印千年部件加入手牌。  
> 消耗。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-MILLENNIUM_GRAND_THIEF.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 用恰好2只千年怪兽融合召唤。入场时可选择一个金币总价档位：支付10、100、1000……金币，分别逐张自选1、2、3……张被封印千年部件加入手牌。

**升级实际效果：** 用恰好2只千年怪兽融合召唤。入场时可选择一个金币总价档位：支付7、70、700……金币，分别逐张自选1、2、3……张被封印千年部件加入手牌。

**必要条件与实现补充：** 每次入场只选一次档位，可不支付；n张的总价为10^n，升级后为0.7×10^n，并非逐张累加。购买n张部件时，由玩家从左手、右手、左腿、右腿、躯干5种中依次选择n次，允许重复选择同一种；每次生成所选基础部件加入手牌。本体属于千年卡及千年怪兽。基础与升级均为5生命并带消耗；正常召唤后留场，通常送墓型离场进入消耗堆。素材来源仍服从所用融合方式。使用默认卡图。 唯一手牌持有：本体通过正常融合、直接特殊召唤或再次入场成功召唤后，触发本次已快照的封印部件手持效果：左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。

**实现位置：** [MillenniumResolution.cs](../ThermalVortexCode/Cards/MillenniumResolution.cs)；[MillenniumExtraDeckCards.cs](../ThermalVortexCode/Cards/MillenniumExtraDeckCards.cs)；[ThermalVortexCore.cs](../ThermalVortexCode/Relics/ThermalVortexCore.cs)。

### 千年召唤神·艾库佐尼亚 · `ExodiaSummoner`

内部名称：`THERMALVORTEX-EXODIA_SUMMONER`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为2素材） | 0能量（卡面为2素材） |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只千年怪兽。  
> 在场时，每回合开始将随机1张可作为主牌奖励的千年怪兽加入抽牌堆。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只千年怪兽。  
> 在场时，每回合开始将随机1张可作为主牌奖励的千年怪兽加入抽牌堆。  
> 加入的牌本场战斗费用减少1。  
> 消耗。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-EXODIA_SUMMONER.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 用恰好2只千年怪兽融合召唤。在场期间，每次己方回合开始时，随机生成1张千年奖励怪兽，加入本场战斗抽牌堆的随机位置。

**升级实际效果：** 用恰好2只千年怪兽融合召唤。在场期间，每次己方回合开始时，随机生成1张千年奖励怪兽，加入本场战斗抽牌堆的随机位置；生成牌本场战斗费用减少1，最低为0。

**必要条件与实现补充：** 从武藤游戏完整主牌奖励目录中取中文标准名含“千年”的怪兽，排除被封印部件、其他衍生物和额外怪兽；不受本局自定义奖励白名单限制。每只在场召唤者分别生成1张，并按各自升级状态决定是否减费；生成牌本身不升级。本体属于千年卡及千年怪兽，可作为要求千年怪兽的融合素材。千年召唤者的单一持续能力计入1种千年能力，同名多只召唤者不增加能力种数；本体仍为额外怪兽，不进入千年奖励怪兽生成池。回合开始触发点为AfterPlayerTurnStartEarly，位于正常抽牌之前。基础与升级均为5生命并带消耗；正常召唤后留场，通常送墓型离场进入消耗堆。素材来源仍服从所用融合方式。使用默认卡图。 唯一手牌持有：本体通过正常融合、直接特殊召唤或再次入场成功召唤后，触发本次已快照的封印部件手持效果：左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。千年召唤者、千年邪与千年守护者均计入千年卡及千年怪兽，也可作为要求千年怪兽的融合素材；它们仍是额外怪兽，不进入千年奖励怪兽生成池。

**实现位置：** [MillenniumResolution.cs](../ThermalVortexCode/Cards/MillenniumResolution.cs)；[MillenniumExtraDeckCards.cs](../ThermalVortexCode/Cards/MillenniumExtraDeckCards.cs)；[MillenniumExtraDeckPowers.cs](../ThermalVortexCode/Powers/MillenniumExtraDeckPowers.cs)；[ThermalVortexCore.cs](../ThermalVortexCode/Relics/ThermalVortexCore.cs)。

### 千年宝库的密钥 · `MillenniumMasterKey`

内部名称：`THERMALVORTEX-MILLENNIUM_MASTER_KEY`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为2素材） | 0能量（卡面为2素材） |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只千年怪兽。  
> 召唤时，将每种可作为主牌奖励的千年怪兽各1张随机加入抽牌堆。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只千年怪兽。  
> 召唤时，将每种可作为主牌奖励的千年怪兽的升级版各1张随机加入抽牌堆。  
> 消耗。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-MILLENNIUM_MASTER_KEY.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 用恰好2只千年怪兽融合召唤。入场时将所有千年奖励怪兽每种各1张加入本场战斗抽牌堆的随机位置。

**升级实际效果：** 用恰好2只千年怪兽融合召唤。入场时将所有千年奖励怪兽每种各1张升级后加入本场战斗抽牌堆的随机位置。

**必要条件与实现补充：** 从武藤游戏完整主牌奖励目录中取中文标准名含“千年”的怪兽，排除被封印部件、其他衍生物和额外怪兽；不受本局自定义奖励白名单限制。每种按卡牌类型只生成1张；只加入本场战斗抽牌堆，不加入永久主牌组。本体属于千年卡及千年怪兽。基础与升级均为5生命并带消耗；正常召唤后留场，通常送墓型离场进入消耗堆。素材来源仍服从所用融合方式。使用默认卡图。 唯一手牌持有：本体通过正常融合、直接特殊召唤或再次入场成功召唤后，触发本次已快照的封印部件手持效果：左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。

**实现位置：** [MillenniumResolution.cs](../ThermalVortexCode/Cards/MillenniumResolution.cs)；[MillenniumExtraDeckCards.cs](../ThermalVortexCode/Cards/MillenniumExtraDeckCards.cs)；[ThermalVortexCore.cs](../ThermalVortexCode/Relics/ThermalVortexCore.cs)。

### 千年邪神·艾库佐尼亚 · `EvilExodia`

内部名称：`THERMALVORTEX-EVIL_EXODIA`。所属牌池：额外牌组奖励可选候选；类型：攻击；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为X素材） | 0能量（卡面为X素材） |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只及以上怪兽。  
> 召唤时，造成千年计数×融合素材数点伤害。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只及以上怪兽。  
> 召唤时，造成千年计数×融合素材数点伤害。  
> 被消耗时，返回额外牌组。  
> 消耗。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-EVIL_EXODIA.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 用至少2只怪兽融合召唤。对选择的敌人造成使用前锁定的千年计数×本次实际融合素材数的无强化伤害。

**升级实际效果：** 用至少2只怪兽融合召唤。对选择的敌人造成使用前锁定的千年计数×本次实际融合素材数的无强化伤害；此牌被消耗时返回本场额外牌组。

**显示补充：** 战斗中查看卡牌时，黄色“千年计数”解释末尾实时显示支付素材前当前拥有的千年计数，卡面不另列当前值；无战斗状态时不显示占位0。本次结算沿用使用前锁定值，与确认素材前的展示口径一致。

**必要条件与实现补充：** 素材不要求千年系列，没有固定素材上限。数值使用本次真实融合材料数，基础与升级公式一致。返回只在升级牌实际消耗时触发，返回条目保持升级状态，不新增永久拥有条目。本体属于千年卡及千年怪兽，可作为要求千年怪兽的融合素材；仍为额外怪兽，不进入千年奖励怪兽生成池。本次千年计数在使用前锁定：确认融合素材后、支付素材前取值，成功入场时沿用；素材离手或离场、后续抽弃牌与新增能力均不改变本次锁值，新召唤的本体不计入。直接特殊召唤或再次入场按入场前锁定值结算。基础与升级均为5生命并带消耗；正常召唤后留场，通常送墓型离场进入消耗堆。素材来源仍服从所用融合方式。使用默认卡图。 唯一手牌持有：本体通过正常融合、直接特殊召唤或再次入场成功召唤后，触发本次已快照的封印部件手持效果：左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。千年召唤者、千年邪与千年守护者均计入千年卡及千年怪兽，也可作为要求千年怪兽的融合素材；它们仍是额外怪兽，不进入千年奖励怪兽生成池。

**实现位置：** [MillenniumResolution.cs](../ThermalVortexCode/Cards/MillenniumResolution.cs)；[MillenniumExtraDeckCards.cs](../ThermalVortexCode/Cards/MillenniumExtraDeckCards.cs)；[ThermalVortexCore.cs](../ThermalVortexCode/Relics/ThermalVortexCore.cs)。

### 千年守护神·艾库佐尼亚 · `ExodiaGuardian`

内部名称：`THERMALVORTEX-EXODIA_GUARDIAN`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为X素材） | 0能量（卡面为X素材） |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只及以上怪兽。  
> 召唤时，获得千年计数×融合素材数点格挡。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只及以上怪兽。  
> 召唤时，获得千年计数×融合素材数点格挡。  
> 被消耗时，返回额外牌组。  
> 消耗。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-EXODIA_GUARDIAN.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 用至少2只怪兽融合召唤。给予玩家使用前锁定的千年计数×本次实际融合素材数的无强化格挡。

**升级实际效果：** 用至少2只怪兽融合召唤。给予玩家使用前锁定的千年计数×本次实际融合素材数的无强化格挡；此牌被消耗时返回本场额外牌组。

**显示补充：** 战斗中查看卡牌时，黄色“千年计数”解释末尾实时显示支付素材前当前拥有的千年计数，卡面不另列当前值；无战斗状态时不显示占位0。本次结算沿用使用前锁定值，与确认素材前的展示口径一致。

**必要条件与实现补充：** 素材不要求千年系列，没有固定素材上限。格挡给予玩家，基础与升级公式一致；格挡保持无强化，但玩家已有NoBlockPower时不会获得。返回只在升级牌实际消耗时触发，返回条目保持升级状态，不新增永久拥有条目。本体属于千年卡及千年怪兽，可作为要求千年怪兽的融合素材；仍为额外怪兽，不进入千年奖励怪兽生成池。本次千年计数在使用前锁定：确认融合素材后、支付素材前取值，成功入场时沿用；素材离手或离场、后续抽弃牌与新增能力均不改变本次锁值，新召唤的本体不计入。直接特殊召唤或再次入场按入场前锁定值结算。基础与升级均为5生命并带消耗；正常召唤后留场，通常送墓型离场进入消耗堆。素材来源仍服从所用融合方式。使用默认卡图。 唯一手牌持有：本体通过正常融合、直接特殊召唤或再次入场成功召唤后，触发本次已快照的封印部件手持效果：左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。千年召唤者、千年邪与千年守护者均计入千年卡及千年怪兽，也可作为要求千年怪兽的融合素材；它们仍是额外怪兽，不进入千年奖励怪兽生成池。

**实现位置：** [MillenniumResolution.cs](../ThermalVortexCode/Cards/MillenniumResolution.cs)；[MillenniumExtraDeckCards.cs](../ThermalVortexCode/Cards/MillenniumExtraDeckCards.cs)；[ThermalVortexCore.cs](../ThermalVortexCode/Relics/ThermalVortexCore.cs)。

### 励辉重启骑士 · `BrilliantRebootKnight`

内部名称：`THERMALVORTEX-BRILLIANT_REBOOT_KNIGHT`。所属牌池：额外牌组奖励可选候选；类型：攻击；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为2素材） | 0能量（卡面为2素材） |
| 怪兽生命 | 1 | 1 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：1。  
> 融合素材：2只怪兽。  
> 敌人当前生命总和高于你时可用。  
> 移除所有敌人的格挡，对其造成20点伤害。  
> 失去全部格挡，本回合无法获得格挡。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：1。  
> 融合素材：2只怪兽。  
> 敌人当前生命总和高于你时可用。  
> 移除所有敌人的格挡，对其造成28点伤害。  
> 失去全部格挡，本回合无法获得格挡。  
> 消耗。  

卡文来源：[当前中文资源:106](<../ThermalVortex/localization/zhs/cards.json:106>)，键 `THERMALVORTEX-BRILLIANT_REBOOT_KNIGHT.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 以2只怪兽融合召唤1生命怪兽。敌人当前生命总和必须大于你的当前生命。清空所有存活敌人的格挡，随后对所有敌人发动1次20点攻击。最后清空你的格挡，给予你1层无法获得格挡。

**升级实际效果：** 以2只怪兽融合召唤1生命怪兽。敌人当前生命总和必须大于你的当前生命。清空所有存活敌人的格挡，随后对所有敌人发动1次28点攻击。最后清空你的格挡，给予你1层无法获得格挡。

**必要条件与实现补充：** 融合选择及打出时均检查生命比较。先逐一清空存活敌人格挡，再调用一次显式CardAttackAllEnemies群攻入口，基础攻击段数为1，不随敌人数增加。仍接受原生伤害和攻击次数修正，无存活目标时可提前结束。原版NoBlockPower只阻止来自卡牌的普通格挡；本牌完成原版能力施加步骤后必定另登记当前战斗与玩家回合的专属锁，不依赖PowerCmd返回的新建或叠加实例。该锁覆盖BlockVar、decimal两种GainBlock入口及Creature.GainBlockInternal最终写入点，在切换至敌方回合或下个玩家回合时自动失效。因此Unpowered、无CardPlay来源、原版或其他Mod发放乃至绕过公开命令直接写入的格挡在本回合内也不能生效，且不改变其他来源施加的NoBlockPower语义。

**实现位置：** [BrilliantRebootKnight.cs:13](<../ThermalVortexCode/Cards/BrilliantRebootKnight.cs:13>)；[BrilliantRebootKnight.cs:26](<../ThermalVortexCode/Cards/BrilliantRebootKnight.cs:26>)；[BrilliantRebootKnight.cs:59](<../ThermalVortexCode/Cards/BrilliantRebootKnight.cs:59>)；[ThermalVortexCore.cs:2304](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2304>)；[ExtraDeckCard.cs:22](<../ThermalVortexCode/Cards/ExtraDeckCard.cs:22>)；[ThermalVortexCombatVfx.cs:24](<../ThermalVortexCode/Vfx/ThermalVortexCombatVfx.cs:24>)；`.tools/nuget-packages/alchyr.sts2.baselib/3.1.2/lib/net9.0/BaseLib.dll`

### 电子嵌合龙 · `ChimeratechOverdragon`

内部名称：`THERMALVORTEX-CHIMERATECH_OVERDRAGON`。所属牌池：额外牌组奖励可选候选；类型：攻击；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为X素材） | 0能量（卡面为X素材） |
| 怪兽生命 | 4×max(2,已用素材数) | 4×max(2,已用素材数) |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：融合素材数×4。  
> 融合素材：至少2只怪兽，含场上的电子龙。  
> 造成（7×融合素材数＋继承攻击）点伤害。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：融合素材数×4。  
> 融合素材：至少2只怪兽，含场上的电子龙。  
> 造成（11×融合素材数＋继承攻击）点伤害。  
> 消耗。  

卡文来源：[当前中文资源:110](<../ThermalVortex/localization/zhs/cards.json:110>)，键 `THERMALVORTEX-CHIMERATECH_OVERDRAGON.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 以至少2只怪兽融合召唤；必须包含1只场上的电子龙。生命为素材数×4，对选择的敌人造成素材数×7加继承攻击力的1段攻击伤害。

**升级实际效果：** 以至少2只怪兽融合召唤；必须包含1只场上的电子龙。生命为素材数×4，对选择的敌人造成素材数×11加继承攻击力的1段攻击伤害。

**必要条件与实现补充：** 素材没有固定上限；须满足所用融合方式的来源限制。混沌的有效电子龙身份可以满足指定素材。继承攻击力只加一次。

**实现位置：** [ChimeratechOverdragon.cs:9](<../ThermalVortexCode/Cards/ChimeratechOverdragon.cs:9>)；[ChimeratechOverdragon.cs:11](<../ThermalVortexCode/Cards/ChimeratechOverdragon.cs:11>)；[ThermalVortexCore.cs:3032](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3032>)；[ThermalVortexCore.cs:3242](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3242>)；[CyberSeries.cs:1](<../ThermalVortexCode/Cards/CyberSeries.cs:1>)

### 断路护符兽 · `CircuitTalismanBeast`

内部名称：`THERMALVORTEX-CIRCUIT_TALISMAN_BEAST`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为2素材） | 0能量（卡面为2素材） |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只怪兽。  
> 给予所有敌人1层虚弱。  
> 被消耗时，获得8点格挡。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：5。  
> 融合素材：2只怪兽。  
> 给予所有敌人2层虚弱。  
> 被消耗时，获得12点格挡。  
> 消耗。  

卡文来源：[当前中文资源:100](<../ThermalVortex/localization/zhs/cards.json:100>)，键 `THERMALVORTEX-CIRCUIT_TALISMAN_BEAST.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 以2只怪兽融合召唤5生命怪兽，给予所有可命中的敌人1层虚弱。此牌被消耗时获得8点格挡。

**升级实际效果：** 以2只怪兽融合召唤5生命怪兽，给予所有可命中的敌人2层虚弱。此牌被消耗时获得12点格挡。

**必要条件与实现补充：** 消耗格挡由本角色核心遗物监听，不要求此前已入场；纯消耗亦可触发。格挡保持无强化，但玩家已有NoBlockPower时不会获得。若属于失败召唤的追踪回收，回收分支先处理，不把回收当消耗奖励。

**实现位置：** [CircuitTalismanBeast.cs:11](<../ThermalVortexCode/Cards/CircuitTalismanBeast.cs:11>)；[CircuitTalismanBeast.cs:17](<../ThermalVortexCode/Cards/CircuitTalismanBeast.cs:17>)；[ThermalVortexCore.cs:433](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:433>)；[ThermalVortexCore.cs:3281](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3281>)

### 电子龙无限 · `CyberDragonInfinity`

内部名称：`THERMALVORTEX-CYBER_DRAGON_INFINITY`。所属牌池：额外牌组奖励可选候选；类型：攻击；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为3素材） | 0能量（卡面为3素材） |
| 怪兽生命 | 1 | 1 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：1。  
> 融合素材：3只怪兽，含场上的电子龙。  
> 在场时，每回合开始时根据自己吞噬的效果总和，施展吞噬得到的能力。回合结束随机吞噬1只其他场上怪兽。若无其他怪兽，则消耗自身。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：1。  
> 融合素材：3只怪兽，含场上的电子龙。  
> 理智残存。  
> 在场时，每回合开始时根据自己吞噬的效果总和，施展吞噬得到的能力。回合结束随机吞噬1只其他场上怪兽。  
> 消耗。  

卡文来源：[当前中文资源:189](<../ThermalVortex/localization/zhs/cards.json:189>)，键 `THERMALVORTEX-CYBER_DRAGON_INFINITY.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 以3只怪兽融合召唤1生命电子怪兽，须含1只场上电子龙。后续玩家回合开始，对随机敌人造成等于继承攻击力的效果伤害。本方回合末随机吞噬1只其他场上怪兽；没有其他目标则消耗自身。

**升级实际效果：** 以3只怪兽融合召唤1生命电子怪兽，须含1只场上电子龙。后续玩家回合开始，对随机敌人造成等于继承攻击力的效果伤害。本方回合末随机吞噬1只其他场上怪兽；没有其他目标则停止吞噬，不消耗自身（理智残存）。

**必要条件与实现补充：** 吞噬先吸收目标当前生命、最大生命、攻击贡献与可复制效果，再消耗目标。新获得持续能力立即同步，但不当场重放入场/回合开始奖励；每个实体本方回合末仅选择一个目标。其原生攻击贡献为0。吞噬成长按实体独立保存，持续到本场战斗结束。普通离场、消耗、回收与复活不删除成长；返回虚拟额外牌组、未来融合预约、失败返还与再次实体化均携带完整战斗快照，恢复快照不重放吞噬效果。同名实体不合并，复制与重放的新实体各持有独立成长副本。真正变形把成长迁移给新形态，旧卡回收后不再拥有已迁移的成长；新形态实际入场即提交，即使立即离场也算成功，失败只回退本次迁移并保留回调新增成长。成长不写入永久额外牌组，战斗结束、下一战斗或运行重建清理。当前生命仍正常承受伤害，各继承能力仍遵守自己的触发条件。

**实现位置：** [CyberDragonInfinity.cs:13](<../ThermalVortexCode/Cards/CyberDragonInfinity.cs:13>)；[CyberDragonInfinity.cs:15](<../ThermalVortexCode/Cards/CyberDragonInfinity.cs:15>)；[CyberDragonInfinityPower.cs:21](<../ThermalVortexCode/Powers/CyberDragonInfinityPower.cs:21>)；[CyberDragonInfinityPower.cs:57](<../ThermalVortexCode/Powers/CyberDragonInfinityPower.cs:57>)；[ThermalVortexCore.cs:3245](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3245>)；[CyberSeries.cs:149](<../ThermalVortexCode/Cards/CyberSeries.cs:149>)；[CyberSeries.cs:307](<../ThermalVortexCode/Cards/CyberSeries.cs:307>)；[CyberSeries.cs:330](<../ThermalVortexCode/Cards/CyberSeries.cs:330>)；[CyberSeries.cs:388](<../ThermalVortexCode/Cards/CyberSeries.cs:388>)

### 电子终结龙 · `CyberEndDragon`

内部名称：`THERMALVORTEX-CYBER_END_DRAGON`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为X素材） | 0能量（卡面为X素材） |
| 怪兽生命 | 8×max(2,已用素材数) | 8×max(2,已用素材数) |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：融合素材数×8。  
> 融合素材：至少2只电子怪兽。  
> 所有敌人失去（融合素材数×15）点生命，无视格挡。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：融合素材数×8。  
> 融合素材：至少2只电子怪兽。  
> 所有敌人失去（融合素材数×20）点生命，无视格挡。  
> 消耗。  

卡文来源：[当前中文资源:139](<../ThermalVortex/localization/zhs/cards.json:139>)，键 `THERMALVORTEX-CYBER_END_DRAGON.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 以至少2只电子怪兽融合召唤；生命为素材数×8。所有可命中存活敌人分别直接失去素材数×15生命。

**升级实际效果：** 以至少2只电子怪兽融合召唤；生命为素材数×8。所有可命中存活敌人分别直接失去素材数×20生命。

**必要条件与实现补充：** 所有所选素材均须通过 CyberSeries.IsCyberMonster 的电子怪兽判定，来源仍依融合方式；素材提示绑定电子怪兽系列说明。直接扣生命绕过格挡，接入力量焊接的完整输出强化/后续减伤并向下取整。本卡原生扣血不额外加吞噬继承攻击；被吞噬时攻击贡献按素材数×15/20计算。

**实现位置：** [CyberEndDragon.cs:11](<../ThermalVortexCode/Cards/CyberEndDragon.cs:11>)；[CyberEndDragon.cs:13](<../ThermalVortexCode/Cards/CyberEndDragon.cs:13>)；[CyberEndDragon.cs:47](<../ThermalVortexCode/Cards/CyberEndDragon.cs:47>)；[ThermalVortexCore.cs:3007](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3007>)；[CyberWeldingPower.cs:98](<../ThermalVortexCode/Powers/CyberWeldingPower.cs:98>)

### 导爆 · `Detonation`

内部名称：`THERMALVORTEX-DETONATION`。所属牌池：固定起始额外牌；类型：攻击；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为2素材） | 0能量（卡面为2素材） |
| 怪兽生命 | 8 | 8 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：8。  
> 融合素材：2只怪兽。  
> 造成12点伤害。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：8。  
> 融合素材：2只怪兽。  
> 造成16点伤害。  
> 消耗。  

卡文来源：[当前中文资源:98](<../ThermalVortex/localization/zhs/cards.json:98>)，键 `THERMALVORTEX-DETONATION.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 以2只怪兽融合召唤8生命导爆，对选择的敌人造成12点攻击伤害。

**升级实际效果：** 以2只怪兽融合召唤8生命导爆，对选择的敌人造成16点攻击伤害。

**必要条件与实现补充：** 固定初始额外牌，不进入后续额外奖励候选。目标通过同步目标选择流程确定；额外召唤不额外消耗能量。打出融合后即把素材、腾位后的怪兽位、打出限制与有效目标合入候选预判；若无可召唤目标，提示“无法融合召唤。”并结束，不打开素材选择、不支付素材、不提取导爆。满场仍允许用该融合方式认可的场上素材腾位。召唤动画前及执行召唤前复查怪兽位、打出条件与有效目标，已知无法召唤时直接停止；同步选择目标后暂存于结算区（Play），等待实际入场。素材回调已经结算的效果不回滚，内部额外条目仍可回收以防丢牌。

**实现位置：** [Detonation.cs:9](<../ThermalVortexCode/Cards/Detonation.cs:9>)；[Detonation.cs:11](<../ThermalVortexCode/Cards/Detonation.cs:11>)；[ThermalVortexCore.cs:63](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:63>)；[RewardPoolCatalog.cs:45](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:45>)；[ExtraDeckCard.cs:22](<../ThermalVortexCode/Cards/ExtraDeckCard.cs:22>)

### 天霆炉神 · `DivineArsenalFurnaceGod`

内部名称：`THERMALVORTEX-DIVINE_ARSENAL_FURNACE_GOD`。所属牌池：额外牌组奖励可选候选；类型：攻击；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为X素材） | 0能量（卡面为X素材） |
| 怪兽生命 | 6 | 6 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：6。  
> 融合素材：场上全部融合怪兽，至少1只。  
> 消耗所有手牌。  
> 对所有敌人造成（5×消耗手牌数×融合素材数）点伤害。  
> 结束回合。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：6。  
> 融合素材：场上全部融合怪兽，至少1只。  
> 消耗所有手牌。  
> 对所有敌人造成（7×消耗手牌数×融合素材数）点伤害。  
> 结束回合。  
> 消耗。  

卡文来源：[当前中文资源:108](<../ThermalVortex/localization/zhs/cards.json:108>)，键 `THERMALVORTEX-DIVINE_ARSENAL_FURNACE_GOD.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 用场上全部额外牌组怪兽作为素材融合召唤6生命天霆炉神。消耗除本牌外全部手牌，对所有敌人造成5×所用素材数×消耗前手牌数的攻击伤害，然后结束自己的回合。

**升级实际效果：** 用场上全部额外牌组怪兽作为素材融合召唤6生命天霆炉神。消耗除本牌外全部手牌，对所有敌人造成7×所用素材数×消耗前手牌数的攻击伤害，然后结束自己的回合。

**必要条件与实现补充：** 至少需要1只场上额外怪兽；固定选取全部符合的场上实体，包含混沌的有效额外怪兽身份。素材选择分支不使用卡类 MaximumMaterials=1 来限制实际全部数量。手牌数先快照，之后逐牌消耗；即使手牌为0也尝试结束本人的玩家回合。 调用一次显式CardAttackAllEnemies入口，对所有敌人造成基础1段公式伤害；仍接受原生攻击次数修正，无存活目标时可提前结束。

**实现位置：** [DivineArsenalFurnaceGod.cs:11](<../ThermalVortexCode/Cards/DivineArsenalFurnaceGod.cs:11>)；[DivineArsenalFurnaceGod.cs:13](<../ThermalVortexCode/Cards/DivineArsenalFurnaceGod.cs:13>)；[DivineArsenalFurnaceGod.cs:36](<../ThermalVortexCode/Cards/DivineArsenalFurnaceGod.cs:36>)；[ThermalVortexCore.cs:3024](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3024>)；[ThermalVortexCore.cs:3166](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3166>)；[ThermalVortexCore.cs:3227](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3227>)；[ThermalVortexCombatVfx.cs:31](<../ThermalVortexCode/Vfx/ThermalVortexCombatVfx.cs:31>)；`.tools/nuget-packages/alchyr.sts2.baselib/3.1.2/lib/net9.0/BaseLib.dll`

### 休眠磁场兽 · `DormantMagneticFieldBeast`

内部名称：`THERMALVORTEX-DORMANT_MAGNETIC_FIELD_BEAST`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为X素材） | 0能量（卡面为X素材） |
| 怪兽生命 | 6 | 6 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：6。  
> 融合素材：2至3只怪兽。  
> 获得（8＋4×X）点格挡和X层休眠磁场。X为融合素材数。  
> 给予所有敌人1层虚弱。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：6。  
> 融合素材：2至3只怪兽。  
> 获得（8＋4×X）点格挡和X层休眠磁场。X为融合素材数。  
> 给予所有敌人融合素材数层虚弱。  
> 消耗。  

卡文来源：[当前中文资源:104](<../ThermalVortex/localization/zhs/cards.json:104>)，键 `THERMALVORTEX-DORMANT_MAGNETIC_FIELD_BEAST.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 以2至3只怪兽融合召唤6生命怪兽。获得8+4×素材数点格挡，给予所有敌人1层虚弱，获得等于素材数层休眠磁场。

**升级实际效果：** 以2至3只怪兽融合召唤6生命怪兽。获得8+4×素材数点格挡，给予所有敌人等于素材数层虚弱，获得等于素材数层休眠磁场。

**必要条件与实现补充：** 休眠磁场存在时不能打出攻击牌；每次格挡应清除时改为保留一半并失去1层。格挡为非强化型直接获得，2/3素材对应16/20；玩家已有NoBlockPower时不会获得这次格挡，休眠磁场和虚弱仍正常结算。

**实现位置：** [DormantMagneticFieldBeast.cs:13](<../ThermalVortexCode/Cards/DormantMagneticFieldBeast.cs:13>)；[DormantMagneticFieldBeast.cs:15](<../ThermalVortexCode/Cards/DormantMagneticFieldBeast.cs:15>)；[DormantMagneticFieldBeast.cs:41](<../ThermalVortexCode/Cards/DormantMagneticFieldBeast.cs:41>)；[DormantMagneticFieldPower.cs:16](<../ThermalVortexCode/Powers/DormantMagneticFieldPower.cs:16>)

### 全装甲雷枪 · `FullArmorThunderLance`

内部名称：`THERMALVORTEX-FULL_ARMOR_THUNDER_LANCE`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：基础。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为3素材） | 0能量（卡面为3素材） |
| 怪兽生命 | 8 | 8 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：8。  
> 融合素材：3只怪兽。  
> 获得8点格挡。  
> 在场时，每回合开始获得6点格挡。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：8。  
> 融合素材：3只怪兽。  
> 获得12点格挡。  
> 在场时，每回合开始获得8点格挡。  
> 消耗。  

卡文来源：[当前中文资源:102](<../ThermalVortex/localization/zhs/cards.json:102>)，键 `THERMALVORTEX-FULL_ARMOR_THUNDER_LANCE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 以3只怪兽融合召唤8生命怪兽，立即获得8点格挡。自召唤后的下个玩家回合开始，只要本体仍在场，每回合获得6点格挡。

**升级实际效果：** 以3只怪兽融合召唤8生命怪兽，立即获得12点格挡；后续每个玩家回合开始本体在场时获得8点格挡。

**必要条件与实现补充：** 同名在场来源的维持格挡相加；召唤当前回合不会补领维持格挡。最后来源离场后，能力容器到下次玩家回合开始检查并移除，不产生无来源格挡。维持格挡可以被吞噬继承；励辉士专属本回合锁有效时，原来源和继承来源都不能给予格挡。

**实现位置：** [FullArmorThunderLance.cs:12](<../ThermalVortexCode/Cards/FullArmorThunderLance.cs:12>)；[FullArmorThunderLance.cs:14](<../ThermalVortexCode/Cards/FullArmorThunderLance.cs:14>)；[FullArmorThunderLancePower.cs:16](<../ThermalVortexCode/Powers/FullArmorThunderLancePower.cs:16>)；[CyberSeries.cs:870](<../ThermalVortexCode/Cards/CyberSeries.cs:870>)

### 纳祭魔 · `Relinquished`

内部名称：`THERMALVORTEX-RELINQUISHED`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为1素材） | 0能量（卡面为1素材） |
| 怪兽生命 | 当前/最大生命取被控制仆从的当前/最大生命；未控制时1/1 | 当前/最大生命取被控制仆从的当前/最大生命；未控制时1/1 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：1。  
> 融合素材：场上仅有的1只怪兽。  
> 控制1只敌方仆从，至多3回合。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：1。  
> 融合素材：场上仅有的1只怪兽。  
> 控制1只敌方仆从，至多5回合。  
> 消耗。  

卡文来源：[当前中文资源:136](<../ThermalVortex/localization/zhs/cards.json:136>)，键 `THERMALVORTEX-RELINQUISHED.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 消耗。使用场上唯一1只怪兽为素材召唤，选择1个存活敌方仆从；自身当前/最大生命分别变为该仆从的当前/最大生命，建立双向生命共享，控制其3个敌方回合；控制倒计时结束时，纳祭魔战斗破坏离场。

**升级实际效果：** 消耗。使用场上唯一1只怪兽为素材召唤，选择1个存活敌方仆从；自身当前/最大生命分别变为该仆从的当前/最大生命，建立双向生命共享，控制其5个敌方回合；控制倒计时结束时，纳祭魔战斗破坏离场。

**必要条件与实现补充：** 己方场上必须恰好1只怪兽，敌方目标须有 MinionPower；该唯一场上怪兽是固定素材。控制跳过仆从行动并推进其行动状态，不改变阵营。建立控制时保留目标原本的当前与最大生命，例如目标为20/50时，双方均为20/50。控制期间双方始终共用当前/最大生命：纳祭魔受到伤害时，被控仆从同步失去等量生命；反向伤害、治疗、最大生命变化及直接生命设定也实时同步。伤害先在实际受作用方正常结算格挡和减伤，另一方仅同步最终生命变化，不再次结算格挡、减伤或伤害/治疗效果。纳祭魔提前离场时解除关系；控制倒计时结束时，纳祭魔战斗破坏离场并解除关系。被控仆从确认死亡时同样令纳祭魔战斗破坏离场，原生防死或恢复以敌人最终生命回同步。黑暗决斗保护期间共享生命可同为0且继续控制、同步恢复；保护到期仍为0时只完成一次死亡，零生命时解除控制则按原生流程结算敌人死亡。倒计时在敌方回合结束递减。共享期间生命以敌人为准，吞噬生命贡献仍保存在自身成长中，不增加共享池；后续普通形态恢复生效。伤害与破坏队列绑定当次入场的生命对象，异步回调后及实际离场前复查，已离场再入场的实体不受旧队列影响。 只能经额外牌组授权召唤流程打出，卡面数值为素材要求而非能量；怪兽生命可被其他实际效果改变。 代码登记池仍是 ThermalVortexCardPool，实际走独立额外牌组候选；ExtraDeckCardPool 负责额外牌外观，不是普通主牌奖励来源。

**实现位置：** [Relinquished.cs:45](<../ThermalVortexCode/Cards/Relinquished.cs:45>)；[RelinquishedControlPower.cs:44](<../ThermalVortexCode/Powers/RelinquishedControlPower.cs:44>)；[RelinquishedControlPower.cs:205](<../ThermalVortexCode/Powers/RelinquishedControlPower.cs:205>)；[RelinquishedControlPower.cs:238](<../ThermalVortexCode/Powers/RelinquishedControlPower.cs:238>)；[ThermalVortexCore.cs:3257](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3257>)；[MonsterFieldHealthService.cs:442](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:442>)；[MonsterFieldHealthService.cs:461](<../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:461>)；[RestrictedCardTargetPatch.cs:11](<../ThermalVortexCode/Patches/RestrictedCardTargetPatch.cs:11>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ExtraDeckCard.cs:20](<../ThermalVortexCode/Cards/ExtraDeckCard.cs:20>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 涡旋 · `Vortex`

内部名称：`THERMALVORTEX-VORTEX`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为2素材） | 0能量（卡面为2素材） |
| 怪兽生命 | 8 | 8 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：8。  
> 融合素材：2只怪兽。  
> 将抽牌堆中的1张怪兽加入手牌。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：8。  
> 融合素材：2只怪兽。  
> 将抽牌堆中的2张怪兽加入手牌。  
> 消耗。  

卡文来源：[当前中文资源:96](<../ThermalVortex/localization/zhs/cards.json:96>)，键 `THERMALVORTEX-VORTEX.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 使用恰好2个合法素材召唤；从抽牌堆选1张怪兽加入手牌。消耗。

**升级实际效果：** 使用恰好2个合法素材召唤；从抽牌堆选2张怪兽加入手牌。消耗。

**必要条件与实现补充：** 抽牌堆怪兽不足时选择全部可用数量；直接加入手牌，不算抽牌命令。 只能经额外牌组授权召唤流程打出，卡面数值为素材要求而非能量；怪兽生命可被其他实际效果改变。 代码登记池仍是 ThermalVortexCardPool，实际走独立额外牌组候选；ExtraDeckCardPool 负责额外牌外观，不是普通主牌奖励来源。

**实现位置：** [Vortex.cs:8](<../ThermalVortexCode/Cards/Vortex.cs:8>)；[ThermalVortexCore.cs:2899](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2899>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ExtraDeckCard.cs:20](<../ThermalVortexCode/Cards/ExtraDeckCard.cs:20>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)

### 拉之翼神龙-球形体 · `WingedDragonOfRaSphereMode`

内部名称：`THERMALVORTEX-WINGED_DRAGON_OF_RA_SPHERE_MODE`。所属牌池：额外牌组奖励可选候选；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0能量（卡面为X素材） | 0能量（卡面为X素材） |
| 怪兽生命 | 1 | max(1,所选全部场上素材当前生命之和) |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 融合怪兽。生命：1。  
> 融合素材：满场时的全部怪兽。  
> 仅由融合召唤。  
> 下回合开始变为拉之翼神龙-不死鸟。  
> 消耗。  

**升级卡面文本：**

> 融合怪兽。生命：融合素材使用前生命总和。  
> 融合素材：满场时的全部怪兽。  
> 仅由融合召唤。  
> 下回合开始变为拉之翼神龙-不死鸟。  
> 消耗。  

卡文来源：[当前中文资源:253](<../ThermalVortex/localization/zhs/cards.json:253>)，键 `THERMALVORTEX-WINGED_DRAGON_OF_RA_SPHERE_MODE.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 仅通过融合召唤；场上怪兽数等于当前正容量时，将全部场上怪兽作为素材，召唤1生命球形体。下次自己的回合开始若仍在场，消耗本体并转为不死鸟。消耗。

**升级实际效果：** 仅通过融合召唤；场上怪兽数等于当前正容量时，将全部场上怪兽作为素材，召唤生命等于这些素材当前生命之和（最低1）的球形体。下次自己的回合开始若仍在场，消耗本体并转为不死鸟。消耗。

**必要条件与实现补充：** 限融合的场上模式或升级融合的场上＋手牌模式；即使升级融合也固定只用全部场上怪兽，不用手牌。怪兽位容量须大于0且恰好填满；素材数量不是固定3。升级生命在移动素材前捕获，素材生命总和不足1时仍可召唤并按1生命结算；不死鸟基础为未升级1生命，不继承球体的素材生命；自身吞噬成长另行迁移。转换使用原怪兽位，未完成且本体尚在场则保留后续尝试。 融合召唤提交成功后球形体移到最左端。 只能经额外牌组授权召唤流程打出，卡面数值为素材要求而非能量；怪兽生命可被其他实际效果改变。 代码登记池仍是 ThermalVortexCardPool，实际走独立额外牌组候选；ExtraDeckCardPool 负责额外牌外观，不是普通主牌奖励来源。

**实现位置：** [ChaosPhantomAndRaCards.cs:815](<../ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:815>)；[ThermalVortexCore.cs:2981](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:2981>)；[ThermalVortexCore.cs:3233](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:3233>)；[NewCardPowers.cs:346](<../ThermalVortexCode/Powers/NewCardPowers.cs:346>)；[ThermalVortexCore.cs:538](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:538>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ExtraDeckCard.cs:20](<../ThermalVortexCode/Cards/ExtraDeckCard.cs:20>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)


## 生成专用牌（12 张）

### 电子龙 · `CyberDragon`

内部名称：`THERMALVORTEX-CYBER_DRAGON`。所属牌池：生成专用；效果生成；类型：攻击；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 2；在手牌且场上或前序待入场区无有效电子龙身份时为0 | 2；在手牌且场上或前序待入场区无有效电子龙身份时为0 |
| 怪兽生命 | 5 | 5 |
| 固有关键词 | 无 | 无 |

**基础卡面文本：**

> 怪兽。生命：5。  
> 场上没有电子龙时，费用为0。  
> 造成5点伤害。  

**升级卡面文本：**

> 怪兽。生命：5。  
> 场上没有电子龙时，费用为0。  
> 造成8点伤害。  

卡文来源：[当前中文资源:35](<../ThermalVortex/localization/zhs/cards.json:35>)，键 `THERMALVORTEX-CYBER_DRAGON.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤5生命电子怪兽，对选择的敌人造成5点加继承攻击力的攻击伤害。在手牌且场上或前序待入场区没有电子龙时，固有费用为0。

**升级实际效果：** 召唤5生命电子怪兽，对选择的敌人造成8点加继承攻击力的攻击伤害。在手牌且场上或前序待入场区没有电子龙时，固有费用为0。

**必要条件与实现补充：** 仅由效果生成，普通奖励不提供。身份检查包含真实场上实体及嵌套连锁中已经完成自身召唤步骤的前序待入场怪兽，使用有效身份，包含完整复制电子龙的混沌；只有手牌满足该固有0费判断。某些生成入口可额外赋予本场0费，费用修正仍由对应生成入口决定。

**实现位置：** [CyberDragon.cs:12](<../ThermalVortexCode/Cards/CyberDragon.cs:12>)；[CyberDragon.cs:14](<../ThermalVortexCode/Cards/CyberDragon.cs:14>)；[CyberDragon.cs:42](<../ThermalVortexCode/Cards/CyberDragon.cs:42>)；[CyberSeries.cs:52](<../ThermalVortexCode/Cards/CyberSeries.cs:52>)；[NewCardRulesPatch.cs:1](<../ThermalVortexCode/Patches/NewCardRulesPatch.cs:1>)

### 电子虫 · `CyberLarva`

内部名称：`THERMALVORTEX-CYBER_LARVA`。所属牌池：生成专用；效果生成；类型：状态；稀有度：状态。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 1 | 1 |
| 固有关键词 | 消耗 | 无 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 召唤后死亡。  
> 消耗。  

**升级卡面文本：**

> 怪兽。生命：1。  

卡文来源：[当前中文资源:166](<../ThermalVortex/localization/zhs/cards.json:166>)，键 `THERMALVORTEX-CYBER_LARVA.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤1生命电子怪兽，完成入场及继承的召唤效果后消耗自身。

**升级实际效果：** 移除消耗及原生入场自耗，成为可留场的1生命怪兽。

**必要条件与实现补充：** 仅由效果生成；未升级离场原因是消耗，不是战斗破坏或素材使用。电子革命系统可把两版费用设为0。

**实现位置：** [CyberLarva.cs:9](<../ThermalVortexCode/Cards/CyberLarva.cs:9>)；[CyberLarva.cs:18](<../ThermalVortexCode/Cards/CyberLarva.cs:18>)；[SummonRulesPower.cs:79](<../ThermalVortexCode/Powers/SummonRulesPower.cs:79>)；[CyberSeries.cs:52](<../ThermalVortexCode/Cards/CyberSeries.cs:52>)

### 被封印千年的左手 · `ExodiaLeftArm`

内部名称：`THERMALVORTEX-EXODIA_LEFT_ARM`。所属牌池：生成专用；效果生成；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 不可升级 |
| 怪兽生命 | 1 | 不可升级 |
| 固有关键词 | 消耗 | 不可升级 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 召唤后，千年控牌15张。  
> 唯一手牌持有：每使用一张千年卡，控牌5张。  
> 消耗。  

**升级卡面文本：**

> 不可升级。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-EXODIA_LEFT_ARM.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 查看顶15张，可弃任意张。唯一手牌持有：控牌5张。

**升级实际效果：** 不可升级，沿用基础规则。

**必要条件与实现补充：** 仅在手牌中生效，同名部件只提供一次。使用千年卡前记录手牌，排除本次打出的那张牌；另有同名副本仍可生效。结算中新获得的部件从下一张牌起生效。原牌及召唤完成后，依次结算控牌、抽牌、加能量、融合，不限每回合次数。 千年的大义贼、千年召唤者、千年的密钥、千年邪、千年守护者成功召唤时也触发已快照的唯一手牌持有效果，包括正常融合、直接特殊召唤及再次入场。左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。千年召唤者、千年邪与千年守护者均计入千年卡及千年怪兽，也可作为要求千年怪兽的融合素材；它们仍是额外怪兽，不进入千年奖励怪兽生成池。 所有部件0费、1生命、技能型怪兽、消耗、不可升级；普通召唤须有怪兽位。成功召唤后的本体效果包括直接特殊召唤及再次入场；本体效果可被完整复制与吞噬继承，但吞噬当下不发放奖励，宿主以后入场逐份结算。手牌持有不能吞噬继承。查看抽牌堆顶指定数量的牌，可选择任意张置入弃牌堆，包括0张。抽牌堆不足时，保持原牌序，将弃牌堆洗混补至底部；总数不足则查看全部。每次查看只补洗一次，刚弃掉的牌不在同次查看中再次补洗，未弃掉的牌保持顺序。

**实现位置：** [ExodiaLeftArm.cs:9](<../ThermalVortexCode/Cards/ExodiaLeftArm.cs:9>)；[ExodiaLeftArm.cs:18](<../ThermalVortexCode/Cards/ExodiaLeftArm.cs:18>)；[MillenniumSeries.cs:24](<../ThermalVortexCode/Cards/MillenniumSeries.cs:24>)；[ThermalVortexGeneratedCards.cs:7](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:7>)

### 被封印千年的左腿 · `ExodiaLeftLeg`

内部名称：`THERMALVORTEX-EXODIA_LEFT_LEG`。所属牌池：生成专用；效果生成；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 不可升级 |
| 怪兽生命 | 1 | 不可升级 |
| 固有关键词 | 消耗 | 不可升级 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 召唤后，获得3点能量。  
> 唯一手牌持有：每使用一张千年卡，获得1点能量。  
> 消耗。  

**升级卡面文本：**

> 不可升级。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-EXODIA_LEFT_LEG.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 获得3点能量。唯一手牌持有：获得1点能量。

**升级实际效果：** 不可升级，沿用基础规则。

**必要条件与实现补充：** 仅在手牌中生效，同名部件只提供一次。使用千年卡前记录手牌，排除本次打出的那张牌；另有同名副本仍可生效。结算中新获得的部件从下一张牌起生效。原牌及召唤完成后，依次结算控牌、抽牌、加能量、融合，不限每回合次数。 千年的大义贼、千年召唤者、千年的密钥、千年邪、千年守护者成功召唤时也触发已快照的唯一手牌持有效果，包括正常融合、直接特殊召唤及再次入场。左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。千年召唤者、千年邪与千年守护者均计入千年卡及千年怪兽，也可作为要求千年怪兽的融合素材；它们仍是额外怪兽，不进入千年奖励怪兽生成池。 所有部件0费、1生命、技能型怪兽、消耗、不可升级；普通召唤须有怪兽位。成功召唤后的本体效果包括直接特殊召唤及再次入场；本体效果可被完整复制与吞噬继承，但吞噬当下不发放奖励，宿主以后入场逐份结算。手牌持有不能吞噬继承。

**实现位置：** [ExodiaLeftLeg.cs:8](<../ThermalVortexCode/Cards/ExodiaLeftLeg.cs:8>)；[ExodiaLeftLeg.cs:17](<../ThermalVortexCode/Cards/ExodiaLeftLeg.cs:17>)；[MillenniumSeries.cs:24](<../ThermalVortexCode/Cards/MillenniumSeries.cs:24>)；[ThermalVortexGeneratedCards.cs:7](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:7>)

### 被封印千年的右手 · `ExodiaRightArm`

内部名称：`THERMALVORTEX-EXODIA_RIGHT_ARM`。所属牌池：生成专用；效果生成；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 不可升级 |
| 怪兽生命 | 1 | 不可升级 |
| 固有关键词 | 消耗 | 不可升级 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 召唤后，抽6张牌。  
> 唯一手牌持有：每使用一张千年卡，抽2张牌。  
> 消耗。  

**升级卡面文本：**

> 不可升级。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-EXODIA_RIGHT_ARM.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 抽6张牌。唯一手牌持有：抽2张牌。

**升级实际效果：** 不可升级，沿用基础规则。

**必要条件与实现补充：** 仅在手牌中生效，同名部件只提供一次。使用千年卡前记录手牌，排除本次打出的那张牌；另有同名副本仍可生效。结算中新获得的部件从下一张牌起生效。原牌及召唤完成后，依次结算控牌、抽牌、加能量、融合，不限每回合次数。 千年的大义贼、千年召唤者、千年的密钥、千年邪、千年守护者成功召唤时也触发已快照的唯一手牌持有效果，包括正常融合、直接特殊召唤及再次入场。左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。千年召唤者、千年邪与千年守护者均计入千年卡及千年怪兽，也可作为要求千年怪兽的融合素材；它们仍是额外怪兽，不进入千年奖励怪兽生成池。 所有部件0费、1生命、技能型怪兽、消耗、不可升级；普通召唤须有怪兽位。成功召唤后的本体效果包括直接特殊召唤及再次入场；本体效果可被完整复制与吞噬继承，但吞噬当下不发放奖励，宿主以后入场逐份结算。手牌持有不能吞噬继承。

**实现位置：** [ExodiaRightArm.cs:9](<../ThermalVortexCode/Cards/ExodiaRightArm.cs:9>)；[ExodiaRightArm.cs:18](<../ThermalVortexCode/Cards/ExodiaRightArm.cs:18>)；[MillenniumSeries.cs:24](<../ThermalVortexCode/Cards/MillenniumSeries.cs:24>)；[ThermalVortexGeneratedCards.cs:7](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:7>)

### 被封印千年的右腿 · `ExodiaRightLeg`

内部名称：`THERMALVORTEX-EXODIA_RIGHT_LEG`。所属牌池：生成专用；效果生成；类型：技能；稀有度：普通。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 不可升级 |
| 怪兽生命 | 1 | 不可升级 |
| 固有关键词 | 消耗 | 不可升级 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 在场时，每次友方怪兽召唤后，可千年融合。  
> 唯一手牌持有：每使用一张千年卡，可千年融合。  
> 消耗。  

**升级卡面文本：**

> 不可升级。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-EXODIA_RIGHT_LEG.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 在场时，每次友方怪兽成功召唤后，可进行一次手牌与场上素材的融合。唯一手牌持有：可进行一次手牌与场上素材的融合。

**升级实际效果：** 不可升级，沿用基础规则。

**必要条件与实现补充：** 仅在手牌中生效，同名部件只提供一次。使用千年卡前记录手牌，排除本次打出的那张牌；另有同名副本仍可生效。结算中新获得的部件从下一张牌起生效。原牌及召唤完成后，依次结算控牌、抽牌、加能量、融合，不限每回合次数。 千年的大义贼、千年召唤者、千年的密钥、千年邪、千年守护者成功召唤时也触发已快照的唯一手牌持有效果，包括正常融合、直接特殊召唤及再次入场。左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。千年召唤者、千年邪与千年守护者均计入千年卡及千年怪兽，也可作为要求千年怪兽的融合素材；它们仍是额外怪兽，不进入千年奖励怪兽生成池。 所有部件0费、1生命、技能型怪兽、消耗、不可升级；普通召唤须有怪兽位。成功召唤后的本体效果包括直接特殊召唤及再次入场；本体效果可被完整复制与吞噬继承，但吞噬当下不发放奖励，宿主以后入场逐份结算。手牌持有不能吞噬继承。可使用手牌和场上的合法怪兽素材，按升级融合的全部条件召唤1只融合怪兽，可以跳过。同一张牌的使用及自身入场只提供一次机会，同事件的手牌、场上与继承来源不叠加。新怪兽入场是新事件，仍有场上右足能力时可以继续融合。

**实现位置：** [ExodiaRightLeg.cs:8](<../ThermalVortexCode/Cards/ExodiaRightLeg.cs:8>)；[ExodiaRightLeg.cs:17](<../ThermalVortexCode/Cards/ExodiaRightLeg.cs:17>)；[MillenniumSeries.cs:24](<../ThermalVortexCode/Cards/MillenniumSeries.cs:24>)；[ThermalVortexGeneratedCards.cs:7](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:7>)

### 被封印千年的躯干 · `ExodiaTorso`

内部名称：`THERMALVORTEX-EXODIA_TORSO`。所属牌池：生成专用；效果生成；类型：技能；稀有度：罕见。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 不可升级 |
| 怪兽生命 | 1 | 不可升级 |
| 固有关键词 | 消耗 | 不可升级 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 召唤后，恢复10点生命。  
> 唯一手牌持有：所有手牌中的被封印千年部件获得保留。  
> 手牌集齐五种部件时，赢得战斗。  
> 消耗。  

**升级卡面文本：**

> 不可升级。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-EXODIA_TORSO.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 玩家恢复10点生命。唯一手牌持有：手牌中全部封印部件获得保留，包括自身。

**升级实际效果：** 不可升级，沿用基础规则。

**必要条件与实现补充：** 仅在手牌中生效，同名部件只提供一次。使用千年卡前记录手牌，排除本次打出的那张牌；另有同名副本仍可生效。结算中新获得的部件从下一张牌起生效。原牌及召唤完成后，依次结算控牌、抽牌、加能量、融合，不限每回合次数。 千年的大义贼、千年召唤者、千年的密钥、千年邪、千年守护者成功召唤时也触发已快照的唯一手牌持有效果，包括正常融合、直接特殊召唤及再次入场。左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。千年召唤者、千年邪与千年守护者均计入千年卡及千年怪兽，也可作为要求千年怪兽的融合素材；它们仍是额外怪兽，不进入千年奖励怪兽生成池。 所有部件0费、1生命、技能型怪兽、消耗、不可升级；普通召唤须有怪兽位。成功召唤后的本体效果包括直接特殊召唤及再次入场；本体效果可被完整复制与吞噬继承，但吞噬当下不发放奖励，宿主以后入场逐份结算。手牌持有不能吞噬继承。恢复普通当前生命，不提高玩家最大生命。保留由手牌躯干动态提供；躯干离开手牌即停止，不影响其他保留来源。只认手牌中五种部件，每战斗仅提交一次胜利。

**实现位置：** [ExodiaTorso.cs:14](<../ThermalVortexCode/Cards/ExodiaTorso.cs:14>)；[ExodiaTorso.cs:23](<../ThermalVortexCode/Cards/ExodiaTorso.cs:23>)；[ExodiaTorso.cs:46](<../ThermalVortexCode/Cards/ExodiaTorso.cs:46>)；[ExodiaTorso.cs:92](<../ThermalVortexCode/Cards/ExodiaTorso.cs:92>)；[MillenniumSeries.cs:24](<../ThermalVortexCode/Cards/MillenniumSeries.cs:24>)

### 幻之召唤神 艾克佐迪亚 · `PhantomSummoningGodExodia`

内部名称：`THERMALVORTEX-PHANTOM_SUMMONING_GOD_EXODIA`。所属牌池：生成专用；（千年十字）；类型：技能；稀有度：衍生。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 0 |
| 怪兽生命 | 1 | 1 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 对所有敌人造成X点伤害X次。X为召唤它的千年十字使用时的千年计数。  
> 消耗。  

**升级卡面文本：**

> 怪兽。生命：1。  
> 对所有敌人造成X点伤害X次。X为召唤它的千年十字使用时的千年计数。  
> 消耗。  

卡文来源：[当前中文资源](../ThermalVortex/localization/zhs/cards.json)，键 `THERMALVORTEX-PHANTOM_SUMMONING_GOD_EXODIA.description`；原生关键词按实际卡牌附加。

**基础实际效果：** 有合法怪兽位时召唤自身；若携带千年十字记录的计数N，对所有敌人造成N点无强化伤害，共N段。消耗。

**升级实际效果：** 有合法怪兽位时召唤自身；若携带千年十字记录的计数N，对所有敌人造成N点无强化伤害，共N段。消耗。

**必要条件与实现补充：** 仅由千年十字生成；每只只保留生成它的那次十字在使用前锁定的计数，之后的十字重新计算并生成新实体，不覆盖旧记录；未携带计数时不造成该伤害。每实体的千年十字伤害只结算一次，爱丽丝再召唤同一实体时保留原记录且不重复伤害，完整复制保留原计数并有独立执行重置。无合法怪兽位时不能打出，也不结算召唤伤害。基础与升级均有消耗，但成功召唤后仍正常留场；战斗破坏、通常送墓型素材或清场离场时改入消耗堆，明确指定回抽牌堆的效果仍按指定去向处理。名称不含“千年”，故不计入千年卡或千年怪兽， 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记继承角色主牌池，但 Token 稀有度被普通奖励候选过滤；生成来源与登记池不同。 一次CardAttackAllEnemies调用生成基础段数为N的原生AttackCommand；公共CreateCalculatedAttack读取变量Props中的Unpowered标记并显式调用attack.Unpowered()，使无强化属性落实到伤害命令。敌人数不增加段数；仍接受原生攻击次数修正，无存活目标时可提前结束。未计格挡、伤害及次数修正时，承受全部N段的单名敌人理论总伤害为N²。

**实现位置：** [MillenniumCross.cs:86](<../ThermalVortexCode/Cards/MillenniumCross.cs:86>)；[MillenniumSeries.cs:52](<../ThermalVortexCode/Cards/MillenniumSeries.cs:52>)；[SummonRulesPower.cs:144](<../ThermalVortexCode/Powers/SummonRulesPower.cs:144>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)；[ThermalVortexGeneratedCards.cs:13](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:13>)；[MillenniumCross.cs:96](<../ThermalVortexCode/Cards/MillenniumCross.cs:96>)；[MillenniumCross.cs:125](<../ThermalVortexCode/Cards/MillenniumCross.cs:125>)；[ThermalVortexCombatVfx.cs:31](<../ThermalVortexCode/Vfx/ThermalVortexCombatVfx.cs:31>)

### 炉渣 · `Slag`

内部名称：`THERMALVORTEX-SLAG`。所属牌池：生成专用；（过载抽取）；类型：状态；稀有度：状态。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 1 | 1 |
| 怪兽生命 | 不适用 | 不适用 |
| 固有关键词 | 消耗 | 消耗 |

**基础卡面文本：**

> 消耗。  

**升级卡面文本：**

> 消耗。  

卡文来源：[当前中文资源:235](<../ThermalVortex/localization/zhs/cards.json:235>)，键 `THERMALVORTEX-SLAG.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 无主动效果；打出后消耗。

**升级实际效果：** 无主动效果；打出后消耗。

**必要条件与实现补充：** 状态牌，过载抽取生成；升级不改变费用、效果或关键词。 登记继承角色主牌池，但状态稀有度及生成专用规则排除普通奖励。

**实现位置：** [NewMainDeckCards.cs:219](<../ThermalVortexCode/Cards/NewMainDeckCards.cs:219>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexGeneratedCards.cs:13](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:13>)

### 玩具怪兽 · `ToyBoxToken`

内部名称：`THERMALVORTEX-TOY_BOX_TOKEN`。所属牌池：生成专用；（玩具盒）；类型：技能；稀有度：衍生。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 0 |
| 怪兽生命 | 1 | 1 |
| 固有关键词 | 无 | 消耗 |

**基础卡面文本：**

> 怪兽。生命：1。  

**升级卡面文本：**

> 怪兽。生命：1。  
> 消耗。  

卡文来源：[当前中文资源:156](<../ThermalVortex/localization/zhs/cards.json:156>)，键 `THERMALVORTEX-TOY_BOX_TOKEN.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤自身。

**升级实际效果：** 召唤自身；带消耗，离场按消耗规则处理。

**必要条件与实现补充：** 玩具盒生成的衍生怪兽；基础版不带消耗，升级版新增消耗。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记继承角色主牌池，但 Token 稀有度被普通奖励候选过滤；生成来源与登记池不同。

**实现位置：** [ToyBox.cs:33](<../ThermalVortexCode/Cards/ToyBox.cs:33>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)；[ThermalVortexGeneratedCards.cs:13](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:13>)

### 拉之翼神龙 · `WingedDragonOfRa`

内部名称：`THERMALVORTEX-WINGED_DRAGON_OF_RA`。所属牌池：生成专用；（不死鸟形态转换）；类型：攻击；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 不可升级 |
| 怪兽生命 | 1 | 不可升级 |
| 固有关键词 | 消耗 | 不可升级 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 选择1名敌人。  
> 对所有敌人造成等同于该敌人本场累计攻击伤害的伤害。  
> 消耗。  

**升级卡面文本：**

> 不可升级。  

卡文来源：[当前中文资源:257](<../ThermalVortex/localization/zhs/cards.json:257>)，键 `THERMALVORTEX-WINGED_DRAGON_OF_RA.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤自身，选择1名敌人；读取本场战斗该敌人对你发出的攻击伤害记录D，对每名当前可命中敌人各造成D点无强化伤害。消耗。

**升级实际效果：** 不可升级；无升级版本。

**必要条件与实现补充：** 仅由不死鸟形态转换生成，MaxUpgradeLevel=0。D为敌方对本玩家攻击结算分段在玩家格挡与怪兽场吸收前的完整传入伤害向上取整之和，不是意图伤害，也不是玩家实际失血；按选定单个敌人的记录决定对全体的共同伤害。没有记录／核心时D=0。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记继承角色主牌池，但生成专用规则明确排除普通奖励；生成无色外观不等同普通无色奖励资格。

**实现位置：** [ChaosPhantomAndRaCards.cs:913](<../ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:913>)；[MonsterFieldDamageGuardPatch.cs:456](<../ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:456>)；[ThermalVortexCore.cs:395](<../ThermalVortexCode/Relics/ThermalVortexCore.cs:395>)；[NewCardPowers.cs:346](<../ThermalVortexCode/Powers/NewCardPowers.cs:346>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)；[ThermalVortexGeneratedCards.cs:13](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:13>)

### 拉之翼神龙-不死鸟 · `WingedDragonOfRaPhoenix`

内部名称：`THERMALVORTEX-WINGED_DRAGON_OF_RA_PHOENIX`。所属牌池：生成专用；（球形体形态转换）；类型：技能；稀有度：稀有。

| 项目 | 基础 | 升级 |
|---|---|---|
| 能量费用 | 0 | 不可升级 |
| 怪兽生命 | 1 | 不可升级 |
| 固有关键词 | 消耗 | 不可升级 |

**基础卡面文本：**

> 怪兽。生命：1。  
> 你与本怪兽被同一段敌人攻击击杀时，一同复活：你的当前生命恢复为该段完整攻击伤害，最大生命增加同样数值。  
> 下回合开始时，变为拉之翼神龙。  
> 消耗。  

**升级卡面文本：**

> 不可升级。  

卡文来源：[当前中文资源:257](<../ThermalVortex/localization/zhs/cards.json:257>)，键 `THERMALVORTEX-WINGED_DRAGON_OF_RA_PHOENIX.description`；原生关键词按对应等级自动附加。

**基础实际效果：** 召唤自身；若同一敌方攻击分段击毁本体且随后令玩家致死，玩家最大生命增加该段吸收前的完整伤害D，当前生命设为D，并将不死鸟按1点基础生命加自身吞噬生命成长恢复回场。下一次自己的回合开始，若仍在场则消耗本体并转为拉之翼神龙。消耗。

**升级实际效果：** 不可升级；无升级版本。

**必要条件与实现补充：** 仅由球形体转换生成，不能升级。本体基础生命仍为1，复活保留本场吞噬成长并按有效生命上限恢复。真实不死鸟或具有不死鸟完整复制身份的怪兽在场时，玩家显示“不死鸟复活”能力；该能力只展示复活规则，不另行触发复活或改变数值。不再绑定独立的“不死鸟复活”黄色词解释，保留拉之翼神龙的卡牌预览。复活绑定同一攻击分段，每个分段身份只可消费一次；非攻击失血、之前已击毁的不死鸟、不同后续攻击段致死不满足。D取玩家格挡和怪兽吸收前的该段传入非负伤害截去小数部分，不用残余失血或历史平方序列。转换优先保留原怪兽位；新形态实际入场时提交吞噬成长迁移，旧卡以后回收不再拥有该份成长；入场后立即离场仍算成功。未入场则回退本次迁移，保留回调期间另行获得的成长。受目标条件阻止而未完成时，在原体仍在场的前提下下回合再尝试。 怪兽生命为无其他能力、复制或增减生命效果时的卡牌自身值；通常召唤需合法怪兽位。 登记继承角色主牌池，但生成专用规则明确排除普通奖励；生成无色外观不等同普通无色奖励资格。

**结算补充：** 复活回血和恢复场地属于不死鸟自身效果。完整复制形态在实际离场前保存，复活恢复同一实体；资格限定本攻击段且只消费一次。怪兽恢复生命按 GetInitialMaxHp：本体为1＋自身吞噬生命成长，混沌为保存的复制生命基础＋宿主自身吞噬生命成长，不取死亡前所有临时生命上限。

**实现位置：** [ChaosPhantomAndRaCards.cs:880](<../ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:880>)；[PhoenixRevivalPower.cs:15](<../ThermalVortexCode/Powers/PhoenixRevivalPower.cs:15>)；[MonsterFieldDamageGuardPatch.cs:640](<../ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:640>)；[MonsterFieldDamageGuardPatch.cs:962](<../ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:962>)；[NewCardRulesPatch.cs:34](<../ThermalVortexCode/Patches/NewCardRulesPatch.cs:34>)；[NewCardPowers.cs:461](<../ThermalVortexCode/Powers/NewCardPowers.cs:461>)；[NewCardPowers.cs:346](<../ThermalVortexCode/Powers/NewCardPowers.cs:346>)；[RewardPoolCatalog.cs:428](<../ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:428>)；[ThermalVortexCard.cs:271](<../ThermalVortexCode/Cards/ThermalVortexCard.cs:271>)；[MonsterFieldService.cs:66](<../ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)；[ThermalVortexGeneratedCards.cs:13](<../ThermalVortexCode/Cards/ThermalVortexGeneratedCards.cs:13>)；[MonsterFieldDamageGuardPatch.cs:672](<../ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:672>)；[MonsterFieldDamageGuardPatch.cs:714](<../ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:714>)

