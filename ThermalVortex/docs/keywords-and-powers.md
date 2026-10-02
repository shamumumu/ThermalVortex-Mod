# 武藤游戏：关键词、能力与预览说明

依据当前工作区 C# 实现及中英文本资源整理。内部标识继续使用 ThermalVortex；本页解释现行行为，不把旧审阅意见或历史 PASS 当作事实。原生引擎/第三方钩子只记录本项目的接入边界，未做运行时验收。

## 中文文案写作约定

**现有文案仍准确，就保持原样。** 此规则同时保护基础与升级卡面、黄字关键词解释、能力说明、动态／预览提示，以及文档和正式工作簿中的对应文案。debug、边界条件修复、动画速度或等待时间调整，以及让实现符合既定规则的时序修复，都不自动触发文案修改。

只有用户明确要求改文案，或本次实际改变玩法、数值、适用条件、生效回合等并使原显示内容失准，才修改对应内容。只调整失准的数字、词语或语句，保留其余措辞、术语、句式和简洁程度，不顺手润色、重写整段、另造关键词或追加边界例外。动态参数能正确显示新数值时，只改参数或绑定，保留文字模板；显示绑定故障按显示问题修复，不扩展为措辞调整。

以下写作风格仅在确定需要新增或修改文案后适用：先参照同类卡的定稿。能在卡效中简单清楚表达的内容直接写，需要共用概念时才设置黄字关键词，解释用最少篇幅说明含义及必要信息。详细结算、特殊交互和逐项例外保留在实现补充或[玩法系统](gameplay-systems.md)，不自动回填黄字解释。基础、升级及动态说明使用同一套术语，卡面省略细节不改变实际规则。

资料同步只更新本次改动导致失实的既有实现说明、数值、升级对照或文案字段。准确记录保持原样，不要求每次 debug 新增说明或修复记录，保留工作簿内用户填写的待调整值和修改意见。信息不足时先完成可确定的修复并保留原文，只有缺失信息影响玩法决定时才询问。

| 本次修改 | 文案与资料处理 |
| --- | --- |
| 特效等待从 0.8 秒调整为 0.3 秒，玩法不变 | 效果文案不变；既有文档若记录了时长，仅更新该时长。 |
| 修复目标死亡后重复触发、选卡卡住或结算顺序错误 | 保留准确的效果文案，不追加异常情况说明。 |
| 伤害从 8 改为 10，模板为“造成 `{Damage}` 点伤害” | 更新参数和失实的数值记录，模板不变。 |
| 实际规则从“本回合结束”改为“下回合开始” | 只改失准的时间短语；若只是修复实现以符合原文，则原文不变。 |

## 解释的绑定方式

卡牌关键词解释通过 `WithExplanations` / `KeywordExplanation` 显式绑定；`[gold]` 只控制卡面和能力正文的颜色，本身不会生成解释。`appliesTo` 决定当前等级或状态是否生成解释，绑定 ID 用于去重。融合召唤继续附带素材、额外牌组与融合怪兽；融合素材继续附带融合召唤与素材。`XyzMonsterCard` 的融合素材绑定由每张具体额外怪兽继承，依赖递归去重并防止循环。吞噬状态说明由首次解释绑定附加，按当前实例或展示快照生成。见 [WithExplanations](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ThermalVortexCard.cs:108>)、[XyzMonsterCard 公共绑定](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExtraDeckCard.cs:25>)。

卡面中已采用的关键词、原生词和明确引用的卡名按完整词义逐次标黄；同一词再次出现时同样标黄。融合素材、融合怪兽、融合召唤等复合词整体处理，不嵌套标黄，也不把电子龙无限等完整卡名拆成“电子龙”。怪兽位和消耗堆保持原样，不拆开标黄或另补内部词的解释。电子龙自身“场上没有电子龙时”的自指保留普通文字，不添加自己的卡片预览。球形体“仅由融合召唤”中的“融合”是指定卡名，标黄范围为“融合”，对应融合卡片预览。

原生说明通过以下 helper 接入同一绑定列表，只显示解释，不赋予真实关键词、能力、格挡或能量。原生 `HoverTip` 的 ID 与内容保持不变，最终 `CardModel.HoverTips` 的 `Distinct()` 可合并同一原生工厂生成的重复提示。

| 卡面词 | 解释绑定 | 原生自动提示的处理 |
|---|---|---|
| 消耗等原生关键词 | `NativeKeywordExplanation(CardKeyword, appliesTo)` → `HoverTipFactory.FromKeyword` | 已有同名真实关键词时不重复；已有虚无时，消耗说明由原生一并提供。附加条件与此判断同时成立才新增解释。 |
| 格挡 | `BlockExplanation()` → `HoverTipFactory.Static(StaticHoverTip.Block)` | `GainsBlock` 为真时由原生自动提供；正文其他格挡用法可单独补解释。 |
| 易伤、力量 | `PowerExplanation<T>()` → `HoverTipFactory.FromPower<T>(null)` | 使用当前原生文字说明；易伤既有直加提示已迁入同一绑定入口。 |
| 能量 | `EnergyExplanation()` → `HoverTipFactory.ForEnergy(card)` | `WithEnergy` 既有自动提示保留；新增解释沿用同一工厂，重复值在最终列表合并。 |

helper 位置：[NativeKeywordExplanation](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ThermalVortexCard.cs:165>)、[BlockExplanation](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ThermalVortexCard.cs:176>)、[PowerExplanation](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ThermalVortexCard.cs:183>)、[EnergyExplanation](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ThermalVortexCard.cs:189>)。判断解释是否已有时不读取 `card.HoverTips`，避免解释生成过程递归。

卡名提示使用 `CardPreviewExplanation<T>`，不计作关键词文字说明。既有生成、变身预览保持原来的升级匹配：默认随来源升级，显式 `matchSourceUpgrade:false` 保持基础牌，并始终服从目标自身的升级上限。电子嵌合龙和电子龙无限的素材条件使用基础电子龙预览；基础预览用于说明身份，不把可选素材限制为未升级电子龙。电子终结龙的素材条件使用电子怪兽系列说明。融合准备生成的融合、球形体对应的不死鸟，以及不死鸟对应的翼神龙本体仍固定展示基础版本。全部静态调用见下方“全部静态卡牌预览绑定”。

能力图标旁的解释由 `ThermalVortexPower.ExtraHoverTips` 接入 `PowerExplanationBindings`，按具体能力类型和当前状态生成；复用现有关键词说明与依赖展开、去重规则。已核对的46项能力在中英文普通说明、动态说明及 `choose`、`phoenix`、`ra`、`resolved` 分支中，对有对应解释的词按语义标黄。没有对应词的正文不强行增加标记；隐藏的出牌计数仍不显示图标。见 [能力解释入口](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/ThermalVortexPower.cs:25>)、[PowerExplanationBindings](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/PowerExplanationBindings.cs>)。

能力中的消耗、格挡、力量、易伤与能量分别使用原生 `Exhaust`、`Block`、`Strength`、`Vulnerable`、`Energy` 提示；解释展示不授予相应关键词、能力或资源。怪兽位不拆成怪兽，休眠磁场“保留一半”不绑定卡牌的保留关键词，电子龙芯或电子龙无限的完整名称不因包含“电子龙”而误附电子龙预览。融合怪兽、融合召唤等复合词整体标黄，不重叠嵌套；卡名按完整引用处理，已有“不死鸟”等简称按对应卡牌身份解释。

能力卡牌预览与正文采用同一有效状态：

| 能力 | 图标旁的卡牌预览 |
|---|---|
| 封印的黄金柜 | 使用该图标绑定的真实封印牌所对应的预览副本，保留其升级与当前牌面，预览不修改原实体；`SealedCardName` 整体标黄，不固定为示例电子龙。 |
| 电子灯塔、电子废品站 | 根据能力当前有效升级状态展示电子龙或电子龙+，与整体标黄的 `GeneratedCardName` 一致。 |
| 电子龙芯、玩具盒 | 电子龙芯按仍在场的待变身实体及复制来源状态，玩具盒按已登记的来源升级状态，展示对应电子龙或玩具怪兽；普通与升级来源并存时分别保留两种预览，不合并成单一升级版。 |
| 电子革命系统 | 提供基础电子虫的身份预览，不按能力来源推断升级；该预览不表示效果只作用于未升级电子虫。 |
| 翼神龙的涅槃 | 提供当前有效转换分支涉及的卡牌，球形体保留相关来源的有效升级等级；两类转换并存时合并所需预览，不重复列出不死鸟。模板或未绑定有效转换时，与普通正文一致展示两种转换。 |
| 力量焊接 | 等待强化触发时提供怪兽说明；仅剩 `resolved` 减伤正文时不再附加怪兽说明。 |

本页下方的普通说明与战斗显示保留去除颜色标记后的文字，具体颜色及图标旁解释以资源与上述绑定为准。解释中的通常规则仍服从能力明确列出的例外，例如黑暗决斗的零生命留场、爱丽丝的消耗后召唤及休眠磁场的格挡保留；绑定解释不改变这些效果。

## 卡面直接表述的规则

### 主牌奖励范围

中文卡面和能力直接写“可作为主牌奖励的千年怪兽”，只沿用“千年怪兽”的标黄与解释，不再生成“千年奖励怪兽”独立说明。英文保留 `THERMALVORTEX-MILLENNIUM_REWARD_MONSTER` 及对应绑定。

实现补充：从武藤游戏完整主牌奖励目录中取中文标准名含“千年”的怪兽，排除被封印部件、其他衍生物和额外怪兽；不受本局自定义奖励白名单限制。召唤者每只在场实体于己方回合开始、正常抽牌前各生成1张，升级来源使其本场费用减少1、最低0。密钥入场时每种各生成1张，升级来源生成升级牌。生成牌均随机插入本场抽牌堆。中文召唤者、密钥及召唤者能力使用已有“千年怪兽”解释。实现：[MillenniumExtraDeckCards.cs](../ThermalVortexCode/Cards/MillenniumExtraDeckCards.cs) 中的 MillenniumExtraDeckGeneration；[MillenniumExtraDeckPowers.cs](../ThermalVortexCode/Powers/MillenniumExtraDeckPowers.cs)。

### 大义贼的金币支付

中文卡面直接说明金币总价与获得部件的张数，不再使用“金币换部件”黄字或独立说明；卡面不追加“可重复选择”。英文保留 `THERMALVORTEX-MILLENNIUM_GOLD_EXCHANGE` 及对应绑定。

实现补充：大义贼仅提供当前可负担的档位，另有“不支付金币”；支付成功后按购买数量逐张选择封印部件，每次从5种中自选1种并生成对应基础部件加入手牌，允许重复选择。各档位是本次总价，不累计此前档位。选项复用大义贼展示副本，不是第六张新卡。实现：[MillenniumExtraDeckCards.cs](../ThermalVortexCode/Cards/MillenniumExtraDeckCards.cs)。

### 千年召唤者 · `ExodiaSummonerPower`

普通说明／战斗显示：你的回合开始、抽牌前，每只在场的召唤者从可作为主牌奖励的千年怪兽中随机选1张，加入抽牌堆。升级的召唤者生成的牌本场战斗费用减少1。

在场效果通过单一能力管理多个来源，每只仍在场的召唤者分别生成1张可作为主牌奖励的千年怪兽，并按该来源的升级状态决定是否减费。回合开始触发位于正常抽牌前。能力标题为千年召唤者，并实现 IMillenniumPower；该单一能力计入1种千年能力，同名多只召唤者不叠加能力种数。实现：[MillenniumExtraDeckPowers.cs](../ThermalVortexCode/Powers/MillenniumExtraDeckPowers.cs)。

## 全部关键词

### 千年十字 · `THERMALVORTEX-MILLENNIUM_CROSS_RULE`

显示说明：怪兽位已满时无法使用千年十字，也不能召唤艾克佐迪亚。

实现补充：千年十字需要计数至少5及合法空位；满场时手动、自动打出均拒绝，不支付费用、不生成怪兽、不造成伤害，结算前再次检查怪兽位。艾克佐迪亚基础／升级均有消耗，正常召唤留场，通常送墓改进消耗堆。生成体以本次锁定千年计数N，对所有敌人造成基础N段N点无强化伤害；由一个原生群攻命令执行，敌人数不增加段数，每实体仅结算一次。仍接受原生攻击次数修正，无存活目标时可提前结束。 [Powers/SummonRulesPower.cs:158](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/SummonRulesPower.cs:158>)、[Cards/MillenniumCross.cs:116](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:116>)

绑定位置：[MillenniumCross:28](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:28>)；[PhantomSummoningGodExodia:108](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:108>)。

中英资源键：`card_keywords/THERMALVORTEX-MILLENNIUM_CROSS_RULE.title`、`.description`；英文标题：Millennium Cross。

## 卡牌实现补充

以下逐项记录卡旁关键词说明、当前绑定位置与实现补充；没有独立关键词条目的同名卡牌规则另列为实现说明。

### 怪兽 · `THERMALVORTEX-MONSTER`

显示说明：召唤后进入场上，受怪兽位上限限制。受到攻击时最右侧怪兽优先承受伤害，怪兽生命归零时死亡。

实现补充：主卡怪兽与额外怪兽入场后成为场地牌；基础3位，通常必须有空位。敌人攻击经原有格挡、右到左可承伤怪兽、离场产生的新格挡、玩家生命；0生命一般离场，黑暗决斗等显式保护例外。 [MonsterField/MonsterFieldService.cs:16](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldService.cs:16>)

绑定位置：[AliceInWonderland:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/AliceInWonderland.cs:20>)；[AshBlossom:30](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/AshBlossom.cs:30>)；[AwakenedMillenniumPrimitive:92](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumMonsters.cs:92>)；[BlueEyesWhiteDragon:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/BlueEyesWhiteDragon.cs:21>)；[BrilliantRebootKnight:32](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/BrilliantRebootKnight.cs:32>)；[ChaosPhantom:55](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:55>)；[ChimeratechOverdragon:33](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChimeratechOverdragon.cs:33>)；[ChineseWok:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChineseWok.cs:22>)；[CircuitTalismanBeast:35](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CircuitTalismanBeast.cs:35>)；[CrushCardVirus:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CrushCardVirus.cs:20>)；[CyberDragon:39](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragon.cs:39>)；[CyberDragonCore:26](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonCore.cs:26>)；[CyberDragonHerz:29](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonHerz.cs:29>)；[CyberDragonInfinity:33](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonInfinity.cs:33>)；[CyberLarva:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberLarva.cs:21>)；[CyberNextDragon:36](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberNextDragon.cs:36>)；[CyberWelding:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberWelding.cs:20>)；[CyberloadFusion:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberloadFusion.cs:24>)；[DarkDuel:83](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/NewMainDeckCards.cs:83>)；[DarkMagician:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DarkMagician.cs:21>)；[Detonation:28](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/Detonation.cs:28>)；[DormantMagneticFieldBeast:39](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DormantMagneticFieldBeast.cs:39>)；[ElderEntityNtss:27](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ElderEntityNtss.cs:27>)；[Electrode:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/Electrode.cs:24>)；[ElectromagneticCircle:34](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ElectromagneticCircle.cs:34>)；[ExodiaLeftArm:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExodiaLeftArm.cs:23>)；[ExodiaLeftLeg:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExodiaLeftLeg.cs:22>)；[ExodiaRightArm:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExodiaRightArm.cs:23>)；[ExodiaRightLeg:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExodiaRightLeg.cs:22>)；[ExodiaTorso:27](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExodiaTorso.cs:27>)；[ExternalCombustion:25](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExternalCombustion.cs:25>)；[FullArmorThunderLance:35](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FullArmorThunderLance.cs:35>)；[FurnaceStartup:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FurnaceStartup.cs:18>)；[FusionGate:19](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FusionGate.cs:19>)；[FutureFusion:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FutureFusion.cs:23>)；[GrenMajuDaEiza:25](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/GrenMajuDaEiza.cs:25>)；[HeroArrival:139](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/NewMainDeckCards.cs:139>)；[InternalCombustion:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/InternalCombustion.cs:22>)；[MaxxC:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MaxxC.cs:22>)；[MillenniumCarrier:158](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumMonsters.cs:158>)；[MillenniumContractBook:251](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/NewMainDeckCards.cs:251>)；[MillenniumGravekeeper:131](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumMonsters.cs:131>)；[MillenniumOffering:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumOffering.cs:20>)；[MillenniumPartner:219](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumMonsters.cs:219>)；[MillenniumShield:29](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumMonsters.cs:29>)；[MillenniumSleepingTablet:186](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumMonsters.cs:186>)；[MillenniumTreasureGolem:54](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumMonsters.cs:54>)；[MiracleFusion:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MiracleFusion.cs:22>)；[MonsterReborn:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MonsterReborn.cs:23>)；[MonsterSwap:19](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MonsterSwap.cs:19>)；[MulcharmyFuwalos:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MulcharmyFuwalos.cs:22>)；[MulcharmyMeowls:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MulcharmyMeowls.cs:22>)；[MulcharmyPurulia:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MulcharmyPurulia.cs:22>)；[OverheatedCoil:30](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/NewMainDeckCards.cs:30>)；[PhantomSummoningGodExodia:105](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:105>)；[PotOfAvarice:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/PotOfAvarice.cs:21>)；[PrimalGodFara:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/PrimalGodFara.cs:23>)；[ProtectCore:367](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/NewMainDeckCards.cs:367>)；[Relinquished:34](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/Relinquished.cs:34>)；[SectionPole:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/SectionPole.cs:21>)；[SharedFate:395](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/NewMainDeckCards.cs:395>)；[SpareArmor:105](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/NewMainDeckCards.cs:105>)；[StabilizedMagneticCoil:56](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/NewMainDeckCards.cs:56>)；[TorrentialTribute:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/TorrentialTribute.cs:21>)；[ToyBox:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ToyBox.cs:22>)；[ToyBoxToken:44](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ToyBox.cs:44>)；[Vortex:25](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/Vortex.cs:25>)；[WingedDragonOfRa:935](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:935>)；[WingedDragonOfRaPhoenix:895](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:895>)；[WingedDragonOfRaSphereMode:843](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:843>)；[XyzSummon:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/XyzSummon.cs:21>)。

中英资源键：`card_keywords/THERMALVORTEX-MONSTER.title`、`.description`；英文标题：Monster。

### 融合怪兽 · `THERMALVORTEX-FUSION_MONSTER`

显示说明：用符合条件的融合素材，从额外牌组召唤的怪兽。

实现补充：额外怪兽类型 XyzMonsterCard，普通融合需经授权并满足目标素材约束才可从额外牌组打出；爱丽丝等重用实体的专用入口另有授权。承伤、容量和离场遵循公共怪兽规则。 [Cards/ExtraDeckCard.cs:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExtraDeckCard.cs:20>)

绑定位置：[BrilliantRebootKnight:31](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/BrilliantRebootKnight.cs:31>)；[ChimeratechOverdragon:32](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChimeratechOverdragon.cs:32>)；[CircuitTalismanBeast:32](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CircuitTalismanBeast.cs:32>)；[CyberDragonInfinity:30](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonInfinity.cs:30>)；[CyberEndDragon:34](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberEndDragon.cs:34>)；[CyberloadFusion:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberloadFusion.cs:21>)；[Detonation:27](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/Detonation.cs:27>)；[DivineArsenalFurnaceGod:33](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DivineArsenalFurnaceGod.cs:33>)；[DogmatikaPunishment:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DogmatikaPunishment.cs:23>)；[DormantMagneticFieldBeast:36](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DormantMagneticFieldBeast.cs:36>)；[FullArmorThunderLance:34](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FullArmorThunderLance.cs:34>)；[FusionGate:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FusionGate.cs:20>)；[Relinquished:32](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/Relinquished.cs:32>)；[Vortex:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/Vortex.cs:24>)；[WingedDragonOfRaSphereMode:840](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:840>)。另由“融合召唤”经[融合解释依赖](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ThermalVortexCard.cs:29>)自动附加；[XyzMonsterCard 公共绑定](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExtraDeckCard.cs:25>)沿此依赖链覆盖额外怪兽。

中英资源键：`card_keywords/THERMALVORTEX-FUSION_MONSTER.title`、`.description`；英文标题：Fusion Monster。

### 融合牌 · `THERMALVORTEX-FUSION_CARD`

显示说明：名称含“融合”的牌。

实现补充：费用效果当前以卡名或类型名包含“融合”/“Fusion”判定，不按牌型，也不把资源列举的若干牌当成永久穷举。 [Powers/FusionDestinyPower.cs:81](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/FusionDestinyPower.cs:81>)

绑定位置：[FusionDestiny:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FusionDestiny.cs:18>)。

中英资源键：`card_keywords/THERMALVORTEX-FUSION_CARD.title`、`.description`；英文标题：Fusion Card。

### 混沌幻影

完整复制来源的身份、升级、牌型/目标/效果和有效最大生命，费用保留混沌本体3/2；满生命入场，非复制来源剩余生命。离场完整结算后恢复本体，只清理借来的复制部分；自己实际吞噬获得的成长持续保留到本场战斗结束。展示仅场上提供当前复制怪兽。 [Cards/ChaosPhantomAndRaCards.cs:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:23>)

### 额外牌组 · `THERMALVORTEX-EXTRA_DECK`

显示说明：独立于普通主卡组，存放融合怪兽的卡组，不参与抽牌进程。

实现补充：由核心分开保存永久拥有与本战斗剩余条目，含升级与附魔；不进入主卡组，初始导爆1张，拥有上限10。 [Relics/ThermalVortexCore.cs:56](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:56>)

绑定位置：[CyberloadFusion:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberloadFusion.cs:20>)；[DogmatikaPunishment:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DogmatikaPunishment.cs:22>)；[ExternalCombustion:26](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExternalCombustion.cs:26>)；[FutureFusion:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FutureFusion.cs:22>)；[MiracleFusion:19](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MiracleFusion.cs:19>)；[PotOfExtravagance:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/PotOfExtravagance.cs:24>)；[XyzSummon:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/XyzSummon.cs:20>)。另由“融合召唤”经[融合解释依赖](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ThermalVortexCard.cs:29>)自动附加；[XyzMonsterCard 公共绑定](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExtraDeckCard.cs:25>)沿此依赖链覆盖额外怪兽。

中英资源键：`card_keywords/THERMALVORTEX-EXTRA_DECK.title`、`.description`；英文标题：Extra Deck。

### 融合召唤 · `THERMALVORTEX-XYZ`

显示说明：用符合条件的素材，通过融合牌或融合效果从额外牌组召唤1只融合怪兽。

实现补充：打出融合后即预判素材、腾位后的怪兽位、打出限制及有效目标；无可召唤目标时提示“无法融合召唤。”并结束，不打开素材选择、不支付素材、不提取额外怪兽，进入素材选择前计划失效也提示退出。满场仍允许用合法场上素材腾位，需要选择时显示场上素材提示，可自动确定时不额外弹窗。召唤前还会复查，已知无法召唤时直接停止；同步选择目标后暂存于结算区（Play），等待实际入场。

内部ID仍叫XYZ，玩家用语为融合召唤。先选择目标及合格素材，承诺阶段再支付素材/提取条目；素材支付执行素材效果，本身不冒充怪兽入场。来源与去向见公共玩法说明的融合模式表。 [Relics/ThermalVortexCore.cs:493](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:493>)

绑定位置：[CyberloadFusion:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberloadFusion.cs:23>)；[FusionGate:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FusionGate.cs:22>)；[FutureFusion:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FutureFusion.cs:21>)；[MiracleFusion:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MiracleFusion.cs:21>)；[XyzSummon:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/XyzSummon.cs:24>)。另由“融合素材”经[融合解释依赖](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ThermalVortexCard.cs:29>)自动附加；[XyzMonsterCard 公共绑定](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExtraDeckCard.cs:25>)沿此依赖链覆盖额外怪兽。

中英资源键：`card_keywords/THERMALVORTEX-XYZ.title`、`.description`；英文标题：Fusion Summon。

### 素材 · `THERMALVORTEX-MATERIAL`

显示说明：被献祭用于召唤或发动卡牌效果的怪兽。

实现补充：普通素材与融合素材均触发自身及继承的素材效果；仅先前在场者同时获得离场事件。通常弃牌，额外怪兽或带消耗者改消耗；卡牌指定返抽/消耗等另行处理。单纯破坏、消耗、吞噬不冒充素材使用。 [MonsterField/MonsterFieldService.cs:588](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldService.cs:588>)

绑定位置：[ChineseWok:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChineseWok.cs:21>)；[CyberDragonCore:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonCore.cs:23>)；[CyberNextDragon:37](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberNextDragon.cs:37>)；[ElderEntityNtss:28](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ElderEntityNtss.cs:28>)；[MillenniumOffering:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumOffering.cs:18>)；[PrimalGodFara:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/PrimalGodFara.cs:24>)；[XyzSummon:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/XyzSummon.cs:22>)。另由“融合召唤”经[融合解释依赖](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ThermalVortexCard.cs:29>)自动附加；[XyzMonsterCard 公共绑定](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExtraDeckCard.cs:25>)沿此依赖链覆盖额外怪兽；[吞噬／复制状态的素材提示](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberSeries.cs:265>)在有相应继承效果时补充。

中英资源键：`card_keywords/THERMALVORTEX-MATERIAL.title`、`.description`；英文标题：Material。

### 融合素材 · `THERMALVORTEX-FUSION_MATERIAL`

显示说明：被用于融合召唤的素材。

实现补充：用于融合召唤的素材需满足具体怪兽的数量、指定身份与场上占位条件；素材效果与离场效果按公共事件链处理。 [Relics/ThermalVortexCore.cs:2374](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:2374>)

绑定位置：[CyberloadFusion:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberloadFusion.cs:22>)；[FusionGate:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FusionGate.cs:21>)；[FutureFusion:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/FutureFusion.cs:20>)；[MiracleFusion:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MiracleFusion.cs:20>)；[XyzMonsterCard:25](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExtraDeckCard.cs:25>)；[XyzSummon:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/XyzSummon.cs:23>)。[XyzMonsterCard 公共绑定](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ExtraDeckCard.cs:25>)由所有具体额外怪兽继承。

中英资源键：`card_keywords/THERMALVORTEX-FUSION_MATERIAL.title`、`.description`；英文标题：Fusion Material。

### 无效 · `THERMALVORTEX-NEGATE`

显示说明：取消卡面指定的敌方行动或效果。具体范围和时机以该卡说明为准。

实现补充：不同卡牌拦截的层级和时机不同；无效不统一等于跳过整个回合。本说明只解释卡文用语，不新增或扩大原有拦截效果。基础／升级的灰流丽、无限泡影、神之宣告、神之通告、神之警告均显式绑定此词条。

绑定位置：[AshBlossom:31](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/AshBlossom.cs:31>)；[InfiniteImpermanence:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/InfiniteImpermanence.cs:18>)；[SolemnJudgment:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/SolemnJudgment.cs:21>)；[SolemnStrike:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/SolemnStrike.cs:21>)；[SolemnWarning:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/SolemnWarning.cs:21>)。

中英资源键：`card_keywords/THERMALVORTEX-NEGATE.title`、`.description`；英文标题：Negate。

### 虚弱 · `THERMALVORTEX-WEAK`

本项目使用原生 WeakPower，不重定义虚弱伤害算法；资源描述为攻击伤害降低25%。确切原生钩子行为由当前游戏依赖决定，本次静态源码未执行验证。 [Powers/MillenniumPowers.cs:105](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MillenniumPowers.cs:105>)

绑定位置：[CircuitTalismanBeast:33](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CircuitTalismanBeast.cs:33>)；[DormantMagneticFieldBeast:37](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DormantMagneticFieldBeast.cs:37>)；[MillenniumTreasureGolem:56](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumMonsters.cs:56>)；[TrapHole:25](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/TrapHole.cs:25>)。

中英资源键：`card_keywords/THERMALVORTEX-WEAK.title`、`.description`；英文标题：Weak。

### 消耗触发 · `THERMALVORTEX-CONSUME_TRIGGER`

显示说明：此牌被消耗时触发的效果。

实现补充：响应真实消耗事件，包括带消耗关键词的牌打出后消耗。是否触发素材效果另外取决于 Material/FusionMaterial 离场原因，不能混用。 [Relics/ThermalVortexCore.cs:433](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:433>)

绑定位置：[CircuitTalismanBeast:34](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CircuitTalismanBeast.cs:34>)。

中英资源键：`card_keywords/THERMALVORTEX-CONSUME_TRIGGER.title`、`.description`；英文标题：Exhaust Trigger。

### 休眠磁场 · `THERMALVORTEX-DORMANT_MAGNETIC_FIELD`

显示说明：你无法打出攻击牌。敌方回合结束后，原本要被清除的剩余格挡改为保留一半，并失去1层休眠磁场。

实现补充：拥有时禁止攻击牌，清空格挡改保留floor(格挡/2)，每次保留耗1层。 [Powers/DormantMagneticFieldPower.cs:16](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/DormantMagneticFieldPower.cs:16>)

绑定位置：[DormantMagneticFieldBeast:38](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DormantMagneticFieldBeast.cs:38>)。

中英资源键：`card_keywords/THERMALVORTEX-DORMANT_MAGNETIC_FIELD.title`、`.description`；英文标题：Dormant Magnetic Field。

### 控制 · `THERMALVORTEX-CONTROL`

显示说明：控制时，控制怪兽与被控制敌人始终共享生命。控制怪兽受到伤害后，被控制敌人同步失去等量生命。被控制敌人跳过行动。控制倒计时结束时，控制怪兽死亡；控制怪兽提前离场时解除控制。

实现补充：控制建立时以敌人的当前／最大生命初始化同一生命池，例如双方均为20/50。攻击、直接失血、治疗与上限变化双向同步；纳祭魔受到伤害后，被控敌人同步失去等量生命。格挡和减伤仅在原受作用一侧结算，镜像不触发第二次攻击或治疗。目标跳过行动，控制持续基础3／升级5个敌方回合；倒计时结束时控制怪兽战斗破坏离场，提前离场则直接解除。零血保护、防死与最终死亡统一处理，实际离场、到期、重新绑定和战斗清理均释放绑定。 [Powers/RelinquishedControlPower.cs:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/RelinquishedControlPower.cs:20>)

绑定位置：[Relinquished:33](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/Relinquished.cs:33>)。

中英资源键：`card_keywords/THERMALVORTEX-CONTROL.title`、`.description`；英文标题：Control。

### 电子怪兽 · `THERMALVORTEX-CYBER_MONSTER`

显示说明：名称中含“电子”的系列怪兽。

实现补充：依据ICyberMonster标记与MonsterIdentity解出的混沌复制身份判定。吞噬会继承登记的效果，但不会仅因吞噬而改变宿主的系列身份；不是按中文卡名搜索。 [Cards/CyberSeries.cs:46](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberSeries.cs:46>)

绑定位置：[CyberEndDragon:35](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberEndDragon.cs:35>)；[CyberNextDragon:38](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberNextDragon.cs:38>)；[CyberRepairPlant:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberRepairPlant.cs:20>)；[DarkCyberWorld:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DarkCyberWorld.cs:21>)。

中英资源键：`card_keywords/THERMALVORTEX-CYBER_MONSTER.title`、`.description`；英文标题：Cyber Monster。

### 吞噬 · `THERMALVORTEX-DEVOUR`

千年部件本体效果允许吞噬继承，手牌持有及沉睡石板转化不能继承。吞噬时不立即发放入场奖励；之后宿主成功召唤时逐份执行，两份左手分别控牌15张、两份右手分别抽6张。右足同一事件融合仍只触发一次。

显示说明：消耗目标，将其当前生命、最大生命和攻击力加给吞噬者，并继承其可复制效果。吞噬成长在本场战斗内保留。

实现补充：吸收目标当前/最大生命及攻击贡献，记录可复制效果，再消耗目标；各卡限定来源、选择方式及无目标处理。吞噬与混沌完整复制是不同分派链，继承只覆盖代码登记的可复制效果。吞噬成长按实体独立保存，持续到本场战斗结束。普通离场、消耗、回收与复活不删除成长；返回虚拟额外牌组、未来融合预约、失败返还与再次实体化均携带完整战斗快照，恢复快照不重放吞噬效果。同名实体不合并，复制与重放的新实体各持有独立成长副本。真正变形把成长迁移给新形态，旧卡回收后不再拥有已迁移的成长；新形态实际入场即提交，即使立即离场也算成功，失败只回退本次迁移并保留回调新增成长。成长不写入永久额外牌组，战斗结束、下一战斗或运行重建清理。当前生命仍正常承受伤害，各继承能力仍遵守自己的触发条件。混沌仅保留自身实际吞噬成长，借来部分随复制解除。 [Cards/CyberSeries.cs:307](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberSeries.cs:307>)

绑定位置：[CyberDragonInfinity:31](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonInfinity.cs:31>)；[DarkCyberWorld:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DarkCyberWorld.cs:22>)。

中英资源键：`card_keywords/THERMALVORTEX-DEVOUR.title`、`.description`；英文标题：Devour。

### 理智残存 · `THERMALVORTEX-SANITY`

无其他目标时停手，不自耗；由升级来源或显式EnableSanity等状态开启，各吞噬入口分别判断。 [Powers/DarkCyberWorldPower.cs:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/DarkCyberWorldPower.cs:18>)

绑定位置：[CyberDragonInfinity:32](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonInfinity.cs:32>)；[DarkCyberWorld:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DarkCyberWorld.cs:23>)（均仅在升级状态生成）。

中英资源键：`card_keywords/THERMALVORTEX-SANITY.title`、`.description`；英文标题：Sanity Remains。

### 被封印千年部件 · `THERMALVORTEX-SEALED_MILLENNIUM_PIECE`

显示说明：共左手、右手、左腿、右腿、躯干5种。

实现补充：五种同时在手牌中时赢得战斗，场上及其他牌堆不计，没有替代者。部件均为0费、1生命的怪兽，消耗且不可升级。部件身份按有效卡牌身份识别，完整复制按当前有效身份；不把继承效果当成部件身份。每场战斗最多提交一次集齐胜利，战斗结束停止待执行效果。中英键：`card_keywords/THERMALVORTEX-SEALED_MILLENNIUM_PIECE`。

### 唯一手牌持有 · `THERMALVORTEX-MILLENNIUM_UNIQUE_HAND`

显示说明：在手牌中生效，同名部件不叠加。

实现补充：仅在手牌中生效，同名部件只提供一次。使用千年卡前记录手牌，排除本次打出的那张牌；另有同名副本仍可生效。额外召唤在素材支付后、召唤奖励前记录手牌，已用作素材的部件不参与。结算中新获得的部件从下一张牌起生效。原牌及召唤完成后，依次结算控牌、抽牌、加能量、融合，不限每回合次数。 千年的大义贼、千年召唤者、千年的密钥、千年邪、千年守护者成功召唤时也触发已快照的唯一手牌持有效果，包括正常融合、直接特殊召唤及再次入场。左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。千年召唤者、千年邪与千年守护者均计入千年卡及千年怪兽，也可作为要求千年怪兽的融合素材；它们仍是额外怪兽，不进入千年奖励怪兽生成池。

中英资源键：`card_keywords/THERMALVORTEX-MILLENNIUM_UNIQUE_HAND`。实现细节见[千年部件与循环](gameplay-systems.md#千年部件与循环)。

### 千年控牌 · `THERMALVORTEX-MILLENNIUM_CONTROL`

显示说明：查看抽牌堆顶指定数量的牌，可将其中任意张放入弃牌堆。抽牌堆不足时，将弃牌堆洗混后补至底部。

实现补充：查看抽牌堆顶指定数量的牌，可选择任意张置入弃牌堆，包括0张。抽牌堆不足时，保持原牌序，将弃牌堆洗混补至底部；总数不足则查看全部。每次查看只补洗一次，刚弃掉的牌不在同次查看中再次补洗，未弃掉的牌保持顺序。

中英资源键：`card_keywords/THERMALVORTEX-MILLENNIUM_CONTROL`。实现细节见[千年部件与循环](gameplay-systems.md#千年部件与循环)。

### 千年融合 · `THERMALVORTEX-MILLENNIUM_FUSION`

显示说明：使用手牌和场上的合法怪兽素材，融合召唤1只融合怪兽，可以跳过。

实现补充：可使用手牌和场上的合法怪兽素材，按升级融合的全部条件召唤1只融合怪兽，可以跳过。同一张牌的使用及自身入场只提供一次机会，同事件的手牌、场上与继承来源不叠加。新怪兽入场是新事件，仍有场上右足能力时可以继续融合。

中英资源键：`card_keywords/THERMALVORTEX-MILLENNIUM_FUSION`。实现细节见[千年部件与循环](gameplay-systems.md#千年部件与循环)。

### 千年沉睡石板的离场与部件生成

卡面直接说明：下个己方回合开始时，若自身仍在场，进入弃牌堆，将1张随机被封印千年部件加入手牌。升级后改为自选。

实现补充：下个己方回合开始时，仍在场的原牌进入弃牌堆，并将1个部件加入手牌。普通版从五种部件等概率随机，升级版自选。该离场不是素材支付或消耗；完整复制可以继承此转化，吞噬不能。

不再提供独立关键词解释。实现细节见[千年部件与循环](gameplay-systems.md#千年部件与循环)。

### 千年卡 · `THERMALVORTEX-MILLENNIUM_CARD`

显示说明：名称中含“千年”的系列卡片。

实现补充：以中文标准名称含“千年”维护 IMillenniumCard 等系列标记，界面语言不改变归属。契约书纳入，名称不含“千年”的艾克佐迪亚排除；不提供部件替代。 [Cards/MillenniumSeries.cs:58](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumSeries.cs:58>)

绑定位置：[MillenniumCross:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:24>)；[MillenniumTemple:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumTemple.cs:23>)。

中英资源键：`card_keywords/THERMALVORTEX-MILLENNIUM_CARD.title`、`.description`；英文标题：Millennium Card。

### 千年怪兽 · `THERMALVORTEX-MILLENNIUM_MONSTER`

显示说明：名称中含“千年”的系列怪兽卡片。

实现补充：只计本方场上中文标准名称属于千年系列的怪兽；契约书计入，艾克佐迪亚不计。预览／完整复制按有效复制身份判定，不按界面显示语言判断。 [Cards/MillenniumSeries.cs:52](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumSeries.cs:52>)

绑定位置：[MillenniumCross:25](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:25>)；[MillenniumTemple:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumTemple.cs:24>)。

中英资源键：`card_keywords/THERMALVORTEX-MILLENNIUM_MONSTER.title`、`.description`；英文标题：Millennium Monster。

### 千年能力 · `THERMALVORTEX-MILLENNIUM_POWER`

显示说明：名称中含“千年”的能力。

实现补充：CountMillenniumPowerInstances统计拥有者上千年能力的种数；按中文标准名称维护 IMillenniumPower 标记，千年契约书能力已纳入。同一种能力无论多少层均计1，不取Amount。 [Cards/MillenniumSeries.cs:53](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumSeries.cs:53>)

绑定位置：[MillenniumCross:26](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:26>)；[MillenniumTemple:25](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumTemple.cs:25>)。

中英资源键：`card_keywords/THERMALVORTEX-MILLENNIUM_POWER.title`、`.description`；英文标题：Millennium Power。

### 反弹 · `THERMALVORTEX-REFLECT`

显示说明：使敌人的攻击行动无效，并对攻击者造成等同于该次攻击总伤害的伤害。多段攻击按总伤害计算。

绑定位置：[MirrorForce](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MirrorForce.cs>)。卡面“反弹”标黄，名称显示为“神圣防护罩 反射镜力”。实际触发条件见下方 `MirrorForcePower`。

中英资源键：`card_keywords/THERMALVORTEX-REFLECT.title`、`.description`；英文标题：Reflect。

### 千年计数 · `THERMALVORTEX-MILLENNIUM_COUNT`

显示说明：手牌中的千年卡数＋场上的千年怪兽数＋你拥有的千年能力种数。

中文显示：“查看千年计数”解释在战斗中附加“目前千年计数：{CurrentMillenniumCount}。”，每次打开时读取当前手牌、场上怪兽和能力的实时合计；无拥有者的预览读取当前本地玩家，无战斗状态时不显示占位0。卡面和能力正文不单列当前值；各卡原有数值预览与实际锁值规则保留。英文说明沿用已有当前值动态行。

实现补充：手牌千年卡数＋场上千年怪兽数＋拥有的千年能力种数。卡牌使用前锁定千年计数，本次打出的千年牌离手仍保留其原有计数；本次新入场怪兽、新增能力和抽弃牌不改变已锁定值。融合召唤在确认素材后、支付素材前锁定，素材离手或离场不扣减本次计数；直接特殊召唤或再次入场在入场前锁定，新召唤的本体不计入。每次新使用或召唤重新锁定，使用前展示多少，本次结算便按多少。千年十字把包含自身的本次锁值记录到新生成的艾克佐迪亚。例如第一次为5，旧实体始终记录5；下一次为7，新实体记录7，不回写旧实体。 [Cards/MillenniumSeries.cs:50](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumSeries.cs:50>)

绑定位置：[MillenniumCross:27](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:27>)；[MillenniumTemple:26](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumTemple.cs:26>)；[PhantomSummoningGodExodia:107](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:107>)。

中英资源键：`card_keywords/THERMALVORTEX-MILLENNIUM_COUNT.title`、`.description`；英文标题：Millennium Count。

### 玩具盒

每次使用提供每回合1次成功召唤额度，来源升级随该份额度；失败不耗额度，触发条件为本方怪兽战斗破坏。 [Powers/ToyBoxPower.cs:27](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/ToyBoxPower.cs:27>)

### 爱丽丝梦游仙境

仅自己的回合内触发，每实体每回合的消耗后召唤尝试上限等于当前能力层数；每次先扣额度和3生命，失败计次。排除球形体，其他额外实体不重复支付融合素材；原实体再次召唤保留已用次数，新复制或生成实体拥有独立额度。 [Powers/AliceInWonderlandPower.cs:33](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/AliceInWonderlandPower.cs:33>)

### 不死鸟复活

同一段敌方攻击同时击杀玩家与符合条件的不死鸟时，消费该段复活记录D：D=max(0,(int)修正后攻击段伤害)，非负小数截去小数，取格挡与怪兽吸收之前的量。玩家最大生命加D、当前生命设为D，不死鸟按1点基础生命加自身吞噬生命成长恢复；多段继续逐段处理。此D与核心按敌人累计攻击使用ceil的计数不同。 [Powers/NewCardPowers.cs:477](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:477>)

### 千年的伙伴

完整本角色主卡奖励目录中可奖励的怪兽，生成未升级牌；不是本局自定义奖励池，也不是所有注册怪兽/额外/衍生物。 [Cards/MillenniumSeries.cs:124](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumSeries.cs:124>)

## 全部具体能力

以下记录当前具体能力及隐藏辅助能力。已核对的46项普通说明与战斗显示按需提供语义标黄及相应的图标旁解释，动态分支按当前状态选择；不死鸟复活沿用现有说明。括号中的编辑意见不进入能力界面，具体判断保留在实现补充。`Single` 只表示原生不按 Amount 累层，类内仍可能维护多个来源或待结算队列；`Counter` 的 Amount 也不一定等于次数或倒计时。除单独注明外，生命周期随当前战斗实例，源码没有显式按来源移除就不推断“来源离场即消失”。

### 爱丽丝梦游仙境 · `AliceInWonderlandPower`

普通说明：你的回合内，怪兽牌被消耗时，失去3点生命并召唤它。每张牌每回合每层限1次。

战斗显示：你的回合内，怪兽牌被消耗时，失去3点生命并召唤它。每张牌每回合限{Amount}次。

类型：Buff；叠加方式：Counter。

重复使用叠加能力层数：1层时每张牌每回合最多尝试1次，2层时最多尝试2次；每次仍支付3点生命，失败也计次。先记录次数再支付生命，死亡、满场或目标不可用会阻止召唤。允许额外怪兽并保留原实体素材数、升级和成长，不重新支付融合素材；球形体例外。按拥有者实际回合编号重置已用次数，支持额外回合，不依赖回调顺序；原实体再次召唤保留本回合已用次数；新复制或生成的实体拥有独立额度，效果与成长继承不转移已用次数。 仅处理本方仍在消耗堆且未被移除的怪兽；失败回到原消耗堆，不重发消耗事件。仅在拥有者自己的回合内触发，包含开回合、结束阶段与额外回合；敌方回合及他人的专属额外回合不触发、不扣血、不占次数，也不延期补发。重复使用叠加层数，每实体每回合的尝试上限等于当前层数。先计次再支付3生命；已经开始的尝试即使因死亡、满场或目标失效失败，仍扣血计次。异步过程中复查同一战斗、拥有者回合、卡牌及能力有效性。允许额外怪兽，不重复支付融合素材；球形体除外。原实体保留素材数、升级、吞噬成长及本回合已用次数；复制或新生成实体额度独立。按拥有者实际回合编号惰性重置，不依赖能力回调顺序。

实现：[AliceInWonderlandPower.cs:12](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/AliceInWonderlandPower.cs:12>)。中英文能力键：`powers/THERMALVORTEX-ALICE_IN_WONDERLAND_POWER`；英文标题：Alice in Wonderland。

资源动态参数：`Amount`。

### 灰流丽 · `AshBlossomPower`

普通说明：该敌人下次产生非攻击效果时，使其无效。

战斗显示：该敌人下次产生非攻击效果时，使其无效。

类型：Debuff；叠加方式：Counter。

挂在被标记敌人上。只在敌方回合且该能力仍挂载、Amount>0时触发；首项实际非攻击效果预留1层，该敌人同一敌方回合内其余非攻击效果继续无效。回合结算完才扣该层，未触发保留；显示层数预先减去预留层。来源怪兽离场不清除此敌人能力。补丁按真实来源隔离嵌套行动，并覆盖能力改变、状态/诅咒加入、格挡/恢复/召唤等非攻击命令；玩家施加在敌人上的毒等效果不会因此被当成该敌人的效果。

实现补充：回调中的效果以该回调的真实来源为准，不能被命令中代填的受影响敌人覆盖。临时力量、敏捷与集中正常到期时，其自身移除和精确对应的属性还原均按清理处理，不消耗灰流丽层数；其他效果仍照常判断。

实现：[AshBlossomPower.cs:13](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/AshBlossomPower.cs:13>)。中英文能力键：`powers/THERMALVORTEX-ASH_BLOSSOM_POWER`；英文标题：Ash Blossom。

资源动态参数：无插值参数，使用简洁说明。

### 觉醒的千年原人 · `AwakenedMillenniumPrimitivePower`

普通说明：场上的觉醒的千年原人每回合给予敌人易伤。

战斗显示：场上的觉醒的千年原人每回合给予敌人易伤。

类型：Buff；叠加方式：Single。

IMillenniumPower 单例。本方回合开始求场上实际千年原人 CurrentVulnerable 总和，对全部可攻击敌人施加易伤；没有实际来源则删除。继承/完整复制效果由其独立分派链负责。

实现：[MillenniumPowers.cs:109](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MillenniumPowers.cs:109>)。中英文能力键：`powers/THERMALVORTEX-AWAKENED_MILLENNIUM_PRIMITIVE_POWER`；英文标题：Awakened Millennium Primitive。

资源动态参数：无插值参数，使用简洁说明。

### 电子灯塔 · `CyberBeaconPower`

普通说明：你的回合开始时，在抽牌堆随机位置生成1张电子龙。

战斗显示：你的回合开始时，在抽牌堆随机位置生成1张{GeneratedCardName}。

类型：Buff；叠加方式：Single。

本方回合开始生成1张电子龙至抽牌堆随机位置。BindUpgrade 使用逻辑或：获得过升级来源后后续均生成升级牌；重复来源不增加每回合数量。没有按来源牌离场撤销的监听。

实现：[CyberBeaconPower.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/CyberBeaconPower.cs:9>)。中英文能力键：`powers/THERMALVORTEX-CYBER_BEACON_POWER`；英文标题：Cyber Beacon。

资源动态参数：`GeneratedCardName`。

### 电子龙芯 · `CyberDragonHerzPower`

普通说明：你的下回合开始时，场上的电子龙芯变成电子龙。

战斗显示：你的下回合开始时，场上的电子龙芯变成电子龙。

类型：Buff；叠加方式：Single。

按实体登记待变形龙芯，另存混沌完整复制的来源升级状态。本方下回合开始逐个处理仍在场的实体：先准备目标，再事务式迁移吞噬成长，消耗原实体，免费立即打出电子龙，恢复原怪兽位；不计入普通出牌次数。未成功且原实体仍在场则重新登记，列表空时移除能力。新形态实际入场时提交吞噬成长迁移，旧卡以后回收不再拥有该份成长；入场后立即离场仍算成功。未入场则回退本次迁移，保留回调期间另行获得的成长。

实现：[CyberDragonHerzPower.cs:13](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/CyberDragonHerzPower.cs:13>)。中英文能力键：`powers/THERMALVORTEX-CYBER_DRAGON_HERZ_POWER`；英文标题：Cyber Dragon Herz。

资源动态参数：无插值参数，使用简洁说明。

### 电子龙无限 · `CyberDragonInfinityPower`

普通说明：场上的电子龙无限每回合造成伤害并吞噬怪兽。

战斗显示：场上的电子龙无限每回合造成伤害并吞噬怪兽。

类型：Buff；叠加方式：Single。

每次本方回合开始枚举场上真实电子龙无限、继承其能力的吞噬者及对应混沌完整复制；继承维持效果由公共怪兽分派器每次处理一遍，本监听按继承攻击贡献对随机可攻击敌人造成非强化效果伤害。本方回合末各来源随机吞噬1只其他场上怪兽：先累加成长及持续能力绑定，再消耗被吞者，不立即发放召唤/回合开始奖励。无目标时未升级来源自耗，升级来源停止；来源全无则移除。

实现：[CyberDragonInfinityPower.cs:16](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/CyberDragonInfinityPower.cs:16>)。中英文能力键：`powers/THERMALVORTEX-CYBER_DRAGON_INFINITY_POWER`；英文标题：Cyber Dragon Infinity。

资源动态参数：无插值参数，使用简洁说明。

### 电子革命系统 · `CyberRevolutionSystemPower`

普通说明：本场战斗，你的电子虫费用变为0。

战斗显示：本场战斗，你的电子虫费用变为0。

类型：Buff；叠加方式：Single。

本战斗只识别实际 CyberLarva 类型，将当前正费用减至0；不是所有电子怪兽。应用、进战斗、生成、换堆与出牌前均补做，覆盖手牌/抽牌/弃牌/使用中/消耗堆；重复不叠加。修改为 AddThisCombat，源码没有能力离场时反向恢复费用的逻辑。

实现：[CyberRevolutionSystemPower.cs:12](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/CyberRevolutionSystemPower.cs:12>)。中英文能力键：`powers/THERMALVORTEX-CYBER_REVOLUTION_SYSTEM_POWER`；英文标题：Cyber Revolution System。

资源动态参数：无插值参数，使用简洁说明。

### 电子废品站 · `CyberScrapYardPower`

普通说明：每回合开始时，将1张电子龙加入弃牌堆。

战斗显示：每回合开始时，将1张{GeneratedCardName}加入弃牌堆。

类型：Buff；叠加方式：Single。

本方回合开始生成1张电子龙至弃牌堆顶；升级来源用逻辑或保留，获得过升级版后均生成升级牌。重复不增数量，没有按来源牌离场撤销的监听。

实现：[CyberScrapYardPower.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/CyberScrapYardPower.cs:9>)。中英文能力键：`powers/THERMALVORTEX-CYBER_SCRAP_YARD_POWER`；英文标题：Cyber Scrap Yard。

资源动态参数：`GeneratedCardName`。

### 力量焊接 · `CyberWeldingPower`

普通说明：本回合，每次使用使下一次怪兽的伤害×1.5。触发后，本回合我方造成的所有攻击伤害降低50%。

战斗显示：本回合，下一次怪兽的伤害×{NextMultiplier}。触发后，本回合我方造成的所有攻击伤害降低{AfterReduction}%。

强化已触发时：本回合，我方造成的所有攻击伤害降低{CurrentReduction}%。

类型：Buff；叠加方式：Counter。

本方回合内每次 ArmNextOutput 增加累计使用数n，并武装下一次本方任意怪兽的完整输出，包含普通怪兽与融合怪兽，不再要求电子身份；真实正值敌方输出开始后才消耗待触发标记，单纯打出或召唤而未造成伤害不消费。该次倍率为1.5^n，不再乘此前减伤；同一攻击命令的多段共用倍率；次代龙带继承攻击时，首段与余段两条命令共享本次主攻击倍率，全部完成后才结算减伤。继承素材效果独立结算。输出结束后，将已结算使用数r更新为较大值，后续我方攻击倍率为max(0,1−0.5r)。怪兽的非攻击效果伤害及已接入的直接扣血也适用；直接扣血向下取整。预览只读倍率，不消耗机会；本方回合末删除能力并清空上下文。

实现：[CyberWeldingPower.cs:13](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/CyberWeldingPower.cs:13>)。中英文能力键：`powers/THERMALVORTEX-CYBER_WELDING_POWER`；英文标题：Cyber Welding。

资源动态参数：`AfterReduction`、`CurrentReduction`、`NextMultiplier`。

### 黑暗电子世界 · `DarkCyberWorldPower`

普通说明：每回合结束时，弃牌堆中1张随机电子怪兽吞噬其中另1张随机电子怪兽。

战斗显示：每回合结束时，弃牌堆中1张随机电子怪兽吞噬其中另1张随机电子怪兽。

类型：Buff；叠加方式：Single。

本方回合末从弃牌堆电子怪兽随机选吞噬者及另一目标；检查两者仍在弃牌堆，先吸收后消耗目标。没有目标不触发；仅1只时无理智残存则自耗，EnableSanity 后停手。理智状态只会开启，不被后来的普通来源撤销；重复不增加次数。

实现：[DarkCyberWorldPower.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/DarkCyberWorldPower.cs:11>)。中英文能力键：`powers/THERMALVORTEX-DARK_CYBER_WORLD_POWER`；英文标题：Dark Cyber World。

资源动态参数：无插值参数，使用简洁说明。

### 黑暗决斗 · `DarkDuelPower`

普通说明：本场战斗，你的怪兽生命清零后仍可留场，直到你的下一个回合结束。

战斗显示：本场战斗，你的怪兽生命清零后仍可留场，直到你的下一个回合结束。

类型：Buff；叠加方式：Single。

本场战斗持续保护你的全部场上怪兽，包括之后召唤的怪兽。每只怪兽生命清零后，保留至其下一个己方回合结束；届时仍为0生命才破坏。每个实体独立记录生命由正数降至0时的期限；在己方回合内清零也保留到下一个己方回合结束，不在当前回合结束时结算。恢复至正生命后取消该次期限，再次清零重新计时；仍为0时再次受伤不延长期限。能力不会因某只怪兽结算或回合结束而移除，重复使用不叠加。0生命保护不等于回复生命，主动素材、消耗与明确破坏仍可令其离场；到期破坏不再次获得零血保护。与纳祭魔共享生命沿用同一保护和死亡结算。

实现：[NewCardPowers.cs:307](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:307>)。中英文能力键：`powers/THERMALVORTEX-DARK_DUEL_POWER`；英文标题：Dark Duel。

资源动态参数：无插值参数，使用简洁说明。

### 次元扩张 · `DimensionalExpansionPower`

普通说明：怪兽位增加。

战斗显示：怪兽位增加{Amount}。

类型：Buff；叠加方式：Counter。

作为容量修正器，提供+max(0,Amount)怪兽位，无自动回合到期逻辑。

实现：[NewCardPowers.cs:150](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:150>)。中英文能力键：`powers/THERMALVORTEX-DIMENSIONAL_EXPANSION_POWER`；英文标题：Dimensional Expansion。

资源动态参数：`Amount`。

### 休眠磁场 · `DormantMagneticFieldPower`

普通说明：你不能打出攻击牌。格挡被清除时，改为保留一半。

战斗显示：你不能打出攻击牌。格挡被清除时，改为保留一半。剩余{Amount}层。

类型：Buff；叠加方式：Counter。

阻止拥有者打出任何 Attack 类型牌，手动/自动都经过 ShouldPlay。拥有层数时阻止原生清空格挡，实际保留整数除法 floor(格挡/2)，每次阻止清空扣1层；最后一层移除。

实现：[DormantMagneticFieldPower.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/DormantMagneticFieldPower.cs:11>)。中英文能力键：`powers/THERMALVORTEX-DORMANT_MAGNETIC_FIELD_POWER`；英文标题：Dormant Magnetic Field。

资源动态参数：`Amount`。

### 禁忌的圣杯 · `ForbiddenChalicePower`

普通说明：下一次行动改为获得3点力量。

战斗显示：下一次行动改为获得3点力量。

类型：Debuff；叠加方式：Single。

被标记敌人的下一行动在行动补丁中被替换为获得3力量，然后移除此能力；未提前触发则该敌方侧回合结束移除。替换仍推进敌人行动状态机。

实现：[ForbiddenChalicePower.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/ForbiddenChalicePower.cs:11>)。中英文能力键：`powers/THERMALVORTEX-FORBIDDEN_CHALICE_POWER`；英文标题：Forbidden Chalice。

资源动态参数：无插值参数，使用简洁说明。

### 全装甲雷枪 · `FullArmorThunderLancePower`

普通说明：回合开始时，全装甲雷枪为你提供格挡。

战斗显示：回合开始时，全装甲雷枪为你提供{UpkeepBlock}点格挡。

类型：Buff；叠加方式：Counter。图标数字读取场上本体的维持格挡合计，不直接显示施加层数Amount。

本方回合开始只累加场上实际 FullArmorThunderLance，且其 SummonedTurn 必须小于当前玩家回合；按各实体 CurrentUpkeepBlock 给非强化格挡。没有实际来源时移除此监听；混沌及吞噬继承的对应奖励由它们的效果分派链负责，不能把此类单独视作全部继承来源。励辉士专属本回合锁有效时，原来源和继承来源都不能给予格挡。

实现：[FullArmorThunderLancePower.cs:13](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/FullArmorThunderLancePower.cs:13>)。中英文能力键：`powers/THERMALVORTEX-FULL_ARMOR_THUNDER_LANCE_POWER`；英文标题：Full Armor Thunder Lance。

资源动态参数：`UpkeepBlock`。 `UpkeepBlock`与图标数字显示实际在场本体的维持格挡合计，基础每只6、升级每只8；继承效果继续走原分派链。

### 熔炉启动 · `FurnaceStartupPower`

普通说明：每回合第一次召唤怪兽时，抽1张牌。

战斗显示：每回合第一次召唤怪兽时，抽{Amount}张牌。

类型：Buff；叠加方式：Counter。

本场战斗，每个玩家回合内第一次成功召唤本方怪兽时，按当前能力层数抽牌，每层抽1张。重复使用叠加抽牌层数，每回合仍只在第一次成功召唤时触发。只有实体成功在场才消费触发机会；先置触发标记，避免嵌套召唤重复抽牌。每个玩家回合开始重置机会；手动、卡牌效果和额外召唤均可触发。本回合已触发后再增加层数，不重新获得本回合触发机会。

实现：[FurnaceStartupPower.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/FurnaceStartupPower.cs:11>)。中英文能力键：`powers/THERMALVORTEX-FURNACE_STARTUP_POWER`；英文标题：Furnace Startup。

资源动态参数：`Amount`。

### 融合命运 · `FusionDestinyPower`

普通说明：本场战斗，你的融合牌费用降低1点。

战斗显示：本场战斗，你的融合牌费用降低{Amount}点。

类型：Buff；叠加方式：Counter。

名称/类型名含“融合”或不区分大小写的“Fusion”者获得本战斗每层减1费；本方手牌、抽牌、弃牌、使用中、消耗堆和后来生成/进入战斗/出牌前均刷新。按实体记录已施加折扣，只补正向增量，不在 Amount 降低或能力离场时回滚已写入的战斗费用。XyzSummon 是否匹配依其当前标题“融合”；不是按牌型或固定七卡列表限定。

实现：[FusionDestinyPower.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/FusionDestinyPower.cs:11>)。中英文能力键：`powers/THERMALVORTEX-FUSION_DESTINY_POWER`；英文标题：Fusion Destiny。

资源动态参数：`Amount`。

### 融合之门 · `FusionGatePower`

普通说明：你的回合开始时，失去3点生命，消耗场上或手牌怪兽来融合召唤。召唤失败时，再失去3点生命。

战斗显示：你的回合开始时，失去{LifeLoss}点生命，消耗场上或手牌怪兽来融合召唤。召唤失败时，再失去{LifeLoss}点生命。

类型：Buff；叠加方式：Single。

本方回合开始先直接失去生命L，存活且核心认为可融合时尝试召唤；未成功且仍活着再支付L。L默认3，SetLifeLoss可设为非负值，来源卡按3/升级2写入，最后使用版本生效。重复不叠次数；素材来自场上或手牌且消耗。

实现：[FusionGatePower.cs:10](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/FusionGatePower.cs:10>)。中英文能力键：`powers/THERMALVORTEX-FUSION_GATE_POWER`；英文标题：Fusion Gate。

资源动态参数：`LifeLoss`。

### 融合的事前准备 · `FusionPreparationPower`

普通说明：你的下回合开始时，获得融合和能量。

战斗显示：你的下回合开始时，获得{FusionCount}张融合和{EnergyGain}点能量。

类型：Buff；叠加方式：Counter。

每次 Bind 将非负能量独立入队，本方下回合开始每条生成1张未升级融合到手牌，并取得队列能量总和，然后清空队列并移除。没有登记队列的直接施加用 Amount 作为单条能量后备值。

实现：[NewCardPowers.cs:17](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:17>)。中英文能力键：`powers/THERMALVORTEX-FUSION_PREPARATION_POWER`；英文标题：Fusion Preparation。

资源动态参数：`EnergyGain`、`FusionCount`。

### 未来融合 · `FutureFusionPower`

普通说明：你的下回合开始时，融合召唤已选定的怪兽。

战斗显示：你的下回合开始时，融合召唤已选定的怪兽。

类型：Buff；叠加方式：Counter。

每次 Bind 独立保存序列化额外条目、原索引、至少1的素材数。素材已在准备卡阶段支付；本方下回合开始逐项交给核心召唤，结束清空并删除能力。有无核心都不无限保留队列；失败条目返还等事务由核心处理。

实现：[FutureFusionPower.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/FutureFusionPower.cs:9>)。中英文能力键：`powers/THERMALVORTEX-FUTURE_FUSION_POWER`；英文标题：Future Fusion。

资源动态参数：无插值参数，使用简洁说明。

### 封印的黄金柜 · `GoldSarcophagusReturnPower`

普通说明：封印的牌将在倒计时结束后返回手牌。

战斗显示：{SealedCardName}将在{TurnsRemaining}回合后返回手牌。

类型：Buff；叠加方式：Counter。

每张封印牌各有独立图标，显示具体卡名、升级标记与该牌剩余回合，同名牌也分别显示。每条预约从2个后续玩家回合开始倒计时；回合开始递减，到期仅将仍在消耗堆的原实体加入手牌，随后移除该条图标。原实体已离开消耗堆时不生成替代牌，也不继续等候。

实现：[GoldSarcophagusReturnPower.cs:10](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/GoldSarcophagusReturnPower.cs:10>)。中英文能力键：`powers/THERMALVORTEX-GOLD_SARCOPHAGUS_RETURN_POWER`；英文标题：Gold Sarcophagus。

资源动态参数：`SealedCardName`、`TurnsRemaining`。 `SealedCardName`含实际升级标记；图标数字是该牌的`TurnsRemaining`，每张牌独立显示。

### 无限泡影 · `InfiniteImpermanencePower`

普通说明：本回合下一次行动无效。

战斗显示：本回合下一次行动无效。

类型：Debuff；叠加方式：Single。

敌人下一次完整行动跳过且立即移除此能力；攻击/非攻击都适用。未使用时敌方侧回合结束移除；跳过推进行动状态机。

实现：[InfiniteImpermanencePower.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/InfiniteImpermanencePower.cs:9>)。中英文能力键：`powers/THERMALVORTEX-INFINITE_IMPERMANENCE_POWER`；英文标题：Infinite Impermanence。

资源动态参数：无插值参数，使用简洁说明。

### 限制解除 · `LimiterRemovalPower`

普通说明：本回合召唤无视怪兽位上限。回合结束时，破坏超出上限的怪兽位。

战斗显示：本回合召唤无视怪兽位上限。回合结束时，破坏超出上限的怪兽位。

类型：Buff；叠加方式：Single。

能力存在时忽略召唤容量限制；没有伤害倍增代码。说明中的“破坏超出上限的怪兽位”指回合结束时收回临时超额位置，并从右侧破坏其中的超量怪兽，不永久减少原有容量。使用 BattleDestroyed 离场原因，可触发玩具盒等相应监听。

实现：[LimiterRemovalPower.cs:10](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/LimiterRemovalPower.cs:10>)。中英文能力键：`powers/THERMALVORTEX-LIMITER_REMOVAL_POWER`；英文标题：Limiter Removal。

资源动态参数：无插值参数，使用简洁说明。

### 宏观宇宙 · `MacroCosmosPower`

普通说明：所有牌打出后都会被消耗。

战斗显示：所有牌打出后都会被消耗。

类型：Buff；叠加方式：Single。

为拥有者的战斗牌加 Exhaust 关键词，覆盖手牌/抽牌/弃牌/消耗/使用中及场上，且后来生成、进战斗、换堆、出牌前继续补。源码明确检查 card.Owner.Creature==Owner，因此资源里“所有牌”不能解释为全体玩家。怪兽仍按其入场/离场路由处理；此类没有移除时删除已加关键词的反向逻辑。

实现：[MacroCosmosPower.cs:12](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MacroCosmosPower.cs:12>)。中英文能力键：`powers/THERMALVORTEX-MACRO_COSMOS_POWER`；英文标题：Macro Cosmos。

资源动态参数：无插值参数，使用简洁说明。

### 增殖的G · `MaxxCPower`

普通说明：敌人每行动1次，抽1张牌。

战斗显示：敌人每行动1次，抽{Amount}张牌。

类型：Buff（继承）；叠加方式：Counter（继承）。

EnemyActionKind.Any：实际敌人每段攻击及每项其他行动效果均按活跃来源份数抽牌。共用 EnemyActionDrawPower 的绑定与离场清理；不是简单每整次敌人行动抽一次。

实现：[MaxxCPower.cs:3](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MaxxCPower.cs:3>)。中英文能力键：`powers/THERMALVORTEX-MAXX_C_POWER`；英文标题：Maxx C。

资源动态参数：`Amount`。

### 千年契约书 · `MillenniumContractBookPower`

普通说明：你的回合开始时，获得2点能量，并少抽1张牌。

战斗显示：你的回合开始时，获得{EnergyGain}点能量，并少抽{DrawReduction}张牌。

类型：Buff；叠加方式：Counter。

按实体维护贡献份数和抽牌减少总量，允许吞噬继承多份。常规抽牌减少只累加仍在场来源；本方回合开始先剔除离场记录，再得2×活跃份数能量。来源离场即时移除其贡献层数，全部消失即移除；每份抽牌减少保留来源升级差异1/0。此类带 IMillenniumPower 标记，按千年能力种数贡献1；多层和多来源不额外增加该能力的千年计数。契约书卡在手牌或怪兽在场时另按对应位置各计1。

实现：[NewCardPowers.cs:53](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:53>)。中英文能力键：`powers/THERMALVORTEX-MILLENNIUM_CONTRACT_BOOK_POWER`；英文标题：Millennium Contract Book。

资源动态参数：`DrawReduction`、`EnergyGain`。 使用当前有效来源合计：普通与升级各1份时为4能量、少抽3张。

### 千年的伙伴 · `MillenniumPartnerPower`

普通说明：你的回合开始时，在手牌中生成1张随机怪兽牌。

战斗显示：你的回合开始时，在手牌中生成1张随机怪兽牌。

类型：Buff；叠加方式：Single。

IMillenniumPower 单例。本方回合开始调用 MillenniumSeries.CreateRandomMainDeckMonster，从本角色完整可奖励怪兽目录生成未升级牌至手牌，不取本局手工奖励白名单。创建失败无生成；重复不叠加。

实现：[MillenniumPowers.cs:63](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MillenniumPowers.cs:63>)。中英文能力键：`powers/THERMALVORTEX-MILLENNIUM_PARTNER_POWER`；英文标题：Millennium Partner。

资源动态参数：无插值参数，使用简洁说明。

### 千年沉睡石板 · `MillenniumSleepingTabletPower`

普通说明：下个己方回合开始时，仍在场的石板各自进入弃牌堆，并各将1张被封印千年部件加入手牌。

按实体记录下个己方回合的转化预约。只处理仍在场的来源：将原实体送入弃牌堆，普通从五种部件等概率随机，升级自选，每来源1张。离场失效，再次入场重新预约。完整复制的宿主可以转化；吞噬不继承转化。旧生命光环已移除。

显示采用 `RandomPieceCount` 和 `ChosenPieceCount`，分别统计当前仍在场的两类来源；纯随机、纯自选、混合分别使用默认、`.choose`、`.mixed` 动态分支。解释绑定：被封印千年部件。

实现：[MillenniumPowers.cs](../ThermalVortexCode/Powers/MillenniumPowers.cs)。中英文能力键：`powers/THERMALVORTEX-MILLENNIUM_SLEEPING_TABLET_POWER`。

### 千年的石板 · `MillenniumTabletPower`

旧持续能力已移除。千年的石板现为费用3／2的即时技能，自选一个部件加入手牌，正常弃置；不再随机消耗手牌或每回合生成部件。

### 千年神殿 · `MillenniumTemplePower`

普通说明：你的回合结束时，每点千年计数给予你2点格挡。

战斗显示：你的回合结束时，每点千年计数给予你{BlockPerCount}点格挡。

类型：Buff；叠加方式：Single。

IMillenniumPower 单例。每次回合末触发独立读取当时的计数，不固定为施放神殿卡时的数值。本方回合末按当前千年计数×max(0,BlockPerCount)获得非强化格挡。BlockPerCount默认基础卡值2，来源使用逻辑取较高倍率，升级3；重复不按使用次数叠加。励辉士专属本回合锁有效时格挡为0。

实现：[MillenniumPowers.cs:17](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MillenniumPowers.cs:17>)。中英文能力键：`powers/THERMALVORTEX-MILLENNIUM_TEMPLE_POWER`；英文标题：Millennium Temple。

资源动态参数：`BlockPerCount`。中文附带的“千年计数”解释显示定义公式，并在战斗中附加目前千年计数。

### 千年宝物守护巨像 · `MillenniumTreasureGolemPower`

普通说明：场上的千年宝物守护巨像每回合给予敌人虚弱。

战斗显示：场上的千年宝物守护巨像每回合给予敌人虚弱。

类型：Buff；叠加方式：Single。

IMillenniumPower 单例。本方回合开始求场上实际守护巨像 CurrentWeak 总和，对全部可攻击敌人施加虚弱；没有实际来源则删除。吞噬或混沌继承的同类效果由独立继承/完整复制分派器处理。

实现：[MillenniumPowers.cs:82](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MillenniumPowers.cs:82>)。中英文能力键：`powers/THERMALVORTEX-MILLENNIUM_TREASURE_GOLEM_POWER`；英文标题：Millennium Treasure Guardian Golem。

资源动态参数：无插值参数，使用简洁说明。

### 神圣防护罩 反射镜力 · `MirrorForcePower`

普通说明：本回合所有敌人的每次攻击无效，并对攻击者造成等同伤害。

战斗显示：本回合所有敌人的每次攻击无效，并对攻击者造成等同伤害。

类型：Buff；叠加方式：Single。

挂在玩家上。行动补丁找到有此能力的第一个玩家，本回合每名敌人的每次攻击行动均整次跳过并反射，触发后不移除能力；每次反射值取该行动全部 AttackIntent 对玩家目标的总伤害和（含多段），作为非强化效果伤害打回该敌人。无攻击意图或总值≤0不反射；敌方回合结束移除。多人局并非在此函数内逐个判断该攻击是否瞄准拥有者。

实现：[MirrorForcePower.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MirrorForcePower.cs:9>)。中英文能力键：`powers/THERMALVORTEX-MIRROR_FORCE_POWER`；英文标题：Mirror Force。

资源动态参数：无插值参数，使用简洁说明。

### 多多密友 呼哇罗丝 · `MulcharmyFuwalosPower`

普通说明：敌人每段攻击使你抽1张牌。

战斗显示：敌人每段攻击使你抽{Amount}张牌。

类型：Buff（继承）；叠加方式：Counter（继承）。

EnemyActionKind.Attack：实际敌人每段攻击按活跃来源份数抽牌，多段分别计数。共用来源绑定及离场扣层。

实现：[MulcharmyFuwalosPower.cs:3](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MulcharmyFuwalosPower.cs:3>)。中英文能力键：`powers/THERMALVORTEX-MULCHARMY_FUWALOS_POWER`；英文标题：Mulcharmy Fuwalos。

资源动态参数：`Amount`。

### 多多密友 喵喵露丝 · `MulcharmyMeowlsPower`

普通说明：每有1名敌人被召唤，你抽1张牌。

战斗显示：每有1名敌人被召唤，你抽{Amount}张牌。

类型：Buff（继承）；叠加方式：Counter（继承）。

EnemyActionKind.Summon：每名实际召唤并成功加入战斗的敌人按活跃来源份数抽牌；一次召唤2名敌人即触发2次。共用来源绑定及离场扣层。

实现：[MulcharmyMeowlsPower.cs:3](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MulcharmyMeowlsPower.cs:3>)。中英文能力键：`powers/THERMALVORTEX-MULCHARMY_MEOWLS_POWER`；英文标题：Mulcharmy Meowls。

资源动态参数：`Amount`。

### 多多密友 噗噜利亚 · `MulcharmyPuruliaPower`

普通说明：敌人每次强化、给你施加负面效果或添加状态牌，你抽1张牌。

战斗显示：敌人每次强化、给你施加负面效果或添加状态牌，你抽{Amount}张牌。

类型：Buff（继承）；叠加方式：Counter（继承）。

EnemyActionKind.PowerChange：敌方强化己方、对玩家施加负面、添加状态牌等被捕获的各项实际效果按活跃来源份数抽牌。共用来源绑定及离场扣层。

实现：[MulcharmyPuruliaPower.cs:3](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/MulcharmyPuruliaPower.cs:3>)。中英文能力键：`powers/THERMALVORTEX-MULCHARMY_PURULIA_POWER`；英文标题：Mulcharmy Purulia。

资源动态参数：`Amount`。

### 超载怪兽位 · `OverloadedSummonSlotPower`

普通说明：回合结束时破坏最右侧2个怪兽位。

战斗显示：回合结束时破坏最右侧{SlotsToDestroy}个怪兽位。

类型：Buff；叠加方式：Counter。

每次来源单独登记新增怪兽位数；Amount显示当前累计授予怪兽位数，PendingResolutions显示待结算次数，统一常量SlotsToDestroyAtTurnEnd=2，SlotsToDestroy显示该常量×待结算次数。本方回合末每条先按max(显示容量,怪兽数)确定最右侧至多2个怪兽位并快照其中实体，再扣合法怪兽位、破坏对应怪兽。空位也占右侧怪兽位；依次处理结算中新增来源。净负容量转入地盘沉下，防止临时能力删除后恢复；部分转移被阻止时保留尚未转移的损失。

实现：[NewCardPowers.cs:157](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:157>)。中英文能力键：`powers/THERMALVORTEX-OVERLOADED_SUMMON_SLOT_POWER`；英文标题：Overloaded Monster Slot。

资源动态参数：`SlotsToDestroy`。

### 翼神龙的涅槃 · `RaTransitionPower`

普通说明：你的下回合开始时，场上的拉之翼神龙-球形体变为拉之翼神龙-不死鸟；不死鸟变为拉之翼神龙。

战斗显示：你的下回合开始时，场上的拉之翼神龙-球形体变为拉之翼神龙-不死鸟；不死鸟变为拉之翼神龙。

仅有球形体转不死鸟时：你的下回合开始时，场上的拉之翼神龙-球形体变为拉之翼神龙-不死鸟。

仅有不死鸟转翼神龙时：你的下回合开始时，场上的拉之翼神龙-不死鸟变为拉之翼神龙。

类型：Buff；叠加方式：Single。

按实体及目标形态登记球形体→不死鸟、不死鸟→翼神龙。下次本方回合开始逐条对仍在场来源变形：目标准备成功后消耗原实体，免费正常结算新牌并尽力回到原位，豁免出牌次数。失败且原实体仍在场重新排队；来源离场时删其记录，空队列删除能力。

实现：[NewCardPowers.cs:346](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:346>)。中英文能力键：`powers/THERMALVORTEX-RA_TRANSITION_POWER`；英文标题：The Winged Dragon's Rebirth。

资源动态参数：无插值参数，使用简洁说明；状态分支按实际来源选择。 两类当前有效转换并存时显示两句，仅有一类时只显示对应一句。

### 不死鸟复活 · `PhoenixRevivalPower`

显示说明：你与不死鸟被同一段敌人攻击击杀时将一同复活。

单例展示能力，复用现有不死鸟能力图标。不死鸟或完整复制不死鸟的实际宿主在场时提供标志，最后一个来源离场后移除；复活和变形仍分别由 RaRevivalService 与 RaTransitionPower 处理，不因增加标志重复结算。

中英资源键：`powers/THERMALVORTEX-PHOENIX_REVIVAL_POWER.title`、`.description`、`.smartDescription`。来源：[PhoenixRevivalPower.cs](../ThermalVortexCode/Powers/PhoenixRevivalPower.cs)。

### 纳祭魔控制 · `RelinquishedControlPower`

普通说明：被纳祭魔控制，无法行动，与其共享生命。

战斗显示：被纳祭魔控制，无法行动，与其共享生命。剩余{Amount}回合。

类型：Debuff；叠加方式：Single。

按真实控制者与敌人实例建立一对一共享生命绑定，完整复制纳祭魔也使用实际宿主。敌人当前／最大生命变化与怪兽生命提交双向镜像，并防止回传；纳祭魔受到伤害时，被控敌人同步失去等量生命，反向变化同理，原始伤害／治疗只结算一次。共享期间生命以敌人为准，吞噬生命贡献仍保存在自身成长中，不增加共享池；后续普通形态恢复生效。伤害与破坏队列绑定当次入场的生命对象，异步回调后及实际离场前复查，已离场再入场的实体不受旧队列影响。敌人跳过行动并推进状态机；目标侧回合结束Amount减1，归零时控制者战斗破坏离场并解除。提前解除会清理订阅；死亡、防死和黑暗决斗零血保护统一结算，避免重复离场与残留绑定。

实现：[RelinquishedControlPower.cs:13](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/RelinquishedControlPower.cs:13>)。中英文能力键：`powers/THERMALVORTEX-RELINQUISHED_CONTROL_POWER`；英文标题：Relinquished Control。

资源动态参数：`Amount`。

### 生死与共 · `SharedFatePower`

普通说明：怪兽受到致命战斗伤害时，其余怪兽替它承担。

战斗显示：怪兽受到致命战斗伤害时，其余怪兽替它承担。

类型：Buff；叠加方式：Single。

本身是单例标记。承伤服务在每个怪兽承受攻击时，若承伤队列后方仍有活怪兽，最多扣到1生命，其余伤害继续传递；最后可承伤怪兽不获保1。不是免疫效果破坏或所有扣血。

实现：[NewCardPowers.cs:301](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:301>)。中英文能力键：`powers/THERMALVORTEX-SHARED_FATE_POWER`；英文标题：Shared Fate。

资源动态参数：无插值参数，使用简洁说明。

### 地盘沉下 · `SinkingLandPower`

普通说明：怪兽位减少。

战斗显示：怪兽位减少{Amount}。

类型：Buff；叠加方式：Counter。

作为容量修正器，提供−max(0,Amount)怪兽位；虽减容量但代码 Type=Buff。无自动回合到期逻辑，超载失去的合法怪兽位可转为此持久本战斗能力。

实现：[NewCardPowers.cs:143](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:143>)。中英文能力键：`powers/THERMALVORTEX-SINKING_LAND_POWER`；英文标题：Sinking Land。

资源动态参数：`Amount`。

### 神之宣告 · `SolemnJudgmentPower`

普通说明：本回合行动无效。

战斗显示：本回合行动无效。

类型：Debuff；叠加方式：Single。

敌人本回合每次行动都跳过，触发时不耗掉标记；该侧回合结束移除。完整行动被拦截时仍推进状态机。

实现：[SolemnJudgmentPower.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/SolemnJudgmentPower.cs:9>)。中英文能力键：`powers/THERMALVORTEX-SOLEMN_JUDGMENT_POWER`；英文标题：Solemn Judgment。

资源动态参数：无插值参数，使用简洁说明。

### 神之通告 · `SolemnStrikePower`

普通说明：下一次行动中，除伤害外的效果无效。

战斗显示：下一次行动中，除伤害外的效果无效。

类型：Debuff；叠加方式：Single。

敌人下次行动为纯非攻击时整次跳过并移除；混合攻击行动照常攻击，在作用域内阻断其非伤害效果，结束后移除。具体拦截包含敌方增益、玩家负面、加入状态/诅咒及额外非伤害命令；没有本类独立回合末到期函数。

实现：[SolemnStrikePower.cs:10](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/SolemnStrikePower.cs:10>)。中英文能力键：`powers/THERMALVORTEX-SOLEMN_STRIKE_POWER`；英文标题：Solemn Strike。

资源动态参数：无插值参数，使用简洁说明。

### 神之警告 · `SolemnWarningPower`

普通说明：本回合下一次攻击行动无效。

战斗显示：本回合下一次攻击行动无效。

类型：Debuff；叠加方式：Single。

只有下一个含攻击意图的行动会整次跳过并立即移除；纯非攻击行动不消费。未触发则该侧回合结束移除。

实现：[SolemnWarningPower.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/SolemnWarningPower.cs:9>)。中英文能力键：`powers/THERMALVORTEX-SOLEMN_WARNING_POWER`；英文标题：Solemn Warning。

资源动态参数：无插值参数，使用简洁说明。

### 怪兽场地 · `SummonRulesPower`

普通说明：你最多可控制 {MonsterFieldCapacity} 只场上怪兽。

战斗显示：你最多可控制 {MonsterFieldCapacity} 只场上怪兽。

类型：Buff；叠加方式：Single。

战斗开始由核心施加单例。提供动态 MonsterFieldCapacity；守卫手动/自动怪兽入场容量及额外牌组授权，千年十字手动／自动打出均要求满足计数及合法空位，艾克佐迪亚不再具有满场伤害特许。刷新电子龙固有免费费用；登记升级锁、怪兽入场已结算通知、电子吞噬召唤效果、未升级电子虫入场后消耗，并安排躯干胜利检查；玩家回合开始执行所有场上怪兽的自身继承维持效果，并另行执行混沌借用形态维持效果；两部分持续能力合并登记。

实现：[SummonRulesPower.cs:14](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/SummonRulesPower.cs:14>)。中英文能力键：`powers/THERMALVORTEX-SUMMON_RULES_POWER`；英文标题：Monster Field。

资源动态参数：`MonsterFieldCapacity`。

### 玩具盒 · `ToyBoxPower`

普通说明：你的怪兽被战斗破坏时，召唤1只玩具怪兽。每回合限1次。

战斗显示：你的怪兽被战斗破坏时，召唤1只玩具怪兽。每回合限{Amount}次。

类型：Buff；叠加方式：Counter。

每次来源保存其升级标记到永久顺序表与本回合可用顺序表，故可叠成功次数。仅本方 BattleDestroyed 离场事件触发：按第一份可用来源生成对应升级玩具衍生物，临时预留1容量后立即打出；成功才移走机会，失败清理未提交实体但保留机会。玩家回合开始复制永久顺序重置。

实现：[ToyBoxPower.cs:12](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/ToyBoxPower.cs:12>)。中英文能力键：`powers/THERMALVORTEX-TOY_BOX_POWER`；英文标题：Toy Box。

资源动态参数：`Amount`。

### 出牌计数 · `TurnCardPlayTrackerPower`

普通说明：记录本回合打出的牌数。

战斗显示：记录本回合打出的牌数。

类型：Buff；叠加方式：Single。隐藏辅助能力，不显示图标或播放能力特效。

隐藏且不播放能力特效。仅计拥有者且没有 CardPlayCountExemption 标记的 AfterCardPlayed，玩家回合开始归零；计数在 CardsPlayedThisTurn 而非 Amount。核心每场开战施加，供神系等出牌限制使用。

实现：[TurnCardPlayTrackerPower.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/TurnCardPlayTrackerPower.cs:9>)。中英文能力键：`powers/THERMALVORTEX-TURN_CARD_PLAY_TRACKER_POWER`；英文标题：Card Play Tracker。

资源动态参数：无插值参数，使用简洁说明。

## 敌人行动、无效与抽牌的共享边界

无效入口的优先顺序为控制→禁忌圣杯→神之宣告→无限泡影→神之警告（攻击）→反射镜力（攻击）→神之通告（纯非攻击）；其余行动再进入灰流丽/神之通告的部分拦截作用域。优先命中的能力先处理，后面的能力不在同一次入口强行一并触发；一次性能力按自身规则消费，反射镜力触发后保留至敌方回合结束。见 [Patches/InfiniteImpermanenceMonsterMovePatch.cs:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/InfiniteImpermanenceMonsterMovePatch.cs:18>)。

EnemyActionDrawPower按来源实体保存份数，来源不在场即不计活跃份数，离场事件扣去对应层数；具体四种抽牌能力共享这一实现。已登记来源时，抽牌数=事件发生时仍在场的来源份数合计×本次实际捕获的对应触发数；仅在未登记来源时使用Amount代替来源份数合计。EnemyActionDrawPatch和分类器区分攻击段、能力变化、状态牌加入、召唤等，不能只用意图文字推算所有敌人的效果数。见 [Powers/EnemyActionDrawPower.cs:78](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/EnemyActionDrawPower.cs:78>)、[Patches/EnemyActionDrawPatch.cs:17](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/EnemyActionDrawPatch.cs:17>)、[Patches/EnemyActionClassifier.cs:7](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/EnemyActionClassifier.cs:7>)。

## 动态预览的完整语义

预览上下文保存原卡、目标、已选素材与是否融合选择，使用展示绑定和作用域抑制，不把投影写回牌或战斗状态。无有效拥有者/战斗上下文、解绑或读取失败时保留简短公式。预览数字统一非负向下取整并以绿色显示，缺失项的HasPreview标记保持false。见 [Cards/CardEffectPreviewContext.cs:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewContext.cs:18>)、[Cards/CardEffectPreviewValues.cs:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:24>)。

当前字段：Damage、HitCount、FirstDamage、RemainingHitCount、Block、Weak、Materials、MonsterHp、LifeLoss、Heal、Draw、HandCount、GeneratedPairs、Count、SelectedCount、ReturnCount。原生伤害/格挡预览通过当前引擎 Hook 修正；非强化效果仍按其 ValueProp 区分，不能把所有绿色数字都称为实际最终伤害。

专用预览分支完整目录（分支只改变展示，卡牌本身规则见卡牌现行说明）：

- `GrenMajuDaEiza gren`：[Cards/CardEffectPreviewValues.cs:90](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:90>)。
- `CyberNextDragon next`：[Cards/CardEffectPreviewValues.cs:95](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:95>)。
- `OverheatedCoil overheated`：[Cards/CardEffectPreviewValues.cs:110](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:110>)。
- `StabilizedMagneticCoil stabilized`：[Cards/CardEffectPreviewValues.cs:115](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:115>)。
- `DormantMagneticFieldBeast beast`：[Cards/CardEffectPreviewValues.cs:121](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:121>)。
- `DivineArsenalFurnaceGod furnace`：[Cards/CardEffectPreviewValues.cs:131](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:131>)。
- `ChimeratechOverdragon chimeratech`：[Cards/CardEffectPreviewValues.cs:147](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:147>)。
- `CyberEndDragon end`：[Cards/CardEffectPreviewValues.cs:158](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:158>)。
- `WingedDragonOfRaSphereMode sphere when card.CurrentUpgradeLevel > 0`：[Cards/CardEffectPreviewValues.cs:169](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:169>)。
- `ChineseWok when state.SelectedCards is not null`：[Cards/CardEffectPreviewValues.cs:176](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:176>)。
- `CrushCardVirus when state.SelectedCards is not null`：[Cards/CardEffectPreviewValues.cs:180](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:180>)。
- `Relinquished when Relinquished.IsMinion(target)`：[Cards/CardEffectPreviewValues.cs:185](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:185>)。
- `HopeForEscape`：[Cards/CardEffectPreviewValues.cs:189](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:189>)。
- `SolemnJudgment`：[Cards/CardEffectPreviewValues.cs:194](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:194>)。
- `CardDestruction`：[Cards/CardEffectPreviewValues.cs:198](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:198>)。
- `CyberSymbiosis`：[Cards/CardEffectPreviewValues.cs:203](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:203>)。
- `PhantomSummoningGodExodia god when god.MillenniumCrossCount is { } lockedCount`：[Cards/CardEffectPreviewValues.cs:207](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:207>)。
- `WingedDragonOfRa ra when target?.IsEnemy == true`：[Cards/CardEffectPreviewValues.cs:213](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:213>)。
- `ForbiddenDroplet when state.SelectedCards is not null`：[Cards/CardEffectPreviewValues.cs:217](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:217>)。
- `CyberRepairPlant when card.CurrentUpgradeLevel > 0`：[Cards/CardEffectPreviewValues.cs:221](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:221>)。
- `PotOfAvarice when state.SelectedCards is not null`：[Cards/CardEffectPreviewValues.cs:225](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CardEffectPreviewValues.cs:225>)。

投影特别处理：融合素材手牌先从手牌计数扣除；素材生命用选中实体当前生命；嵌合狂暴龙/电子终结龙生命与伤害按选择素材数展示；千年十字生成体用已锁计数；翼神龙按所指敌人的记录伤害；电子次代龙继承攻击只加在首段，必要时分列首段与剩余段。

### 全部静态卡牌预览绑定

下表逐处列出现行 `CardPreviewExplanation` 调用；默认随来源升级，显式 `false` 的调用保持基础版本。素材条件中的电子龙预览只说明卡牌身份，不改变素材升级限制。

| 调用 | 源码 |
|---|---|
| `CardPreviewExplanation<CyberDragon>(matchSourceUpgrade: false)` | [ChimeratechOverdragon:34](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChimeratechOverdragon.cs:34>) |
| `CardPreviewExplanation<CyberDragon>()` | [CyberBeacon:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberBeacon.cs:21>) |
| `CardPreviewExplanation<CyberLarva>()` | [CyberBeacon:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberBeacon.cs:22>) |
| `CardPreviewExplanation<CyberDragon>()` | [CyberDragonCore:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonCore.cs:24>) |
| `CardPreviewExplanation<CyberLarva>()` | [CyberDragonCore:25](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonCore.cs:25>) |
| `CardPreviewExplanation<CyberDragon>()` | [CyberDragonHerz:30](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonHerz.cs:30>) |
| `CardPreviewExplanation<CyberLarva>()` | [CyberDragonHerz:31](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonHerz.cs:31>) |
| `CardPreviewExplanation<CyberDragon>(matchSourceUpgrade: false)` | [CyberDragonInfinity:34](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberDragonInfinity.cs:34>) |
| `CardPreviewExplanation<CyberDragon>()` | [CyberEmergency:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberEmergency.cs:18>) |
| `CardPreviewExplanation<CyberLarva>()` | [CyberEmergency:19](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberEmergency.cs:19>) |
| `CardPreviewExplanation<CyberLarva>()` | [CyberRepairPlant:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberRepairPlant.cs:21>) |
| `CardPreviewExplanation<CyberLarva>()` | [CyberRevolutionSystem:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberRevolutionSystem.cs:20>) |
| `CardPreviewExplanation<CyberDragon>()` | [CyberScrapYard:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberScrapYard.cs:21>) |
| `CardPreviewExplanation<CyberLarva>()` | [CyberScrapYard:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberScrapYard.cs:22>) |
| `CardPreviewExplanation<CyberDragon>()` | [CyberSymbiosis:19](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberSymbiosis.cs:19>) |
| `CardPreviewExplanation<CyberLarva>()` | [CyberSymbiosis:20](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberSymbiosis.cs:20>) |
| `CardPreviewExplanation<CyberLarva>()` | [DarkCyberWorld:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DarkCyberWorld.cs:24>) |
| `CardPreviewExplanation<XyzSummon>(matchSourceUpgrade: false)` | [FusionPreparation:185](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/NewMainDeckCards.cs:185>) |
| `CardPreviewExplanation<PhantomSummoningGodExodia>()` | [MillenniumCross:29](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:29>) |
| `CardPreviewExplanation<Slag>()` | [OverloadDraw:207](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/NewMainDeckCards.cs:207>) |
| `CardPreviewExplanation<ToyBoxToken>()` | [ToyBox:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ToyBox.cs:21>) |
| `CardPreviewExplanation<WingedDragonOfRa>(matchSourceUpgrade: false)` | [WingedDragonOfRaPhoenix:896](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:896>) |
| `CardPreviewExplanation<XyzSummon>()` | [WingedDragonOfRaSphereMode:841](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:841>) |
| `CardPreviewExplanation<WingedDragonOfRaPhoenix>(matchSourceUpgrade: false)` | [WingedDragonOfRaSphereMode:842](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:842>) |

## 额外牌组、奖励池与吞噬悬浮说明

静态悬浮文本按键分为额外牌组（完整拥有/本战斗剩余）、奖励池（主卡与额外奖励目录）与吞噬状态。前两者的计数由核心/顶部栏运行时注入；吞噬记录由CyberDevourState按原始来源、份数、生命/攻击贡献和可继承效果生成，不将历史截图当作当前状态。见 [Patches/ExtraDeckTopBarPatch.cs:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ExtraDeckTopBarPatch.cs:21>)、[Cards/CyberSeries.cs:265](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberSeries.cs:265>)。

完整静态悬浮资源键（中文用途与动态参数）：

| 键 | 用途/文本字段 |
|---|---|
| `THERMALVORTEX_CARDPILE_EXTRA_DECK.title` | 额外牌组 |
| `THERMALVORTEX_CARDPILE_EXTRA_DECK.description` | 点击查看你拥有的完整额外牌组。 |
| `THERMALVORTEX_CARDPILE_EXTRA_DECK.combatDescription` | 点击查看你拥有的完整额外牌组。本场战斗剩余 {Remaining}/{Owned} 张。 |
| `THERMALVORTEX_CARDPILE_EXTRA_DECK.empty` | 额外牌组为空。 |
| `THERMALVORTEX_CARDPILE_EXTRA_DECK.screenInfo` | 完整拥有的额外牌组。战斗中，卡牌说明会显示该类型当前的剩余/拥有数量。 |
| `THERMALVORTEX_CARDPILE_REWARD_POOL.title` | 奖励池 |
| `THERMALVORTEX_CARDPILE_REWARD_POOL.description` | 点击查看本局后续可能出现的奖励：{Main} 种主卡与 {Extra} 种额外卡。 |
| `THERMALVORTEX_CARDPILE_REWARD_POOL.screenInfo` | 本局后续可能出现的奖励：前 {Main} 种为主卡，后 {Extra} 种为额外卡，均需在冒险中获取。5种固定主卡与初始额外卡“导爆”不会成为奖励，因此不在这里显示。 |
| `THERMALVORTEX_DEVOUR_STATUS.title` | 吞噬状态 |
| `THERMALVORTEX_DEVOUR_STATUS.description` | 已吞噬 {Count} 只怪兽。；总增益：最大生命 +{MaxHp}，继承攻击 +{Attack}。；；{Records} |
| `THERMALVORTEX_DEVOUR_STATUS.record` | [gold]{Monster}{Copies}[/gold]；最大生命 +{MaxHp}；继承攻击 +{Attack}；获得效果：{Effect} |
| `THERMALVORTEX_DEVOUR_STATUS.noEffect` | 无可继承效果 |
| `THERMALVORTEX_DEVOUR_STATUS.cyberLarvaEffect` | 召唤时生成 {Amount} 张电子虫{Upgrade} |
| `THERMALVORTEX_DEVOUR_STATUS.enemyDrawAny` | 敌人每结算1段攻击或1项其他行动效果时抽1张牌 |
| `THERMALVORTEX_DEVOUR_STATUS.enemyDrawAttack` | 敌人每攻击一次时抽1张牌，多段攻击的每段分别触发 |
| `THERMALVORTEX_DEVOUR_STATUS.enemyDrawPowerChange` | 敌人每次强化自身或其他敌人、给你施加负面效果或添加状态牌，你抽1张牌，同一行动中的各项效果分别触发 |
| `THERMALVORTEX_DEVOUR_STATUS.enemyDrawSummon` | 每有1名敌人被召唤，你抽1张牌 |
| `THERMALVORTEX_DEVOUR_STATUS.infinityEffect` | 每回合开始时对随机敌人发动继承攻击，并在回合结束时随机吞噬1只其他场上怪兽 |
| `THERMALVORTEX_DEVOUR_STATUS.upkeepBlock` | 从下个回合开始，每回合开始时获得 {Amount} 点格挡 |
| `THERMALVORTEX_DEVOUR_STATUS.upkeepWeakAll` | 每回合开始时给予所有敌人 {Amount} 层虚弱 |
| `THERMALVORTEX_DEVOUR_STATUS.upkeepVulnerableAll` | 每回合开始时给予所有敌人 {Amount} 层易伤 |
| `THERMALVORTEX_DEVOUR_STATUS.materialDamage` | 作为素材使用时，选择1名敌人，造成 {Amount} 点伤害 |
| `THERMALVORTEX_DEVOUR_STATUS.faraEffect` | 不会成为敌人攻击的目标；作为素材使用后重新特殊召唤 |
| `THERMALVORTEX_DEVOUR_STATUS.sleepingTablet` | 你召唤其他怪兽时，使其当前生命和最大生命增加 {Amount}；仅作用于本方，多份效果累加 |
| `THERMALVORTEX_DEVOUR_STATUS.contractBook` | 每回合开始获得2点能量，常规抽牌少抽 {DrawReduction} 张；每份继承效果分别累加，抽牌减量保留其来源的升级状态 |
| `THERMALVORTEX_DEVOUR_STATUS.badge` | 吞×{Count} |

## 中英文资源覆盖与参数边界

能力的通用 `Amount` 由原生能力基类提供；项目额外声明 `MonsterFieldCapacity`（默认3，实时读取场地容量）与 `PendingResolutions`（默认0，显示超载队列长度）。千年神殿BlockPerCount、融合之门LifeLoss、焊接累计使用数等类内状态不一定直接出现在资源插值中，以上逐项说明仍按代码保留。

| 资源表 | 中文键数 | 英文键数 | 仅中文 | 仅英文 |
|---|---:|---:|---|---|
| `card_keywords` | 54 | 54 | 无 | 无 |
| `powers` | 138 | 138 | 无 | 无 |
| `static_hover_tips` | 26 | 26 | 无 | 无 |

中英键对齐只说明资源存在，不证明两种语言每句叙述完全等价，也不覆盖本次没有运行的原生格式化分支。资源里宏观宇宙“所有牌”等短句应结合本文拥有者过滤理解。StrengthPower、WeakPower、VulnerablePower等为原版关联能力，本文只记录施加位置与层数，不另写未经本机引擎源码确认的完整规则。
