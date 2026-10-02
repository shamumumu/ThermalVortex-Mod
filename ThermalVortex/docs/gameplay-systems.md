# 武藤游戏：现行玩法系统

本页以当前工作区代码为准，记录公共系统与实际限制；逐张卡牌的基础/升级效果见卡牌文档，全部能力见[关键词与能力](keywords-and-powers.md)。内部类名、资源名和存档ID保留 ThermalVortex。本文没有进行构建、游戏运行或联机验收，不复用历史测试结论。

## 角色、初始牌组与战斗初始化

角色定义明确覆盖初始生命70；起始牌组共11张：Strike×4、Defend×4、InternalCombustion×1、SectionPole×1、XyzSummon×1。起始遗物为未完成的千年积木 ThermalVortexCore，永久额外牌组初始仅1张未升级导爆 Detonation。创建新局时重新设置当前/最大生命为StartingHp并重置核心，避免上一局可变实例状态流入新局。角色类未覆盖初始金币、每回合基础能量、基础抽牌等原生字段，不能把模板继承值当成项目自定义常量。 来源：[Character/ThermalVortex.cs:25](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Character/ThermalVortex.cs:25>)、[Relics/ThermalVortexCore.cs:63](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:63>)、[Patches/ThermalVortexRunLifecyclePatch.cs:30](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ThermalVortexRunLifecyclePatch.cs:30>)。

每场开战重新从永久拥有条目复制本场额外牌组，清空待召唤跟踪、该战斗敌方攻击累计、电子虫生成数与吞噬战斗状态，并施加SummonRulesPower和隐藏TurnCardPlayTrackerPower。核心保存拥有额外条目、奖励池快照、外来充能球槽贡献、自伤统计及复活计数；本场剩余额外牌组与按敌人CombatId记录的攻击量是运行时状态。存档缺失奖励池快照与快照无效分开标记，均可回退标准模式，外来机制修复据此区别处理。 来源：[Relics/ThermalVortexCore.cs:420](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:420>)、[Relics/ThermalVortexCore.cs:285](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:285>)、[Relics/ThermalVortexCore.cs:295](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:295>)。

项目提供角色卡池、遗物池、药水池类型及资源接入。Potions目录只有抽象ThermalVortexPotion，没有具体自定义药水实现；不能将模板目录写成已实现药水功能。 来源：[Character/ThermalVortexPotionPool.cs:7](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Character/ThermalVortexPotionPool.cs:7>)、[Potions/ThermalVortexPotion.cs:8](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Potions/ThermalVortexPotion.cs:8>)。

## 怪兽场地、生命与离场

场地接收实际MonsterCard或XyzMonsterCard实体，基础容量3。有效容量=max(0,3＋能力容量修正＋遗物容量修正＋临时预约容量)；显示容量不含临时预约，可见槽数=max(显示容量,当前怪兽数)。召唤同时考虑已落场实体、待提交入场及将被支付的场上素材，防止多个异步召唤占同一空位。嵌套连锁中，前序怪兽完成自身召唤步骤后会先进入待提交状态，直至最外层CardPlay结束才实际落场；后序怪兽检查场上身份或按已有怪兽计数时，读取已落场实体与前序待提交实体按引用身份去重后的并集，不计入尚未完成召唤步骤的自身；相同卡牌ID的多个实体仍分别计数。 来源：[MonsterField/MonsterFieldService.cs:16](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldService.cs:16>)、[MonsterField/MonsterFieldService.cs:88](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldService.cs:88>)、[MonsterField/MonsterFieldService.cs:271](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldService.cs:271>)。

通常召唤需要空位；限制解除只忽略容量，各牌自身的召唤条件仍需满足。玩具盒、变形、复活等入口有各自的临时容量或恢复授权；千年十字必须有合法空位才能打出，满场不支付费用、不生成艾克佐迪亚、不造成伤害；手动、自动及结算阶段使用同一规则。 来源：[MonsterField/MonsterFieldService.cs:68](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldService.cs:68>)、[Powers/SummonRulesPower.cs:158](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/SummonRulesPower.cs:158>)、[Powers/ToyBoxPower.cs:12](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/ToyBoxPower.cs:12>)。

实体场上生命独立于玩家生命。正常入场按怪兽基础最大生命、吞噬贡献及适用入场效果初始化；当前与最大生命可分别变化。普通离场清理场地生命，而有效最大生命调整可为混沌完整复制保留只读快照；不能据此推断普通回收会保留伤口。DarkDuel的被保护实体可0生命留场，其他生命降为0者通常按对应原因离场。 来源：[MonsterField/MonsterFieldHealthService.cs:96](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:96>)、[Powers/NewCardPowers.cs:307](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:307>)。

每段敌人攻击经过原生BeforeDamageReceived钩子后，按“已有格挡→右到左的可承伤怪兽→这些怪兽离场触发的新格挡→原生剩余玩家扣血”结算。Unblockable跳过两次格挡吸收，仍走符合条件的怪兽承伤。每段先快照可承伤实体；本段破坏所召唤的新怪兽不会重新插入同一快照。法拉本体或继承其保护的实体被排除出敌方攻击承伤名单。 来源：[Patches/MonsterFieldDamageGuardPatch.cs:794](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:794>)、[Patches/MonsterFieldDamageGuardPatch.cs:640](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:640>)、[MonsterField/MonsterFieldHealthService.cs:300](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:300>)、[MonsterField/MonsterFieldService.cs:66](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldService.cs:66>)。

这一承伤守卫要求当前处于实际敌人PerformMove上下文，目标是玩家，显式来源须匹配当前敌人，且有场上怪兽或该段复活记录。它不等于“拦截所有生命损失”：卡牌直接LoseHp、自伤以及不满足上下文的伤害不应套用场地顺序。守卫与原生DamageBlockInternal结果对接：怪兽吸收不计为真实格挡，真实破格挡通知不会重复发送，嵌套离场/反击效果有路由抑制。 来源：[Patches/MonsterFieldDamageGuardPatch.cs:70](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:70>)、[Patches/MonsterFieldNativeDamagePatch.cs:71](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/MonsterFieldNativeDamagePatch.cs:71>)。

场地守卫把传入非负decimal伤害截为整数；敌方累计攻击记录另以ceil取整并在格挡与怪兽吸收前记录。共享命运在承伤快照后方仍有活怪兽时让当前怪兽最多降到1生命，剩余继续传递，最后一只不获此保护。限制解除到期或其他强制容量校正从右侧清理；其BattleDestroyed原因可触发玩具盒。 来源：[Patches/MonsterFieldDamageGuardPatch.cs:714](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:714>)、[Patches/MonsterFieldDamageGuardPatch.cs:31](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:31>)、[MonsterField/MonsterFieldHealthService.cs:300](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs:300>)、[Powers/LimiterRemovalPower.cs:10](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/LimiterRemovalPower.cs:10>)。

离场原因完整枚举：

| 原因 | 事件语义 |
|---|---|
| FusionMaterial=0 | 融合素材；属于素材使用 |
| Material=1 | 普通素材；属于素材使用 |
| BattleDestroyed=2 | 战斗破坏标记；也被代码中的部分容量清理/显式破坏入口使用 |
| Exhaust=3 | 消耗离场 |
| FieldClear=4 | 场地清理 |
| Transformation=5 | 变形离场 |

离场先快照实体生命及原因，调用离场监听，再移动至目标堆，离开后清场地状态并调用离场已结算监听。场外手牌/抽牌/弃牌/消耗堆素材只执行自身及继承的素材效果，不伪造场地公共离场事件。素材支付链在每个关键回调后复查拥有者活着、同一战斗且未结束，战斗结束或拥有者死亡时中止后续支付。额外怪兽或带消耗关键词实体的通常弃牌路由会改到消耗堆。 来源：[MonsterField/MonsterFieldEvents.cs:8](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldEvents.cs:8>)、[MonsterField/MonsterFieldEvents.cs:160](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldEvents.cs:160>)、[MonsterField/MonsterFieldService.cs:323](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldService.cs:323>)、[MonsterField/MonsterFieldService.cs:50](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldService.cs:50>)。

手动调整怪兽位默认受锁限制；只有获得手动重排授权时允许MoveMonster/SwapMonsters，卡牌效果专用移动可使用自己的作用域。实体从开始召唤到真实离场期间锁定升级版本；升级/降级请求先记下目标等级，离场时应用。这同时影响复制预览与全体升级入口，不能把正在场上的文字变化直接等同当次怪兽效果已换版。 来源：[MonsterField/MonsterFieldService.cs:650](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldService.cs:650>)、[MonsterField/MonsterFieldUpgradeLockService.cs:14](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/MonsterField/MonsterFieldUpgradeLockService.cs:14>)、[Patches/MonsterFieldUpgradeLockPatch.cs:12](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/MonsterFieldUpgradeLockPatch.cs:12>)。

## 额外牌组与融合事务

怪兽洗回牌组时，按物理卡牌的主／额外归属返还：主牌进入抽牌堆，额外怪兽恢复为本场额外牌组条目。大欲之壶、电子修理工厂、公共弃牌补洗与原生普通洗牌均遵守此规则；主牌混沌复制额外怪兽身份后仍属于主牌。返还保留对应实体的升级、附魔、永久来源标识与本场吞噬成长，不增加永久拥有数量，也不重新触发消耗效果。额外实体实际移除且同一战斗条目恢复后才计为回收成功。回手、正常消耗、失败召唤返还，以及超载融合先结算素材效果再返还的流程沿用各自规则。来源：[CardSelectionHelper.cs](../ThermalVortexCode/Cards/CardSelectionHelper.cs)、[ThermalVortexCore.cs](../ThermalVortexCode/Relics/ThermalVortexCore.cs)。

2026-09-10新增5张千年主题额外怪兽，均为5生命、基础与升级消耗，使用默认卡图。大义贼、召唤者、密钥必须以恰好2只千年怪兽融合；邪与守护者接受至少2只任意怪兽，仍遵守所用融合方式的素材来源规则。邪与守护者在确认素材后、支付素材前锁定千年计数，成功入场后以该锁定值乘以实际融合素材数，分别结算对选定单敌的无强化伤害或给予玩家的无强化格挡；升级时实际消耗后返回本场额外牌组。

大义贼入场选择一次可负担金币总价档位，可不支付：基础10、100、1000……金币对应1、2、3……张自选封印部件入手，升级总价7折。购买n张时，玩家从左手、右手、左腿、右腿、躯干5种中依次选择n次，允许重复选择同一种，生成基础部件。召唤者在场时每只于己方回合开始、正常抽牌前各向抽牌堆随机加入1张千年奖励怪兽，升级来源让生成牌本场费用减少1、最低0。密钥入场向本场抽牌堆随机加入千年奖励怪兽每种各1张，升级时生成牌全部升级。生成范围排除封印部件、衍生物与额外怪兽，取完整主奖励目录，不依本局白名单。五张本体均计入千年卡及千年怪兽，并可作为要求千年怪兽的融合素材。千年召唤者的单一持续能力计入1种千年能力，同名多只召唤者不叠加能力种数。千年邪与千年守护者沿用本次入场前锁值，新召唤的本体不计入；素材离手或离场、后续抽弃牌与新增能力均不改变本次计数。五张仍是额外怪兽，继续排除于千年奖励怪兽生成池。来源：[MillenniumExtraDeckCards.cs](../ThermalVortexCode/Cards/MillenniumExtraDeckCards.cs)、[MillenniumExtraDeckPowers.cs](../ThermalVortexCode/Powers/MillenniumExtraDeckPowers.cs)。

核心分别维护永久拥有和本战斗剩余的额外条目；每条保存类型、非负升级等级和深拷贝附魔。拥有上限10，普通战斗不会因使用而永久删除拥有条目。获得额外怪兽时ShouldAddToDeck只拦截核心拥有者自己的牌，转存其核心；即使上限已满也不把它改塞入主卡组。顶部显示在战斗准备前取拥有数，准备后取本场剩余数。 来源：[Relics/ThermalVortexCore.cs:111](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:111>)、[Relics/ThermalVortexCore.cs:924](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:924>)、[Relics/ThermalVortexCore.cs:189](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:189>)。

电子终结龙要求至少2只电子怪兽，所选素材逐一通过 `CyberSeries.IsCyberMonster` 判定；素材来源仍服从下表对应的融合方式。生命仍为素材数×8，对所有敌人的直接扣血仍为素材数×15/20。来源：[CyberEndDragon.cs](../ThermalVortexCode/Cards/CyberEndDragon.cs)、[CyberSeries.cs](../ThermalVortexCode/Cards/CyberSeries.cs)。

融合素材模式：

| 内部模式 | 素材来源 | 支付去向/时机 |
|---|---|---|
| FieldOnly | 场上 | 弃牌路由；额外或消耗牌依路由消耗 |
| FieldAndHand | 场上＋手牌 | 同上 |
| CyberloadFusion | 场上＋消耗堆 | 主卡回抽牌堆并洗牌；额外素材先结算自身效果，再归还虚拟额外牌组 |
| MiracleFusion | 场上＋弃牌堆 | 消耗 |
| FusionGate | 场上＋手牌 | 消耗 |
| FutureFusion | 抽牌堆 | 现在消耗素材并预留额外条目，下次本方回合开始尝试召唤 |

打出融合后立即预判素材、移走拟用场上素材后的怪兽位、打出限制及有效目标，只保留能实际召唤的候选。无可召唤目标时显示“无法融合召唤。”并结束，不打开素材选择、不支付素材、不提取额外怪兽；进入素材选择前计划失效也提示并退出。满场时按合法场上素材离场后的空位做只读预判，允许能够腾位的融合正常继续；手牌怪兽不能代替场上怪兽腾位。需要玩家选择时，先以 `XYZ_FIELD_MATERIALS` 提示选择必须使用的场上怪兽，再选择其余素材；唯一选项等可自动确定的选择不会额外弹窗。

普通及预约额外召唤在召唤动画前和执行召唤前复查怪兽位、打出条件及有效目标；识别到无法召唤就停止。同步选择目标后将额外怪兽暂存于结算区（Play），等待实际入场后才确认召唤成功。支付素材后的回调若改变召唤条件，已经结算的素材效果不回滚；仍保留内部额外条目回收，避免未成功留场的怪兽丢失。

目标怪兽的数量、指定身份、需要场上素材的数量、最大素材数及占位条件在选择计划中统一验证；球形体只允许FieldOnly/FieldAndHand。先选择目标、必须的场上素材和其余素材，承诺前再验证实体与条目仍有效，随后才支付。素材分组发生变化可能使计划失效；取消选择不应被写成已经完成融合。 来源：[Relics/ThermalVortexCore.cs:2899](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:2899>)、[Relics/ThermalVortexCore.cs:2924](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:2924>)、[Relics/ThermalVortexCore.cs:2981](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:2981>)、[Relics/ThermalVortexCore.cs:2987](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:2987>)。

已提取但未真正留场的额外实体有回收跟踪；失败换堆/消耗时可恢复相应额外条目，入场提交后清理跟踪。FutureFusion提前消耗抽牌堆素材、序列化并预留额外条目；未成功绑定待召唤能力时只在仍属于同一旧战斗或战斗已清理的条件下返还条目，避免向新场战斗重复插入。成功绑定后由FutureFusionPower逐项解析，失败由核心恢复相应条目。素材回调已产生的效果不是任意事务回滚。 来源：[Relics/ThermalVortexCore.cs:452](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:452>)、[Relics/ThermalVortexCore.cs:790](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:790>)。

额外条目可经原生升级、附魔和删卡选择界面使用临时代理牌并写回核心。删卡页同时提供符合原生筛选条件的额外怪兽，并标示额外牌组归属；确认后沿用原生删除流程，按所选实体对应的永久条目移除，取消不删除。删光后的空额外牌组可以保存和读回。火堆确认升级后，将所选实体的升级等级写回对应的永久拥有条目，后续查看、战斗与读档均保留；取消选择不写入。代理在原生玩家选牌命令开始筛选前加入，每条只加入一次；原生选择与联机同步完成后仅清理本次代理，预览克隆不回写。主牌组没有可升级牌时，火堆仍按可升级额外条目启用。附魔入口按原生CanEnchant/predicate过滤，不能保证所有原版或第三方附魔对所有额外怪兽都有实际效果。标准牌堆界面使用临时虚拟牌堆展示额外牌组和奖励目录，不把预览牌加入战斗。 来源：[Patches/ExtraDeckUpgradePatch.cs:24](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ExtraDeckUpgradePatch.cs:24>)、[Patches/ExtraDeckRemovalPatch.cs](../ThermalVortexCode/Patches/ExtraDeckRemovalPatch.cs)、[Patches/ExtraDeckEnchantmentPatch.cs:28](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ExtraDeckEnchantmentPatch.cs:28>)、[Patches/ExtraDeckTopBarPatch.cs:106](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ExtraDeckTopBarPatch.cs:106>)。

战斗奖励只有在玩家匹配、处于CombatRoom、核心未满、存在带同步器的普通CardReward且尚无额外奖励时追加。额外奖励抽取3个不同的允许候选；不足3个则不追加此项；该额外奖励不能重掷。导爆为初始固定额外项，不属于后续额外奖励候选。 来源：[Relics/ThermalVortexCore.cs:858](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:858>)、[RewardPools/RewardPoolDefinition.cs:283](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolDefinition.cs:283>)。

### 控制的共享生命

纳祭魔与被控敌人始终共用当前／最大生命，控制建立时采用敌人原生命，例如双方均为20/50。纳祭魔受到伤害后，被控敌人同步失去等量生命；反向伤害、生命流失、治疗和生命上限变化也双向同步。格挡、减伤及伤害／治疗触发只在原作用侧结算一次。按实际实体一对一绑定，重新控制会解除旧绑定，复制纳祭魔使用其实际宿主。共享期间生命以敌人为准，吞噬生命贡献仍保存在自身成长中，不增加共享池；后续普通形态恢复生效。伤害与破坏队列绑定当次入场的生命对象，异步回调后及实际离场前复查，已离场再入场的实体不受旧队列影响。

纳祭魔联动破坏与其他强制破坏也绑定当次生命对象；每个离场监听结束后，继续下一监听或实际换堆前均复查。旧生命对象失效后，不再用旧事件清理新入场实体的持续能力。

控制期限仍为基础3／升级5个敌方回合，目标跳过行动并推进状态；倒计时结束时纳祭魔战斗破坏离场并解除控制，控制者提前离场时直接解除。绑定共享现有零血保护，防死结果镜像给另一端，保护结束后统一判死；离场、重绑和战斗清理释放生命事件订阅。来源：[RelinquishedControlPower.cs](../ThermalVortexCode/Powers/RelinquishedControlPower.cs)、[MonsterFieldHealthService.cs](../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs)。

敌方行动期间触发的我方响应按响应卡牌或能力的实际来源执行；能力自身清理正常完成。原生效果回调与怪兽场监听分别建立来源边界，避免把抽牌响应或离场解绑误判为敌方效果。原生临时力量、敏捷与集中到期时，移除临时能力及还原其对应属性属于同一次清理，不触发灰流丽；此例外只适用于对应能力、拥有者和精确还原量。

## 电子吞噬、完整复制、千年与神系

MonsterIdentity仅解出混沌当前完整复制的有效卡，并处理火极/雷极动态身份。注册、来源牌组、结果堆路由及混沌宿主费用仍属于物理实体。吞噬独立保存最大/当前生命贡献、攻击贡献、可复制效果列表和来源记录；吸收后由调用入口再消耗被吞者。贡献包含被吞者已有的继承成长，被吞者自己保留其原状态，回收它不会反向扣除吞噬者成长。吞噬不会把所有任意OnPlay代码重新执行，也不会自动改变宿主系列身份。

吞噬成长按实体独立保存，持续到本场战斗结束。普通离场、消耗、回收与复活不删除成长；返回虚拟额外牌组、未来融合预约、失败返还与再次实体化均携带完整战斗快照，恢复快照不重放吞噬效果。同名实体不合并，复制与重放的新实体各持有独立成长副本。真正变形把成长迁移给新形态，旧卡回收后不再拥有已迁移的成长；新形态实际入场即提交，即使立即离场也算成功，失败只回退本次迁移并保留回调新增成长。成长不写入永久额外牌组，战斗结束、下一战斗或运行重建清理。当前生命仍正常承受伤害，各继承能力仍遵守自己的触发条件。 来源：[Cards/MonsterIdentity.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MonsterIdentity.cs:9>)、[Cards/CyberSeries.cs:307](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberSeries.cs:307>)、[Cards/CyberSeries.cs:312](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberSeries.cs:312>)。

`ICyberCopyableEffect`的主要分派类型如下；沉睡石板旧生命光环已移除，新的部件召唤效果与完整复制转化按下方千年规则处理：

| 实现 | 触发与参数 |
|---|---|
| CyberLarvaGenerationEffect | 入场生成至少1张电子虫至弃牌；保存数量与升级标记；维持不生成 |
| EnemyActionDrawInheritedEffect | Any/Attack/PowerChange/Summon四类来源标记，由持久绑定器登记 |
| CyberInfinityDevourEffect | 确保无限监听能力；回合吞噬由该监听枚举来源 |
| CyberUpkeepInheritedEffect | 每本方回合开始给固定非负格挡，或对全部敌人施加固定Weak/Vulnerable |
| CyberMaterialDamageEffect | 仅作为素材时选1敌人造成固定非负效果伤害 |
| CyberFaraPersistenceEffect | 不承受敌方攻击；素材离场已结算后尝试特殊召唤宿主 |
| CyberContractBookEffect | 保存非负常规抽牌减少值；持久绑定器统计能量来源 |
| ChaosPhantomHerzTransitionEffect | 完整复制龙芯专用；Clone保留copiedUpgraded，入场时确保CyberDragonHerzPower并以BindCompleteCopy登记宿主及来源升级状态；维持回调不操作 |
| ChaosPhantomRaTransitionEffect | 完整复制拉形态专用；Clone保留目标形态Phoenix/Ra，入场时确保RaTransitionPower并Bind宿主与目标形态；维持回调不操作 |

龙芯、拉形态与沉睡石板的专用转化不会导出到吞噬链。下列两种既有专用变形效果的GetDevourDisplayText均返回空字符串；完整复制状态向吞噬链导出效果时显式排除它们，因此吞噬复制中的龙芯/拉不会把这些专属变形一并传给吞噬者。来源：[ChaosPhantomHerzTransitionEffect:756](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:756>)、[ChaosPhantomRaTransitionEffect:786](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:786>)、[CreateDevourEffects:641](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:641>)。

上述效果均由Clone保留各份参数；持久同步登记抽牌、契约书、无限等持续能力，不提前发放入场或维持奖励。完整复制通过混沌的专属分派链持有来源完整效果，离场结算完才恢复本体；离场效果结算完成后恢复本体，只清除借来的复制形态、属性及能力；自己实际吞噬获得的生命、攻击、继承效果与来源记录持续保留。再次复制时，当前借用形态与自身成长各计算一次，持续能力统一合并登记。成长悬浮说明按来源标题、升级、生命、攻击和效果文本合并等价记录，不能把显示合并当作效果只结算一次。 来源：[Cards/CyberSeries.cs:363](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberSeries.cs:363>)、[Cards/CyberSeries.cs:788](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/CyberSeries.cs:788>)、[Cards/ChaosPhantomAndRaCards.cs:23](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/ChaosPhantomAndRaCards.cs:23>)。

千年计数=手牌千年卡张数＋场上千年怪兽数＋拥有者上IMillenniumPower能力实例数，最后一项不取Amount。火堆升级、牌组查看等战斗外预览中，拥有者没有PlayerCombatState时，手牌项直接计0，不访问不存在的战斗手牌堆；战斗内仍按实际手牌统计。系列接口按中文标准名称维护，界面语言不影响归属。千年契约书的卡、怪兽及能力均计入；艾克佐迪亚不计入千年系列，也不提供部件替代。千年能力按种数计，每种无论多少层均为1。卡牌使用前锁定千年计数，本次打出的千年牌离手仍保留其原有计数；本次新入场怪兽、新增能力和抽弃牌不改变已锁定值。融合召唤在确认素材后、支付素材前锁定，素材离手或离场不扣减本次计数；直接特殊召唤或再次入场在入场前锁定，新召唤的本体不计入。每次新使用或召唤重新锁定，使用前展示多少，本次结算便按多少。千年十字将包含自身的本次锁值只写入新生成的艾克佐迪亚；旧实体记录不更新。千年神殿在每次回合末触发时读取当时的计数。艾克佐迪亚基础／升级均消耗，正常召唤留场，通常送墓改进消耗堆；爱丽丝再召唤旧实体不会重置已结算的伤害。部件生成改为自选，沉睡石板普通版在五种中等概率随机，允许重复。千年伙伴从武藤游戏完整主卡奖励目录的MonsterCard中生成未升级牌，和本局自定义白名单不同。 来源：[Cards/MillenniumSeries.cs:50](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumSeries.cs:50>)、[Cards/MillenniumSeries.cs:104](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumSeries.cs:104>)、[Cards/MillenniumSeries.cs:125](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumSeries.cs:125>)。

真正出牌次数由隐藏TurnCardPlayTrackerPower按拥有者统计，CardPlayCountExemption标记的变形/复活等立即打出不计。翼神龙相关伤害读取核心按具体敌人CombatId记录的本场完整攻击量，该计数使用ceil。满足条件的不死鸟与玩家被同一攻击段击杀时，复活D另取max(0,(int)修正后该段伤害)，非负小数截去小数，并取格挡/怪兽吸收之前的量：玩家最大生命加D、当前生命设为D，不死鸟按1点基础生命加自身吞噬生命成长恢复；多段继续逐段处理。这条路径没有调用核心旧辅助函数ConsumeRaReviveHp的平方数返回值，因此不能把3²、4²等写成当前复活公式。 来源：[Powers/TurnCardPlayTrackerPower.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/TurnCardPlayTrackerPower.cs:9>)、[Patches/CardPlayCountExemptionPatch.cs:19](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/CardPlayCountExemptionPatch.cs:19>)、[Relics/ThermalVortexCore.cs:408](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:408>)、[Patches/MonsterFieldDamageGuardPatch.cs:714](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/MonsterFieldDamageGuardPatch.cs:714>)、[Powers/NewCardPowers.cs:477](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Powers/NewCardPowers.cs:477>)。

不死鸟在场时显示“不死鸟复活”能力标志，完整复制身份也计入来源，最后一个来源离场后移除。标志复用原图，仅展示共同复活规则，不接管原复活服务。

复活使用本攻击段实际离场的快照；复制不死鸟在离场清除复制状态前保存形态，恢复同一宿主并保留其实时吞噬成长，攻击段结束释放未使用资格。玩家回血和重新入场均按不死鸟卡牌自身来源结算，避免继承敌方无效效果。怪兽生命采用卡牌基础（或保存的复制生命基础）加自身吞噬生命成长，不叠加旧成长快照。

## 能力标志与持续效果

能力界面的普通说明只保留主要触发和效果，战斗说明按当前状态显示实际数值；详细判断保留在本页及关键词与能力文档。原生易伤说明保持原样，出牌计数仍隐藏，不因补齐说明而显示图标。

神圣防护罩 反射镜力覆盖本回合所有敌人的每次攻击行动，首次反射后继续保留，敌方回合结束移除。沿用行动入口的既有优先顺序，取消含攻击意图的整次行动，并把该行动全部攻击意图的总伤害（含多段）作为无强化伤害反弹给攻击者。来源：[MirrorForcePower.cs](../ThermalVortexCode/Powers/MirrorForcePower.cs)、[InfiniteImpermanenceMonsterMovePatch.cs](../ThermalVortexCode/Patches/InfiniteImpermanenceMonsterMovePatch.cs)。

熔炉启动可叠加，每回合第一次成功召唤时按当前层数抽牌；增加层数不重置本回合已经使用的触发机会。两种能力均按拥有者实际回合编号判断，支持额外回合并避免回调顺序造成重置过晚。爱丽丝梦游仙境也可叠加，只在拥有者自己的回合（含开始、结束阶段）触发；敌方回合不复活、不扣血、不占次数、不延期。每张怪兽牌每个自己的回合的尝试上限等于层数，每次支付3生命，已开始的失败尝试计次；保留球形体排除、原实体成长、额外召唤授权与原实体计次、新复制或生成实体独立额度的规则。来源：[FurnaceStartupPower.cs](../ThermalVortexCode/Powers/FurnaceStartupPower.cs)、[AliceInWonderlandPower.cs](../ThermalVortexCode/Powers/AliceInWonderlandPower.cs)。

黑暗决斗是本场持续能力，覆盖当前及之后召唤的全部己方怪兽。每只怪兽由正生命降至0时开始独立期限，保留到其下一个己方回合结束；己方回合中清零也不在当前回合末处理。届时仍为0才破坏；恢复至正生命取消该次期限，再次清零重新计时，零血再受伤不续期。素材、消耗和明确破坏仍可令怪兽离场，保护不会阻止到期清理；能力持续存在并保留纳祭魔共享生命兼容。卡牌基础2费、升级1费。来源：[NewCardPowers.cs](../ThermalVortexCode/Powers/NewCardPowers.cs)、[MonsterFieldHealthService.cs](../ThermalVortexCode/MonsterField/MonsterFieldHealthService.cs)。

黄金柜每张封印牌使用独立能力图标，以实际卡名及升级标记区分，同名牌不合并；各自显示从2开始的剩余回合，第2个后续己方回合开始按原回收条件返回手牌。全装甲雷枪显示实际在场本体的格挡合计，基础每只6、升级每只8；继承奖励沿原分派链处理。灯塔、废品站直接显示当前生成的电子龙或电子龙+；神殿显示当前每计数2或3格挡；融合之门两处支付显示当前3或2生命；沉睡石板按当前待转化来源分别显示随机、自选或混合说明。契约书显示有效来源的能量与少抽牌总量，例如普通与升级各1份时为4能量、少抽1张。涅槃只展示当前有效的形态转换，有两种转换时分别展示两句。来源：[关键词与能力](keywords-and-powers.md)。

## 标准奖励池、自定义目录与预设

标准模式沿原生奖励路径；自定义模式保存70–90个主卡目录ID与10个额外目录ID，多人共享选卡固定为每人80个主卡ID，实际起始牌组仍为11张。主卡必含5个固定初始种类；额外必含导爆，其余9种自由选择。自选主卡Common/Uncommon/Rare各至少3种。目录要求非空、无重复、ID当前可解析、位于正确候选范围并含全部固定项。奖励池定义写入schemaVersion=3、rulesVersion=2，版本1/2的旧60张运行快照继续按历史规则读取；旧预设保留编辑，新局需补足当前要求。 来源：[张数规则](../ThermalVortexCode/RewardPools/RewardPoolSizePolicy.cs)、[奖励池定义](../ThermalVortexCode/RewardPools/RewardPoolDefinition.cs)。

主卡可选来源固定顺序为武藤游戏、战士Ironclad、静默Silent、故障Defect、亡灵契约师Necrobinder、储君Regent；各来源内部按Common/Uncommon/Rare及完整ModelId稳定排列。武藤游戏奖励候选的代码期望数83，全部额外候选期望数17；外来角色目录来自当前已加载原生模型，不在文档中固定其总数。生成专用、封印部件、古代限定等并不因此变成普通奖励。 来源：[RewardPools/RewardPoolCatalog.cs:21](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:21>)、[RewardPools/RewardPoolCatalog.cs:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolCatalog.cs:18>)、[Patches/ThermalVortexRewardFilters.cs:17](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ThermalVortexRewardFilters.cs:17>)。

发牌策略按真实来源分类：

| 策略 | 自定义白名单 |
|---|---|
| GenericRandomReward | 限制；未知随机发牌默认归此类 |
| Transform | 限制 |
| FixedSingle / FixedEventPool | 保留固定牌/固定事件候选 |
| Generated | 保留效果生成 |
| CurseStatus | 保留诅咒/状态 |
| CopyReturn | 保留复制与回收 |
| Ancient | 保留先古来源 |

标准模式优先直通，不枚举自定义候选或改变随机源。自定义候选按原始候选与白名单交集→白名单中仍满足原过滤者→白名单后备逐层处理，并保留多人约束、isAllowed等限制。无色专属与额外专属来源不应注入主卡目录，普通无色候选另有保留路径；结果不应简写成“全游戏所有给牌都被自选奖励池替换”。解析器本身不取随机数。 来源：[RewardPools/RewardGrantPolicy.cs:13](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardGrantPolicy.cs:13>)、[RewardPools/RewardPoolCandidateResolver.cs:30](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolCandidateResolver.cs:30>)。

事件候选黑名单耗尽时兼容入口可放宽去重以允许重复候选；商店不走这一去重放宽分支。异常后备只在兼容代码认可的范围重建候选，不代表任意异常静默成功。ArcaneScroll在本角色上从当前允许主卡池的Rare候选生成，无升级随机；虽然可按原生Cards值生成多个候选，当前实现只取第1个加入牌组，未提供另一个选择界面。 来源：[Patches/CardFactoryEventCompatibilityPatch.cs:30](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/CardFactoryEventCompatibilityPatch.cs:30>)、[Patches/ArcaneScrollCompatibilityPatch.cs:37](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ArcaneScrollCompatibilityPatch.cs:37>)。

预设库允许创建、保存、复制、重命名、删除与选择；每项有稳定GUID、名称、主/额外ID列表、创建/更新时间，库保存ActivePresetId。草稿可以尚未满足60/10等玩法要求而保存或选中；只有验证通过的预设才暂存为可开局配置，无效项会清除待开局配置。删除当前活动预设切回标准；排序按更新时刻倒序及ID稳定排序。库结构错误、重复GUID、无效时间或不存在的活动ID与“可编辑但尚未完成的草稿”分开处理。 来源：[RewardPools/RewardPoolPresetModels.cs:5](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolPresetModels.cs:5>)、[RewardPools/RewardPoolPresetService.cs:251](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolPresetService.cs:251>)、[RewardPools/RewardPoolPresetService.cs:452](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolPresetService.cs:452>)、[RewardPools/RewardPoolPresetService.cs:343](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolPresetService.cs:343>)。

运行配置保存在Godot用户数据目录下mod_configs/ThermalVortex.reward_pool_presets.json及其.bak；兼容的上次单一构筑文件为mod_configs/ThermalVortex.reward_pool.json及其.bak。写入使用临时文件、刷新、替换与读回验证；主文件无效会尝试有效备份并按可用条件恢复。预设库是权威，成功预设事务后旧单构筑镜像保存失败不会回滚已成功的库/待开局状态。这些是实际用户配置，不能当作文档清理产物删除。 来源：[RewardPools/RewardPoolPresetStore.cs:6](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolPresetStore.cs:6>)、[RewardPools/RewardPoolLastBuildStore.cs:6](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolLastBuildStore.cs:6>)、[RewardPools/RewardPoolPresetService.cs:343](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolPresetService.cs:343>)。

预设库主文件与备份均无法读取时，管理器显示错误及“本次使用标准牌池”。只有明确选择后才在本进程允许标准开局，后续页面及开局按当前上下文重建待开局配置；重启需重新选择。库读取错误保留，错误期间禁止预设写入，不覆盖原文件和备份。

待开局配置绑定当前启动上下文，只消费一次；新局在上下文可确定后应用，读档从遗物快照恢复而不借用未开始新局的选择。创建/读档/清理会重置相关临时状态与启动协调器。预设编辑器可检索、按来源/稀有度/类型筛选排序、查看计数与不足项、查看单卡详情，并分别编辑主卡和额外页；只有可用配置才进入相应启动路径。 来源：[RewardPools/RewardPoolSetupService.cs:7](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/RewardPools/RewardPoolSetupService.cs:7>)、[Patches/RewardPoolLaunchPatch.cs:26](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/RewardPoolLaunchPatch.cs:26>)、[Patches/RewardPoolBuilderScreen.cs:2064](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/RewardPoolBuilderScreen.cs:2064>)、[Patches/ThermalVortexRunLifecyclePatch.cs:54](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ThermalVortexRunLifecyclePatch.cs:54>)。

自定义目录包含Defect牌时，持久能力层把基础充能球槽补到至少当前Defect原生基础值，记录本系统增加的差额；后续移除只扣自己的贡献，不扣其他遗物/效果所给槽。旧有效快照可接管可证明的贡献；缺失快照不启用猜测，旧无效快照只按代码的严格特例修复。包含Regent牌时保证星星计数UI可见，不因此免费获得星星。跨角色牌仍执行各自原生机制，不能把这两项兼容概括为所有其他角色/第三方机制已全面支持。 来源：[Patches/RewardPoolForeignMechanicsPatch.cs:41](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/RewardPoolForeignMechanicsPatch.cs:41>)。

### 预组、Genesis 与三选一

构筑方式分为自由构筑、Genesis、单人三选一与多人共享选卡；最后都生成Manual奖励白名单，起始实际牌组仍为11张。自由构筑与Genesis主池70–90张，单人三选一按所选包得到70–90张，多人共享选卡每人恰好80张；以上均包含5张固定主牌。个人预设可保存自由构筑或Genesis；选卡会话仅供一次新局，单人完成结果可另存为自由构筑预设。定义版本与规则版本分开保存，旧预设默认为自由构筑。

内置预组将原版五角色的小流派整理成16个双轴包，每包19–21张，另各设通用支援包。每包保存两个轴、桥接牌与中英联动说明，例如战士消耗循环与格挡转伤、静默小刀与弃牌奇巧、故障状态牌引擎与混合球、亡灵契约师灵魂循环与奥斯提、储君星星与置顶循环。成员使用显式ModelId；共享卡可以属于多个包，个人加入后只占一个名额。包内不含生成专用牌，召唤与铸造使用对应启动牌和原生机制。通用支援包覆盖其余合法奖励牌，不参与随机流派包抽选。Genesis可独立按角色浏览和导入预组，查看成员、已选、新增张数与积分；超名额或预算时整包拒绝，不截取部分成员。

Genesis使用240分共同预算，65–85个自选主牌与9个自选额外牌计分，固定5种主牌和导爆为0分。原版基价为`1 + floor(8 × picked / offered + 0.5)`，有依据的人工偏移限±1，最终为1—10分；武藤游戏牌逐牌使用注明参照与配套门槛的设计估价。统计快照来自[STS Tracker](https://ststracker.app/cards?asc=10)的单人A10、v0.107.1口径，只匹配本地合法原版候选，不把其他Mod或无色条目直接混入同一标尺。pick率受上传样本、出现时机和既有构筑影响，不等于独立强度；240分是首版设计预算。完整积分、来源和预组成员随程序集内嵌，运行时不联网。新增选择不得超分；已有超分草稿可保存和删改，开局前重新核算。已经开始的局保存当时的分值、总分、预算与版本，读档不按新价格重算。

单人三选一不使用积分：先进行三轮流派包三选一，每轮尽可能展示不同角色，允许跨轮再次选同角色。新会话的主题包保留原19–21张目标，固定双轴核心与桥接牌；每包仅1–3个配件位从该角色的有限合法奖励牌名单中抽取，排除核心与重复牌，不改原始预组成员。候选实际成员与已选成员一同保存，旧会话继续原来的固定成员；Genesis整包导入仍使用原始预组。候选保留后续可完成性，前三包去重总量必须为45–65张；随后默认进行四轮泛用包选择，每轮从三个不同的5张随机包中选一个，共加入20张；各轮排除已选成员并保留后续完成稀有度要求的空间。泛用轮数可在创建会话时调整为20的正整数约数，每轮张数随总量20平均分配并随会话保存。已有旧版会话保留原来的轮数、候选与流程：schema2仍为一次20张，schema3仍为原来保存的泛用轮数，均不重抽。加上5张固定主牌后为70–90张，再从额外候选三选一补足9种。泛用包使用独立的明确白名单，不直接把所有支援包余牌视为泛用。独立随机状态、完整候选包和当前选择随操作保存；关闭界面或重启继续同一轮，不提供单轮刷新、撤销或完成后的自由换牌，可明确放弃整次重新开始。

三选一会话保存在Godot用户数据目录的`mod_configs/ThermalVortex.reward_pool_draft.json`及其备份；保存失败不推进可见选择，损坏文件保留并报告。完成会话通过当前角色页上下文暂存，成功应用到新局后记录消费；消费后的选中模式要求下一局重新构筑。继续已有局只使用核心遗物中的最终奖励池。相关实现：[构筑规则](../ThermalVortexCode/RewardPools/RewardPoolConstruction.cs)、[数据目录](../ThermalVortexCode/RewardPools/RewardPoolConstructionCatalog.cs)、[三选一会话](../ThermalVortexCode/RewardPools/RewardPoolDraftService.cs)、[界面](../ThermalVortexCode/Patches/RewardPoolBuilderConstruction.cs)。本次新增构筑流程不改变已有跨职业战斗结算；实际画面与交互需要单独获授权的进游戏测试确认。

多人共享选卡用于2–4人全员武藤游戏的标准新局。桌面提供`(人数+1)×3`个不同流派包，每人同时选择三个。不同玩家选择同包，或选包成员存在相同ID，均为冲突；同一玩家包内重合成员去重。候选生成先寻找每人三个包、跨玩家成员无重合且可补满的分配见证，多出的三个包可以无人选择。确认前公开全部意向，不按请求先后决定赢家。

选包全员确认后，从本桌全部候选包之外的合法主牌中生成公共库存，包括原版与武藤游戏牌。库存以`(人数+1)×20`份为基础，不足则补到所有人能完成，并保留稀有度分配。优先不同卡种，适用卡种不足时增加副本；两份同名牌允许两名玩家各选一次，同一玩家仍只计一种。包内共享牌不因单卡副本机制解除冲突。每人最终75种自选主牌＋5种固定主牌，额外池各自选9种＋导爆，固定主牌与额外牌允许跨玩家重复。

多人主机保存权威意向和库存；修改递增内容版本并撤销全员确认，确认自身不改变内容版本。全员合法且确认同一版本、客户端接收最终快照后开放原生准备。临时断线保留选择并暂停开局，原玩家重连同步；参与名单或角色改变时取消本次构筑，主机退出不迁移主持权。最终按NetId分别应用各人奖励池并消费会话，继续存档从各人核心遗物恢复，不重新抽选。相关实现：[共享选卡领域模型](../ThermalVortexCode/RewardPools/RewardPoolSharedDraftSession.cs)。

## 遗物与先古选项

| 遗物 | 当前效果与取得路径 | 来源 |
|---|---|---|
| 未完成的千年积木 ThermalVortexCore | 起始遗物；管理额外牌组、召唤、战斗统计与奖励池状态；建筑师结尾不再提供或自动执行升级 | [Relics/ThermalVortexCore.cs:259](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/ThermalVortexCore.cs:259>) |
| 千年积木 AncientThermalVortexCore | Ancient稀有度，继承核心全部状态；本方常规抽牌前将弃牌堆中DeckVersion仍是XyzSummon的实体置于抽牌顶；不包含所有名字含融合的牌 | [Relics/AncientThermalVortexCore.cs:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/AncientThermalVortexCore.cs:18>) |
| 叠层线圈 LayeredCoil | Uncommon；每本方回合首次捕获拥有者自己IsAutoPlay且实际MonsterCard的出牌后，为遗物拥有者抽1牌；XyzMonsterCard不符合该具体类型判断 | [Relics/LayeredCoil.cs:15](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Relics/LayeredCoil.cs:15>) |
| 古老牙齿 ArchaicTooth | 欧洛巴斯的原生先古选项。对武藤游戏，永久牌组没有电磁圈且有可移除的α号电圈、α号磁圈各至少1张时提供合成；每种选升级最高的1张，平级按永久牌组顺序。取得时复查条件，各移除1张素材后加入1张电磁圈；条件失效不移除素材、不生成结果。每张已升级素材贡献1级，合成基础等级为0／1／2级、基础费用为3／2／1；原生入牌效果继续正常结算，可能继续改变结果。选项预览两类素材及按素材贡献等级生成的电磁圈，并沿用原生先古选项结束流程 | [Patches/ArchaicToothCompatibilityPatch.cs:84](<../ThermalVortexCode/Patches/ArchaicToothCompatibilityPatch.cs:84>)、[取得流程:120](<../ThermalVortexCode/Patches/ArchaicToothCompatibilityPatch.cs:120>)、[预览:161](<../ThermalVortexCode/Patches/ArchaicToothCompatibilityPatch.cs:161>) |

建筑师结尾只保留四组定稿对话及每句一个原生推进按钮。“完成千年积木”“合成电磁圈”的结尾入口与专用奖励回调已移除：首句和后续刷新均不追加奖励选项，推进对白也不会自动升级或替换遗物、移除素材牌或发放电磁圈。电磁圈卡牌定义、数值和普通奖励排除规则保留。DustyTome对本角色把AncientCard指定为PrimalGodFara的逻辑保留。来源：[Patches/ThermalVortexAncientOptionsPatch.cs](../ThermalVortexCode/Patches/ThermalVortexAncientOptionsPatch.cs)、[Cards/ElectromagneticCircle.cs](../ThermalVortexCode/Cards/ElectromagneticCircle.cs)。

## 建筑师结尾对话与首次回应

武藤游戏有四组中英文结尾对话，资源编号为 `0–3`，全部设置 `visit=0` 并以 `r` 标记可重复：从首次通关开始，由原生随机逻辑每次在四组中等概率选择，不按累计胜场依次解锁。各组 `-attack` 均配置为 `Architect`，保留原生建筑师收尾攻击。四组的角色回复均由暗游戏说出，包括第三组；对外角色名称仍为“武藤游戏”。

| 组（资源编号） | 说话者 | 台词 | 本句后的按钮 |
|---|---|---|---|
| 1（0） | 建筑师 | 登上塔顶，就以为自己是王了？ | 回应 |
| 1（0） | 暗游戏 | 王的位置，不由你册封。 | 继续 |
| 1（0） | 建筑师 | 我只承认力量。 | 回应 |
| 1（0） | 暗游戏 | 那就看清楚。我的回合。 | 原生结束按钮 |
| 2（1） | 建筑师 | 积木里的那个，你还要躲多久？ | 另一个我…… |
| 2（1） | 暗游戏 | 让你久等了。 | 原生结束按钮 |
| 3（2） | 建筑师 | 世界之间，本该保持沉默。 | 回应 |
| 3（2） | 暗游戏 | 可你还是回应了我。 | 继续 |
| 3（2） | 建筑师 | 你把这当作许可？ | 回应 |
| 3（2） | 暗游戏 | 我把它视作问候。 | 原生结束按钮 |
| 4（3） | 建筑师 | 这块积木，并非出自你手。 | 回应 |
| 4（3） | 暗游戏 | 它在等待最后的位置。 | 继续 |
| 4（3） | 建筑师 | 已经千年了。 | 回应 |
| 4（3） | 暗游戏 | 可黄金国的声音，还没有停。 | 原生结束按钮 |

末句不配置 `.next`，使用原生结束按钮。资源中的 `.ancient` 对应建筑师，`.char` 对应暗游戏回复。来源：[中文资源](../ThermalVortex/localization/zhs/ancients.json)、[英文资源](../ThermalVortex/localization/eng/ancients.json)。

本地玩家使用武藤游戏通关时，结尾对白与建筑师原生收尾结束后，直接进入显示时间、层数、分数与徽章的本局总结页，跳过“胜利……？”建筑师伤害统计页及其“继续”按钮。跳转使用原生总结入口，保留通关记录、进阶解锁、徽章与发现奖励结算以及既有胜利台词；按通关记录判断，不以收尾后的剩余生命判断胜负。败北、放弃和本地使用其他角色时沿用原生页面流程。来源：[Patches/ThermalVortexVictorySummaryPatch.cs](../ThermalVortexCode/Patches/ThermalVortexVictorySummaryPatch.cs)、[Patches/ThermalVortexRunSummaryPatch.cs](../ThermalVortexCode/Patches/ThermalVortexRunSummaryPatch.cs)。

进入建筑师场景时保留原立绘，先显示建筑师第一句，不自动变身。首句仅显示“回应”或“另一个我……”一个按钮；首次点击后沿用原生按钮禁用流程，收起旧对白气泡，再在黑幕覆盖后换为暗游戏立绘。动画依次为全黑 0.25 秒、眼光淡入 0.25 秒、眼光脉动 0.30 秒、暗游戏显形 0.50 秒；完整等待 1.30 秒动画结束，才调用原生推进并显示暗游戏的第一句回复。同一场景后续每句保留一个“继续”“回应”或原生结束按钮，正常推进且不重复变身，暗游戏立绘保留至本场景结束。

`ArchitectFirstResponsePatch` 仅包装武藤游戏首句选项的原回调，保留按钮文本、提示与不计入选择历史的设置；入口为 `ArchitectFirstResponse.AwaitRevealThenAdvance` → 内部异步接口 `ArchitectYamiRevealVfx.PlayAsync`。重复点击共用一次变身和一次原回调推进，避免跳过台词。结尾不再生成或刷新两个额外奖励选项，也不会把原奖励效果隐式并入首回应。不新增公共 API、存档字段或角色身份。

变身复用现有 `ending_visual_yami.png` 与 `ending_visual_yami_eyes.png`，不依赖积木完成度。整图缺失时保留原立绘并推进；眼图缺失或尺寸不匹配时直接切换静态暗游戏并推进。完成、退出、异常及超时均清理黑幕并结束动画等待；场景退出也取消旧气泡收起的等待，并停止原对话的后续推进。素材与对齐说明见[资源文档](assets.md)及[结尾立绘记录](../../outputs/ending_visual_yami/README.md)。本节记录当前实现，不代表已完成游戏内验收。

历史兼容记录：此前“完成千年积木”选项把只读遗物模板传给 `WithRelic(RelicModel)`，曾触发 `CanonicalModelException`，后改用 `WithRelic<AncientThermalVortexCore>(player)` 创建可变展示实例。最新单按钮方案已删除该选项及专用奖励回调，此历史修复不代表当前结尾仍提供或执行核心替换。

## 界面、选择与输入边界

战斗外仍可能查询有拥有者的卡牌。红莲魔兽的消耗堆计数、天霆炉神的实时手牌计数与融合素材牌堆查询均以PlayerCombatState是否存在为读取条件；缺失时分别返回0或空候选，不访问战斗牌堆。天霆炉神已锁定的结算手牌数量仍优先于实时计数。素材／目标的绿色数值投影要求Creature.CombatState和PlayerCombatState同时存在，缺失时保留公式。以上为预览与查询的空状态处理，不改变战斗内卡牌数值。来源：[Cards/GrenMajuDaEiza.cs](../ThermalVortexCode/Cards/GrenMajuDaEiza.cs)、[Cards/DivineArsenalFurnaceGod.cs](../ThermalVortexCode/Cards/DivineArsenalFurnaceGod.cs)、[Relics/ThermalVortexCore.cs](../ThermalVortexCode/Relics/ThermalVortexCore.cs)、[Cards/CardEffectPreviewValues.cs](../ThermalVortexCode/Cards/CardEffectPreviewValues.cs)。

素材与目标选择的效果预览在原生卡片节点完成 `_Ready` 后才刷新费用和目标视觉；创建尚未挂入场景树的预览卡时不调用这些方法。融合素材选择右侧目标卡及同类带 `pretendPlayable` 标记的武藤游戏预览卡（包括继承该标记的右键放大卡），保留左上素材数量／费用及其正常的 X 数值标记，隐藏“无法打出”的覆盖叉号；星形费用的同类覆盖叉号也隐藏。此处理只作用于展示节点，不改变真实卡牌的打出资格、融合条件或素材支付。预览的创建、挂载或刷新异常会记录日志并释放该预览，保留原本的素材／目标选择流程；玩家选择同步和素材支付规则不由预览层接管。来源：[Patches/CardEffectPreviewUiPatch.cs](../ThermalVortexCode/Patches/CardEffectPreviewUiPatch.cs)、[Patches/CardPreviewPlayabilityPatch.cs](../ThermalVortexCode/Patches/CardPreviewPlayabilityPatch.cs)。

手牌出牌预览也只刷新仍在场景树内且已就绪的卡片，附属视觉异常不向原生出牌动作传播。素材预览退出时恢复列表原宽度并删除面板记录。效果的本地目标选择同时等待选择结果和当前界面退出；房间／目标管理器退出或后发选择接管时，旧选择返回中断，不循环重开。清理只取消本次仍拥有的未完成选择，恢复手柄导航前重新核对原战斗房间；读取目标管理器先检查当前局与全局界面，避免退出后读取空单例。来源：[Patches/CardEffectPreviewUiPatch.cs](../ThermalVortexCode/Patches/CardEffectPreviewUiPatch.cs)、[Cards/EffectTargeting.cs](../ThermalVortexCode/Cards/EffectTargeting.cs)。

场地界面绘制怪兽当前/最大生命、怪兽位、可选/目标状态并与实际顺序服务对接。卡牌目标限制会过滤非法敌人/场上怪兽；融合素材选择显示实际来源堆徽标，并使用专用预览上下文展示所选素材产生的生命/伤害/格挡投影。预览不支付素材、不消耗焊接、不写回成长；无有效战斗上下文时保留公式。 来源：[Patches/MonsterFieldUiPatch.cs:28](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/MonsterFieldUiPatch.cs:28>)、[Patches/RestrictedCardTargetPatch.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/RestrictedCardTargetPatch.cs:11>)、[Patches/CardSelectionLocationBadgePatch.cs:15](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/CardSelectionLocationBadgePatch.cs:15>)、[Patches/CardEffectPreviewUiPatch.cs:19](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/CardEffectPreviewUiPatch.cs:19>)。

选择卡牌的来源位置标签（手牌、抽牌堆、弃牌堆等）挂在对应卡牌的卡面节点 `NCard.Body` 下，使用相对 `ZIndex=0`，随卡面一起隐藏、淡出、缩放，并受同一详情遮罩与相关卡说明遮挡。位置按实际 `%Frame` 卡框的四角换算到卡面局部坐标，贴卡框底边居中；不把零尺寸、中心原点的 `Body` 当作卡片左上角。标签在卡牌离开场景树时隐藏，重新进入网格后按当前选择界面刷新，避免池化复用到其他界面时残留旧位置。来源：[Patches/CardSelectionLocationBadgePatch.cs](../ThermalVortexCode/Patches/CardSelectionLocationBadgePatch.cs)。

顶部额外牌组入口区分永久拥有与本战斗剩余，顶部悬浮仍显示本场剩余/拥有总数；奖励池入口展示主/额外目录。额外牌组查看界面逐张展示永久拥有的实体，升级牌沿用原生绿色名称与“+”标记，不在卡牌效果末尾显示同类剩余/拥有数量。每张卡牌下方原生位置标签改为“已使用”或“未使用”，仅此查看界面使用该标签；手牌、抽牌堆、弃牌堆、奖励池及其他选牌界面保持各自显示。战斗中按该实体是否仍可从本场额外牌组取用判断：提取或消耗后显示“已使用”，返还并恢复可取用后显示“未使用”；战斗外全部显示“未使用”。它们复用原生牌堆浏览界面及临时代理，关闭时恢复状态；虚拟牌组按钮没有原生主牌组快捷键。悬浮卡组使用网格，额外怪兽小图保持专用色，升级和附魔显示读对应条目。遗物悬浮在打开牌堆时有退场清理，避免旧浮层遮挡。 来源：[Patches/ExtraDeckTopBarPatch.cs:46](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ExtraDeckTopBarPatch.cs:46>)、[Patches/ExtraDeckHoverTipGridPatch.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ExtraDeckHoverTipGridPatch.cs:11>)、[Patches/ExtraDeckTinyCardColorPatch.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ExtraDeckTinyCardColorPatch.cs:11>)、[Patches/RelicHoverCardPileFadePatch.cs:14](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/RelicHoverCardPileFadePatch.cs:14>)。

额外牌组原生附魔使用带版本标记的稳定JSON保存模型名称、数量及完整属性；无附魔旧条目继续兼容，未知ID跳过、异常升级按0、超额条目按原上限截取。每个附魔字段先完整解析；无法确认身份的旧网络编号附魔或损坏的附魔数据会明确阻止读取和保存，含附魔条目也不能被静默跳过或截断。保留原存档，不能按当前编号猜测恢复。吞噬成长仍只属于本场战斗，不写入永久附魔字段。

右键卡牌由统一CardInspection输入入口打开原生详细查看；当前拖牌或选目标时右键优先原生取消，并消费对应抬起事件，避免取消后又打开卡牌。普通右键在详情已开时可关闭。悬浮预览牌上的左键被消费，防止点到下层商店/卡牌；详情关闭淡出期间仍阻挡鼠标、按键和手柄按钮穿透。 来源：[CardInspection/CardInspectionInputRouter.cs:53](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/CardInspection/CardInspectionInputRouter.cs:53>)。

检查注册器只从实际可见卡面、裁剪范围、画布顺序及当前屏幕上下文命中；隐藏牌/被模态层遮住的卡不应进入查看。它保留原牌堆、预览模式、目标、forceUnpowered/pretendPlayable与相关卡组索引；手牌、网格、历史卡牌与自定义区域可形成查看组。开启/关闭状态机持有来源屏幕及返回焦点，屏幕切换会失效清理，不允许把陈旧展示快照当作当前战斗实体。 来源：[CardInspection/CardInspectionRegistry.cs:22](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/CardInspection/CardInspectionRegistry.cs:22>)、[CardInspection/CardInspectionService.cs:12](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/CardInspection/CardInspectionService.cs:12>)、[CardInspection/CardInspectionRequest.cs:8](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/CardInspection/CardInspectionRequest.cs:8>)。

角色选人图、战斗立绘、商店/营地立绘、能量球图层有角色限定补丁；卡牌库修正肖像、稀有度排序，控制台卡牌列表单独排序，升级预览使用当前绑定语义。结束/历史摘要按角色与胜利/败北/放弃上下文选择角色文本，不改变战斗胜负判断。美术具体来源和路径见[美术与资源](assets.md)。 来源：[Patches/CharacterSelectFullArtPatch.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/CharacterSelectFullArtPatch.cs:11>)、[Patches/BattleCharacterArtPatch.cs:10](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/BattleCharacterArtPatch.cs:10>)、[Patches/MerchantCharacterArtPatch.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/MerchantCharacterArtPatch.cs:11>)、[Patches/RestSiteCharacterArtPatch.cs:9](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/RestSiteCharacterArtPatch.cs:9>)、[Patches/CardLibraryRarityOrderPatch.cs:15](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/CardLibraryRarityOrderPatch.cs:15>)、[Patches/CardConsoleSortPatch.cs:12](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/CardConsoleSortPatch.cs:12>)、[Patches/CardUpgradePreviewPatch.cs:13](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/CardUpgradePreviewPatch.cs:13>)、[Patches/ThermalVortexRunSummaryPatch.cs:17](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ThermalVortexRunSummaryPatch.cs:17>)。

## 原生调用、运行边界与继续开发入口

ThermalVortexCommandCompat对能力施加、层数变更、生成入堆和自定义固定格挡调用提供参数兼容封装，最终仍调用原生命令。原版NoBlockPower只阻止来自卡牌的普通格挡，会跳过Unpowered及没有卡牌来源的格挡；励辉重启骑士的卡面是更强的“本回合无法获得格挡”，因此本牌完成原版能力施加步骤后必定登记绑定当前战斗与玩家回合的专属锁，不依赖PowerCmd返回的是新实例还是叠加实例。BlockVar与decimal两种GainBlock入口以及Creature.GainBlockInternal最终写入点都检查该锁，进入敌方回合或下个玩家回合后自动失效；原版、其他Mod、Unpowered、无CardPlay来源及绕过公开命令直接写入的格挡都不能在锁定回合生效，同时不会改变其他来源施加的原版NoBlockPower语义。项目内固定格挡也在命令分派前短路，稳压磁圈、休眠磁场兽、千年守护者、断路护符兽消耗格挡、千年神殿回合末格挡、全装甲雷枪维持格挡及其吞噬继承版本均受约束；卡牌动态格挡预览执行相同检查。Vfx的CardPlay攻击重载通过CommonActions按卡牌目标类型分派；显式Creature目标的decimal/CalculatedDamageVar重载创建DamageCmd.Attack并Targeting(target)，固定攻击传入目标。全体攻击使用独立CardAttackAllEnemies入口，由一个原生AttackCommand统一处理目标和段数。两个显式CalculatedDamageVar入口共享CreateCalculatedAttack，读取变量Props中的Unpowered标记并显式调用attack.Unpowered()，不依赖原生构造函数自动传递该属性。励辉重启骑士清空敌人格挡后造成基础1段20/28点攻击伤害；天霆炉神造成基础1段公式伤害；幻之召唤神的千年十字效果保留每实体一次结算守卫，以锁定计数N造成基础N段全体N点Unpowered伤害。敌人数均不增加基础攻击段数；仍保留原生ModifyAttackHitCount次数修正，无存活目标时可提前结束。幻之召唤神对承受全部N段的单名敌人，在未计格挡、伤害及次数修正时理论总伤害为N²。其他原生Hook、跨版本反射入口及多人实际画面未做运行验证，不能从这项局部修改推断全面兼容。 来源：[Commands/ThermalVortexCommandCompat.cs:11](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Commands/ThermalVortexCommandCompat.cs:11>)、[Vfx/ThermalVortexCombatVfx.cs:18](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Vfx/ThermalVortexCombatVfx.cs:18>)、[Cards/BrilliantRebootKnight.cs:44](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/BrilliantRebootKnight.cs:44>)、[Cards/DivineArsenalFurnaceGod.cs:59](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/DivineArsenalFurnaceGod.cs:59>)、[Cards/MillenniumCross.cs:116](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Cards/MillenniumCross.cs:116>)。

怪兽场界面只绑定本地玩家，找不到本地武藤游戏玩家时不回退到队列中的其他玩家。

战斗结束清理本场额外快照和吞噬成长。新局、读档与RunManager清理会重置场地生命/顺序、吞噬、敌人抽牌捕获、无效作用域、伤害守卫、不死鸟复活、额外牌组UI/代理与遗物悬浮等战斗临时状态。继续修改公共规则时从对应服务和上文源码入口追踪；修改卡面或数值时同步卡牌文档及唯一数值工作簿。安装与精准验收流程见[诊断与开发操作](diagnostics.md)，本文不是历史PASS清单。 来源：[Patches/ThermalVortexRunLifecyclePatch.cs:78](<C:/Users/16014/Documents/简单的实验 4t/ThermalVortex/ThermalVortexCode/Patches/ThermalVortexRunLifecyclePatch.cs:78>)。

## 固定数量的抽牌堆顶部效果

效果需要查看或处理抽牌堆顶部固定N张牌时，若当前抽牌堆不足N张，保持已有抽牌堆的顺序，先将弃牌堆额外怪兽归还本场额外牌组，再将普通弃牌单独洗混后补至抽牌堆底部，继续结算；每次效果只补洗一次。强欲而贪婪之壶要求抽牌堆与弃牌堆可进入抽牌堆的普通牌合计至少10张，额外弃牌不计数，补洗后消耗恰好10张；英雄到来和千年控牌在可用普通牌仍不足N张时改为处理全部现有牌。按牌种检索目标的效果不适用此规则，普通“抽N张牌”继续使用游戏原生抽牌与洗牌流程，洗入抽牌堆的额外怪兽由核心在下一次抽牌前归还额外牌组。 来源：[CardSelectionHelper.cs](<../ThermalVortexCode/Cards/CardSelectionHelper.cs>)、[PotOfDesires.cs](<../ThermalVortexCode/Cards/PotOfDesires.cs>)、[NewMainDeckCards.cs](<../ThermalVortexCode/Cards/NewMainDeckCards.cs>)、[MillenniumPieceEffects.cs](<../ThermalVortexCode/Cards/MillenniumPieceEffects.cs>)。

## 千年部件与循环

五种封印部件统一为0费、1生命、技能型怪兽、消耗且不可升级。只有五种同时位于手牌才能赢得战斗，场上部件和其他牌堆不计，所有千年怪兽及幻之召唤神的替代作用已取消。身份按实际有效卡牌识别；继承了部件能力的宿主不会因此成为部件。

仅在手牌中生效，同名部件只提供一次。使用千年卡前记录手牌，排除本次打出的那张牌；另有同名副本仍可生效。结算中新获得的部件从下一张牌起生效。原牌及召唤完成后，依次结算控牌、抽牌、加能量、融合，不限每回合次数。 千年的大义贼、千年召唤者、千年的密钥、千年邪、千年守护者成功召唤时也触发已快照的唯一手牌持有效果，包括正常融合、直接特殊召唤及再次入场。左手控牌5张、右手抽2张、左腿加1能量、右腿提供一次可跳过的融合。同名部件不叠加，同一次打出与自身入场只结算一次。手持来源以本次事件提前快照为准，用作素材离手的部件不参与，本次入场奖励新获得的部件不追溯本次触发。躯干仍在手牌中持续保留封印部件，不提供召唤奖励。千年召唤者、千年邪与千年守护者均计入千年卡及千年怪兽，也可作为要求千年怪兽的融合素材；它们仍是额外怪兽，不进入千年奖励怪兽生成池。

例：打出唯一一张右手，成功召唤抽6张，不额外抽自身持有的2张；另有右手留在手牌时才再抽2张。左腿同理，本体只加3能量，另有同名手牌副本才再加1。抽到的新部件不会插入本次已记录的持有效果队列。

| 部件 | 本体效果 | 唯一手牌持有 |
|---|---|---|
| 左手 | 成功召唤后控牌15张 | 每使用千年卡控牌5张 |
| 右手 | 成功召唤后抽6张 | 每使用千年卡抽2张 |
| 左腿 | 成功召唤后加3能量 | 每使用千年卡加1能量 |
| 右腿 | 在场时每次友方怪兽成功召唤后可融合，包括自身 | 每使用千年卡可融合 |
| 躯干 | 成功召唤后玩家恢复10生命 | 手牌全部封印部件保留，包括自身 |

查看抽牌堆顶指定数量的牌，可选择任意张置入弃牌堆，包括0张。抽牌堆不足时，保持原牌序，先归还弃牌堆额外怪兽，再将普通弃牌洗混补至底部；可用普通牌总数不足则查看全部。每次查看只补洗一次，刚弃掉的牌不在同次查看中再次补洗，未弃掉的牌保持顺序。

可使用手牌和场上的合法怪兽素材，按升级融合的全部条件召唤1只融合怪兽，可以跳过。同一张牌的使用及自身入场只提供一次机会，同事件的手牌、场上与继承来源不叠加。新怪兽入场是新事件，仍有场上右足能力时可以继续融合。

没有额外每回合次数限制。控牌可以选0张，融合可以跳过，抽牌和加能量自动执行。右足使用原版升级融合的素材与目标限制；场上素材能腾出的空位按原融合预判处理，不能把任意千年怪兽当作指定电子素材。不增加满场主动入口，也不新增“千年融合”卡；此名称只是右足融合效果的解释词。

成功召唤包括手动打出、自动打出、直接特殊召唤及再次入场。先兑现本体入场奖励，再执行对应打牌的持有效果，完整等待选牌和攻击目标选择。部件本体效果允许吞噬继承，吞噬当下不立即兑现，宿主之后成功入场才逐份执行；两份右手抽6再抽6，两份左手独立控15再控15。右足场上效果在吞噬宿主仍在场时有效，同一事件来自本体、手牌、复制及多份吞噬的融合机会仍去重为一次。完整复制使用同一组新效果，避免本体和复制分派双算；手牌持有效果不能吞噬继承。

躯干保留随实际手牌状态动态变化：躯干离开手牌后不再提供保留，其他保留来源不受影响。治疗恢复玩家当前生命，遵守正常最大生命上限。五种集齐时每场只提交一次胜利，战斗结束立即停止未完成的选牌、抽牌、加能量和融合链。

下个己方回合开始时，仍在场的原牌进入弃牌堆，并将1个部件加入手牌。普通版从五种部件等概率随机，升级版自选。该离场不是素材支付或消耗；完整复制可以继承此转化，吞噬不能。 原石板弃置后可重新抽取使用，再次入场重新预约。多张石板逐个转化；来源提前离场便取消该次预约。千年的石板则改为3／2费即时技能，无手牌消耗代价，自选1个部件后正常弃置；千年供奉为2／1费，成功支付2个场上素材后自选部件。千年的伙伴保持原版3／2费，从角色完整普通主卡奖励怪兽目录生成未升级牌。

实现入口：[MillenniumSeries.cs](../ThermalVortexCode/Cards/MillenniumSeries.cs)、[五部件卡牌](card-effects-summary.md)、[千年能力与解释](keywords-and-powers.md)。

