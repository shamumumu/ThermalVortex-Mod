# 武藤游戏资源与素材说明

整理日期：2026-09-07。生产资源的入口依据当前代码；来源清单保留原图与加工输出的关系。历史清单中指向的生产路径可能已被后续素材覆盖，文件存在不等于仍是清单当时的图像版本。

## 当前加载入口

资源前缀由 [MainFile.ResPath](../ThermalVortexCode/MainFile.cs) 定义为 `res://ThermalVortex`，磁盘资源在项目内的 `ThermalVortex` 子目录。

| 资源 | 当前入口 |
| --- | --- |
| 卡牌小图／大图 | `images/card_portraits` 与其 `big` 子目录；各卡的 PortraitPath 等属性给出文件名。`StringExtensions` 缺图时退回对应 `card.png`；小图作用域使大图请求改用小图。 |
| 能力图标 | `images/powers` 与 `big` 子目录；能力类给出图标名，公共扩展缺图时使用 `power.png`。显示是否生效还取决于实际能力及 UI 绑定。 |
| 遗物图标 | `images/relics` 与 `big` 子目录；缺图时对应 `relic.png`。 |
| 角色与地图图标 | `Character/ThermalVortex.cs` 指向 `images/charui` 中的角色图标、选人图标、锁定图标与地图标记。 |
| 战斗立绘 | 角色创建使用 `battle_visual_yugi.png`；`MillenniumPuzzleCharacterArt` 与战斗／商店／休息点补丁按千年积木状态选择立绘，并有加载失败回退。 |
| 建筑师结尾立绘 | 入场保留原立绘；`ArchitectFirstResponsePatch` 接入首句选项，通过 `ArchitectFirstResponse.AwaitRevealThenAdvance` 在首次回应时调用 `ArchitectYamiRevealVfx.PlayAsync`，于黑幕下将本事件 `EmbeddedCombatRoom` 中武藤游戏的 `YugiSprite` 换为 `ending_visual_yami.png`。当前项目资源已换为用户提供的三张手持卡、另一手自然下垂版，经本地去背景并配准到既有地标。以人物实体高度、中心及鞋底对齐，保留朝向、对白锚点与收尾动作；缺图沿用原立绘。母图、设计要点与透明处理方法见[结尾立绘记录](../../outputs/ending_visual_yami/README.md)。 |
| 建筑师暗游戏变身 | 首句仅显示“回应”或“另一个我……”一个按钮；点击后沿用原生按钮禁用流程并收起旧气泡，再播放一次全黑 0.25 秒 → 眼光淡入 0.25 秒 → 眼光脉动 0.30 秒 → 暗游戏显形 0.50 秒；动画结束才推进暗游戏回复，四组均采用此时序，后续对话不重复。`ending_visual_yami_eyes.png` 与整图均为 1536×1024、共用坐标并跟随 Sprite 变换及翻转。缺眼图或尺寸不符直接切换静态暗游戏并推进；完成、退出、异常及超时均清理黑幕并结束等待，场景退出后不再推进。立绘保留至收尾挡雷动作开始，不依赖积木完成度。 |
| 建筑师胜利挡雷 | `ArchitectVictoryGuardPatch` 仅在结局 `TheArchitect.WinRun` 中准备，建筑师实际 `Attack` 触发之后同帧起手；`ArchitectVictoryGuardVfx` 使用 `images/charui/actions/victory_guard` 的八张原始透明帧及 `vfx/yugi_victory_guard/yugi_victory_guard.tscn`。当前采用 V3 居中紫金护罩；原生雷电接触罩面时锁定举手终帧、完整护罩和冲击亮闪，截住罩面以下雷电，替换身体火焰并保留一次原震屏。进入胜利结算时，主角、护罩与建筑师一起隐藏，真实退出时清理；详情见下节。 |
| 选人背景 | `CharacterSelectFullArtPatch` 当前 `AnimatedBackgroundEnabled=false`，使用 `character_select_bg_yugi.png` 静态图。动态目录保留，但当前分支不启用。 |
| 能量计数器 | `Character/ThermalVortex.cs` 使用 `images/charui/energy_counter/layer_1.png` 至 `layer_5.png`；层序补丁恢复原场景顺序。详见[能量图标说明](../design/energy-icon-concepts/energy-icon-prompts.md)。 |
| 卡面与行内能量图标 | `ThermalVortexCardPool`、`MillenniumColorlessCardPool` 使用 `images/charui/big_energy.png`、`images/charui/text_energy.png`，与战斗计数器分开加载。 |
| 打击丢卡牌动作 | `MonsterPlayActionVfx` 使用 `images/charui/actions/monster_play/frame_000.png` 至 `frame_021.png`，仅玩家从手牌手动打出“打击”时播放一次，在释放点继续伤害结算。怪兽改用下述小魔法规则。 |
| 角色受击动作 | `YugiNativeActionController` 使用 `images/charui/actions/ludo_native/hurt` 的原始透明PNG；仅敌方伤害经格挡、怪兽防护与生命损失修正后实际扣到角色生命时播放。完全格挡、怪兽全吸收与零掉血不播放，致死交由死亡表现。对照原生每次命中重新触发，从清晰护腹的 `frame_001` 起播；异常尾帧由 `frame_023` 保持替代，24个时间槽／30fps仍为0.8秒，最后0.1秒平滑接回待机。不排队或等待动画结束，与待机、施法和旧投放动作共用贴图控制。来源和时长依据见[受击素材接入记录](../../outputs/ludo_hurt_install_20260914/README.md)。 |
| 灰流丽特效 | `AshBlossomVfx` 使用 `images/vfx/ash_blossom` 的 96 帧，初始化时发起预载；加载失败有日志和相应分支，不能凭文件齐全断言效果播放成功。 |
| 电子龙攻击命中 | `CyberDragonHitVfx` 使用 `images/vfx/cyber_dragon_hit/frame_000.png` 至 `frame_015.png` 的原始透明帧。电子龙及升级版通过原生攻击命中入口，在敌人的原受击锚点播放一次白蓝电弧，显示画布 280×280、总长 0.35 秒、末尾 0.10 秒淡出；不等待播放结束，不改变伤害与命中次数。加载失败沿用原受击特效。来源与接入说明见[电子龙攻击素材记录](../../outputs/cyber_dragon_hit_20260914/README.md)。 |
| 艾克佐迪亚特殊胜利 | `ExodiaTorsoVfx` 使用 `images/vfx/exodia_special_win/summon_special_win.ogv`。来源为用户提供的 `summonspecialwin` 中 P4027 Spine 与时间轴，经用户再次剪辑后为 1280×720、30 fps、6.6 秒；独立声音采用 A 自然衔接版，剪口交叉淡化、尾音平滑收束。原角色动作与锁链保留，外围 Unity 光效适配到离线合成。详见下节。 |
| 八张怪兽出场展示 | `TcgMonsterCutinVfx` 按卡牌类型读取 `images/vfx/tcg_monster_cutin/<卡牌标识>/sequence.json` 和透明 PNG 帧，来源为 2026-09-12 恢复的 TCG Spine 原动作。对应关系、触发与旧版保留方式见下节。 |
| 融合召唤 | `FusionSummonVfx` 加载 `vfx/fusion/fusion_summon.tscn`，使用原融合贴图、移植的材质计算及曲线，动态显示本次素材卡。生产与独立预览共用场景，详见“融合召唤还原”一节。 |
| 其他特效 | `Vfx` 下的具体实现决定帧图、节点、粒子及播放时机，涵盖导爆、电子终结龙、电子龙无限等。无独立图片不等于没有特效。 |

路径解析见 [StringExtensions.cs](../ThermalVortexCode/Extensions/StringExtensions.cs)，角色入口见 [ThermalVortex.cs](../ThermalVortexCode/Character/ThermalVortex.cs)，实际条件见 [Patches](../ThermalVortexCode/Patches) 和 [Vfx](../ThermalVortexCode/Vfx)。

当前建筑师立绘来源为用户提供的 `C:/Users/16014/Downloads/exec-0ef71fff-89cd-4c65-91ef-5e38ab3da46c.png`：三张卡握在手中，另一手自然下垂。经用户授权，本地去背景阶段保留不透明主体的原始 RGB，仅处理背景与半透明边缘；整图与独立眼层再使用同一仿射矩阵，配准到既有 1536×1024 地标（头顶 y=10、鞋底 y=1005、脚锚 x=783.5）。来源、脚本与两张 RGBA 产物保存在 `outputs/yugi_victory_guard_20260915/portrait/`，已沿用 `ending_visual_yami.png` 与 `ending_visual_yami_eyes.png` 两个原文件名写入正式资源目录 `ThermalVortex/ThermalVortex/images/charui/`。首次回应变身时序不变，立绘维持至 `WinRun` 中建筑师实际 `Attack` 之后同帧起手的 V3 挡雷动作。本轮未安装真实 Mod、未启动游戏。

此前四张悬浮卡、宽紫光带及金色卡沿的精简版来源为 `outputs/ending_visual_yami/sparse-vfx-concept.png`，加工脚本 `prepare_sparse_layers.py` 及 `sparse-production/` 两张透明产物保留作历史。该版于 2026-09-09 按用户要求完成含资源的真实 Mod 安装，四个安装文件的源／目标 SHA-256 全部一致，未启动游戏；该安装记录不代表本轮新立绘已安装。

## 建筑师胜利挡雷（2026-09-15）

来源为用户提供的 `C:/Users/16014/Downloads/胜利2/sprite-max-px-frames-25-rows-5-cols-5-frames`，仅采用 `000、002、005、008、012、014、016、017` 八张 386×534 原始 RGBA PNG，按原编号保存；下载原文件保持不变。统一身体比例并使用逐帧鞋底锚，非等时抬手去掉翻掌变形及重复停留；017 只在命中回调出现。护罩由独立薄膜、亮边和冲击层绘制，最后三成动作展开，不烧入人物帧。罩面按各阶段指尖轮廓抬高，完整成罩时最小间隙约 19.28 个原素材像素。按用户新参考将人物置于护罩水平中心，半径收为人物高度的 `0.68 × 0.62`；主膜下沿向上拱起、下回弧极淡，去掉厚亮底圈。球面投影的紫色折射纹、金色细脉、罩内外分叉电流、金色短流星、掌心上方聚能星芒及沿曲面传播的命中环波共同构成光效。薄膜／人物／亮边分别位于局部 Z 层 0／1／2，并共同响应人物显示调制。

起手只发生在本次结局建筑师的实际 `SetAnimationTrigger("Attack")` 之后，准备时不显示动作。普通／快速等待分别跟随原生 0.5／0.25 秒，雷电仍使用原场景的五张纹理、位置和时间轴。`lightning_bridge.gd` 复制本次实例的动画资源，按最终罩面与原雷不透明像素的首次交点设置即时回调；当前素材两档均在雷电局部 0.066666670 秒接触，即名义起手后约 0.5667／0.3167 秒。终帧、完整护罩、实际雷截口亮闪和一次原震屏共用该触点。即时档不另加等待，有雷电画面时在接触处直接显示终态；原流程可能先进入结算。

结局专属显示锁阻止待机、受击、旧投放与死亡召回覆盖挡雷姿态，保留原生生命、胜利记录、死亡 UI 清理和既有胜利结算跳转。人物及护罩在原 `Visuals` 内共同重挂；`ThermalVortexVictorySummaryPatch` 在打开胜利统计前统一隐藏玩家和敌人的显示层，主角、护罩与建筑师不出现在统计界面。挡雷终态保持到该切换点，不创建额外等待任务；真正退出、资源错误或结局异常时清理，未起手的准备在结局任务结束时释放。开场对白的攻击不准备这项演出。

制作源映射、共用生产场景与原生雷电的独立预览、定向检查和本次构建日志见 [胜利挡雷制作目录](../../outputs/yugi_victory_guard_20260915/README.md)。本次未安装真实 Mod、未启动游戏；独立预览不代表游戏内验收。

## 艾克佐迪亚特殊胜利（2026-09-12）

源目录为 `D:\新建文件夹\assets\assets\resourcesassetbundle\duel\timeline\duel\universal\summon\summonspecialwin`。使用 `spine/p4027/P4027JS.json`（Spine 4.2.24）、`spine/p4027/0.3/P4027.atlas.txt` 和配套图集，经官方 Spine 4.2 WebGL 运行库离线渲染，保留原骨骼、加权网格、路径约束及锁链变形。`animation2` 为初始静止姿态，时间轴 4.7 秒切入 5.4667 秒的 `animation`；原 Spine 激活区间为 3.0–9.81663 秒，完整时间轴为 11.4 秒。

五张占位卡面改用模组现有五部件卡图。火焰使用同目录 `fxt_nis_010`、`fxt_nis_002` 噪声纹理，光球与闪光使用随附 `fxt_ple_*` 纹理，按原前后火焰、眼闪、聚能球和冲击波的起始时点合成。源目录没有可执行的原 Unity Shader 程序，导出的 `.shader` 只保留说明与属性，粒子参数也未完整保留；外围光效属于适配重建，不能称为原 Unity 演出的逐像素复刻。声音轨仅有事件引用，视频文件仍无音轨；现从用户补充的 MDPro3 目录提取原 `SE_EV_EXODIA`，以独立 `audio/vfx/exodia_special_win.ogg` 同步播放，保留源 0.3 秒触发点并按上述视频节奏保音调重定时。

此前成片按用户要求调整节奏：源时间 0–3 秒的五部件展示保持原速，3–9.81663 秒的完整角色段连同光效以 1.1 倍速播放；删除 10.9–11.4 秒约 0.5 秒的纯黑停留，并将角色段之后的收黑过渡再缩短 0.25 秒，末帧保留黑场。按 30 fps 取整后为 301 帧、10.0333 秒。原始时间轴证据不改写，重定时参数保存在制作目录的 `timing.json`。

当前采用用户提供的 `E:/9月14日 (2).mp4` 短版，在此前成片中删去 3.1–6.533333 秒，最终 198 帧时点、6.6 秒。配套声音选用 A 自然衔接：在新 2.98–3.22 秒等功率交叉淡化，保留后续爆发与画面的对应，末尾 0.32 秒平滑收声。视频与音频已同步替换项目正式资源；试听、独立 WAV、分轨及剪辑参数位于 `outputs/exodia-special-win-user-cut-20260914`，这次短版已于 2026-09-14 16:40 随其他更新写入真实游戏 Mod，安装器源／目标 SHA-256 核对通过，见[统一安装回执](../../outputs/mod_install_20260914_hurt_and_updates/install-receipt.json)。

生产文件是 Godot 原生支持的 Ogg Theora 视频，避免将整段帧图同时载入显存。`ExodiaTorsoVfx.PlayAsync` 在独立黑幕层中等比居中播放，等待视频完成后才继续原特殊胜利结算；没有额外固定黑场等待，无需修改播放代码即可采用新时长。仍只由手牌五种封印部件集齐触发，每场战斗的防重复结算保持原样，与幻之召唤神的登场展示独立。

加载或解码失败时记录日志并继续原结算；播放最长等待 15 秒，角色节点退出、覆盖层退出、战斗结束或切换时清理视频和黑幕。卡牌在演出结束后再次核对原战斗身份，防止旧演出结算到另一场战斗。此说明是源码行为，未以进游戏测试确认。

制作脚本、可拖动时间轴的离线预览、MP4 预览及来源记录见 [本次制作目录](../../outputs/exodia-special-win/README.md)。源目录未被修改。本次为项目资源替换；安装这项修改需要按安装流程显式包含资源 PCK。

## 八张怪兽出场展示（2026-09-12）

素材实际目录为 `D:\新建文件夹\assets\resourcesassetbundle\duel\timeline\duel\monstercutin\Spine动画_编号匹配_20260912\tcg`。使用各模型的原有 `animation` 动作，经该目录现成的 Spine 4.2 官方 WebGL 播放器采样为透明 PNG；保留原网格变形、贴图层次和约 1.6 秒的时序。每组画布 1024×768、30 fps、48 帧，使用整段动作的固定取景，播放时按视口等比居中并淡入淡出黑幕。

| 卡牌 | 源编号／文件夹 | 新资源子目录 |
| --- | --- | --- |
| 电子终结龙 | P6397／电子终结龙 | `cyber_end_dragon` |
| 电子龙无限 | P11765／电子龙无限 | `cyber_dragon_infinity` |
| 黑魔导 | P4041／黑魔导 | `dark_magician` |
| 青眼白龙 | P4007／青眼白龙 | `blue_eyes_white_dragon` |
| 幻之召唤神 艾克佐迪亚 | P20212／幻之召唤神 | `phantom_summoning_god_exodia` |
| 拉之翼神龙 | P5000／拉之翼神龙 | `winged_dragon_of_ra` |
| 拉之翼神龙－不死鸟 | P12234／拉之翼神龙 不死鸟 | `winged_dragon_of_ra_phoenix` |
| 天霆号 阿宙斯 | P15524／天霆号 | `divine_arsenal_furnace_god` |

八张卡从各自 `OnPlay` 接入，展示完成后继续原有效果；不改变伤害、格挡、素材、牌堆或形态转换规则。入口要求本次打出的卡就是展示卡、且是首次结算，混沌幻影借用其他卡的效果与同一打出的复诵不额外播放。两张电子龙与天霆号还要求正在执行已授权的额外牌组召唤。天霆号即使没有可造成的伤害也有出场展示。拉与不死鸟的正常形态转换、千年十字生成幻之召唤神均经过即时打出流程，能够触发展示；不死鸟战斗复活直接返回怪兽场，沿用既有复活表现。

主牌在出牌前开始预载；额外牌组在 `FusionSummonVfx` 的通用融合阶段预载，回退光环 `XyzSummonVfx` 也保留预载入口。播放器最多缓存一组帧，切换怪兽时释放旧纹理，避免八组同时常驻显存。透明原图直接显示，不使用旧视频的黑底处理、高光增强或边缘羽化着色器。资源缺失或解码失败记录日志并跳过展示，继续卡牌结算。

PNG 在安装打包时会转换为 `.ctex` 并保留 `.png.import` 映射，包内没有同名原始 PNG。播放器通过 `ResourceLoader` 读取原路径并跟随导入映射，使用独立缓存模式以便释放纹理；只有未导入的原始 PNG 才回退到字节解码。`sequence.json` 按原文件读取。不能仅用 `FileAccess.FileExists(frame_000.png)` 判断打包后的帧是否存在。

**电子终结龙和电子龙无限的旧版原样留作备用**：`ThermalVortexCode/Vfx/CyberEndDragonSummonVfx.cs`、`CyberDragonInfinitySummonVfx.cs` 及 `images/vfx/cyber_end_dragon/frame_*.jpg`、`images/vfx/cyber_dragon_infinity/frame_*.jpg` 均保留。当前调用及预载指向新播放器；恢复旧版时需同步改回对应卡的旧 `PlayAsync`，以及 `FusionSummonVfx`、`XyzSummonVfx` 的预载调用。青眼、黑魔导和天霆号此前的代码绘制展示类也保留，当前召唤入口使用上述新素材。

制作脚本、来源与抽样预览保存在 [本次素材制作目录](../../outputs/tcg-monster-cutin)。源文件及源目录的其他动作没有修改或删除。P12234 的源恢复资料已注明其外部 Unity 自定义 Shader 没有恢复，本次沿用现有 Spine 动作和纹理的效果。

八张出场展示现使用原共享时间轴 `summonmonster/04backeff` 的属性音效，均从出场起点播放：电子终结龙、电子龙无限、青眼白龙、天霆号使用 `SE_MONSTER_CUTIN_LIGHT`；黑魔导、幻之召唤神使用 `SE_MONSTER_CUTIN_DARK`；拉与不死鸟使用 `SE_MONSTER_CUTIN_DIVINE`。声音截取至现有 1.6 秒展示并淡出，预载不播放声音；来源、卡库属性对应及加工记录见 [音效制作目录](../../outputs/vfx-audio/README.md)。

## 融合召唤还原（2026-09-12）

来源为 `D:\新建文件夹\assets\assets\resourcesassetbundle\duel\timeline\duel\universal\summon\summonfusion`。生产场景位于 `ThermalVortex/vfx/fusion`，素材提取脚本、来源记录与预览说明见 [融合制作目录](../../outputs/fusion-summon/README.md)。独立 Godot 工程位于 `design/fusion-summon-preview`，准备脚本复制当前生产场景与指定卡图，预览直接驱动同一场景、Shader 和曲线。

源时间轴采用素材展示约 1.3333 秒、融合主段约 3.0333 秒，总计约 4.3667 秒。前导与主段的串联是缺少原宿主控制器后选定的模组编排，不代表已证实原版总长度。主段内素材吸入、四层红蓝旋涡、Out 与 PostFusion 按原区间重叠，不逐段累加；保留源主段约 1.80 秒的强化节点与 2.8167 秒的收尾节点。按用户指定，正常模式改为最初源时间轴的 80% 速度：素材展示实际约 1.6667 秒、融合主段约 3.7917 秒，共约 5.4583 秒；快速模式仍为正常模式的两倍，即源速 1.6 倍，实际约 2.7292 秒；即时模式跳过新增段。生产宿主和独立预览使用相同速率，源曲线、预览拖动及采样秒数不变，原有怪兽专属动画的速度行为不变。

1～5 张素材采用对应原槽位布局；更多素材沿 Num 的独立单卡层思路按实际数量展开，不截取前五张。超过 12 张使用居中的多行布局，20 张为 5×4、30 张为 6×5，每张素材都有独立翻牌网格和吸入层，保留选择顺序及重复卡。展示端没有另设素材张数上限，但实际可选数量仍受对应卡牌规则约束；例如未来融合仍至多 2 张、升级至多 3 张。三维卡片和吸入层分别使用透明 SubViewport，渲染宽度最多 1600 像素，再等比适配界面，限制高分辨率下多素材的像素开销。

参与融合的素材卡使用普通尖塔卡片的上下布局：模组实际卡图保持比例，放在上半部原生立绘窗口，下半部完整保留原生效果说明底板并留空。素材卡隐藏全部文字（包括卡名、卡效及中间牌匾文字）和能量图标／数值；空白标题条、立绘装饰边框、空白中间牌匾、稀有度颜色、阴影和说明背景均保留。共用 `spire_card_faces.gd` 按攻击／技能／能力框和卡池颜色合成透明卡面，额外牌组沿用当前冷白框着色；标题条、立绘装饰边和牌匾共用实际卡牌 `BannerMaterial` 的颜色。重复卡面共用一次绘制的纹理，保留每张卡的独立运动。缺图或缺少预约快照仍保留对应素材槽位，并使用原生轮廓替代面。

融合结束时的结果卡使用实际召唤卡牌的原生 `NCard`，完整显示卡名、费用、类型和卡效，动态数值由游戏原生显示逻辑生成。`FusionResultCardCapture` 在独立透明视口中绘制后，将完整纹理直接交给结果卡网格；取景包含左上角费用图标，演出结束或中断时清理。原生卡面捕获失败时保留现有无字卡面回退，不阻断召唤。有专属动画的怪兽仍沿用专属登场。

即时融合在支付前捕获卡图路径、卡框类型、类型文字、稀有度与卡框／标题条颜色等不可变字符串／标量；未来融合将同样的快照随预约独立复制，在下回合真正执行召唤时播放。不把纹理、卡牌实体或 UI 节点写入预约，也不改变永久存档。融合／融合+、电子负载融合、奇迹融合、融合之门、千年融合（含右脚能力的复制与继承）都经过统一即时入口，未来融合经过统一预约入口，均调用同一个 `FusionSummonVfx`。

核心卡片扭曲移植原反编译 Shader 的 UV 计算与颜色混合，卡面比例改为当前 340∶450 画布，透明边界使用原生卡框自身 alpha。材质曲线通过属性名 CRC32 低 28 位匹配恢复，保留关键帧和切线；旋转值按弧度使用，`_Scale` 放大采样坐标以使卡面收缩。Num 在放大采样坐标时同步补偿中心偏移，避免大量素材偏向画面角落。原粒子发射组件、完整绑定和曝光流程未恢复，外围旋涡与粒子按原贴图、层次和时间补建；音效事件对应的音频本体现从 MDPro3 资源包提取，由 C# 宿主同步播放亮牌、融合主段与收束声音，普通／快速模式分别保音调处理，即时模式跳过。声音跟随游戏主音量、音效音量及后台静音，演出结束或中断时停止。具体恢复与补建项记录在资源目录的 `recovery-notes.md`，声音来源见 [音效制作目录](../../outputs/vfx-audio/README.md)。

新演出在素材支付有效后、原生目标选择与正式打出之前播放。演出结束先撤掉遮罩，再交还原流程；有专属登场动画的怪兽不重复长时间展示结果卡，仍由原卡牌入口播放专属动画。新场景加载或初始化失败时回退现有通用光环，视觉异常不改变卡牌结算；战斗退出则直接清理，不继续播放回退演出。

2026-09-12 初版独立场景的 23 项定向检查通过，完成 13 张关键画面及交互界面截图；这些历史结果不作为新版原生卡框和 20／30 张展示的验证证据。2026-09-13 卡框与大数量修改完成一次关闭部署与资源打包的 `dotnet build --no-restore`，0 警告、0 错误，日志位于 `outputs/fusion-summon/many-materials/build.log`。本轮没有安装真实 Mod，也没有启动游戏验收；未来融合快照已检查源码接线与复制语义，未做游戏内跨回合实测。

新版另通过 19 项大数量／清理定向检查，并重新渲染目视检查八张无文字卡面和 20／30 张关键画面。首帧卡面纹理未准备好导致的碎裂已修复：先等待卡面完成绘制，再刷新演出视口，停止或退出使旧等待失效。完整范围、日志与图片见 [本轮制作与验证记录](../../outputs/fusion-summon/many-materials/README.md)，不作为任意素材数量或游戏内性能保证。

随后按用户澄清，将最初的整框铺图调整为“上方卡图、下方空白说明面板”，只修改卡面合成 Shader。生产与预览副本同步，定向渲染素材卡、冷白结果卡及单卡图已目视确认；记录见 [原生空白面板样式](../../outputs/fusion-summon/native-empty-panel/README.md)。此轮没有 C# 修改或构建、安装、游戏启动，也没有重复运行大数量检查。

最终按“保留原生装饰、去除所有文字和能量”补回标题条、立绘边框、中间牌匾及稀有度颜色，所有文字节点与字体依赖均移除。新增快照稀有度参数的一次 C# 构建通过，五张灰／蓝／金卡面及演出截图目视确认；装饰最小尺寸导致的放大裁切已修复。当前画面和本次日志见 [最终无字卡面](../../outputs/fusion-summon/native-details/README.md)。没有安装真实 Mod 或启动游戏。

## 游戏文字资源

`localization/zhs` 与 `localization/eng` 各有以下七份文件。它们属于游戏资源，当前外部说明不替代或回写它们。

| 文件 | 用途 | 对应外部说明 |
| --- | --- | --- |
| `cards.json` | 卡名、卡面、选牌和额外牌组提示 | 卡牌效果说明、当前实现总表 |
| `card_keywords.json` | 关键词名称及解释 | 关键词与能力说明 |
| `powers.json` | 普通／动态能力说明 | 关键词与能力说明 |
| `static_hover_tips.json` | 固定悬浮提示、吞噬记录等模板 | 关键词与能力说明、玩法系统说明 |
| `relics.json` | 遗物名称、效果和风味文字 | 玩法系统说明 |
| `characters.json` | 角色介绍、奖励池构筑及预设界面 | 玩法系统说明 |
| `ancients.json` | 先古对话及选项 | 玩法系统说明 |

`ThermalVortex.json` 的 ID、名称、简介和依赖是 Mod 管理信息。对外名称为武藤游戏；资源键名仍使用既有内部前缀。

建筑师的武藤游戏结尾文本在中英文 `ancients.json` 中各有四组，编号 `0–3`，均设 `visit=0` 并以 `r` 标记可重复，从首次通关开始由原生随机逻辑每次等概率选择，末尾 `-attack` 均配置为 `Architect`。每份资源仅保留 32 个结尾键：14 句台词、10 个按钮、4 个访问条件和 4 个攻击配置。“完成千年积木”“合成电磁圈”的标题与描述键均已移除；结尾只显示每句一个原生按钮，不自动发放原选项奖励。电磁圈同名卡牌资源与定义保留。四组角色台词的说话者均为暗游戏，包括第三组；对外角色名称仍为“武藤游戏”。完整中文及首次回应时序见[玩法系统说明](gameplay-systems.md#建筑师结尾对话与首次回应)。

## 素材来源清单

下列 9 份清单保留 88 条来源映射。它们是素材追溯资料，不是卡牌数值表；没有保留旧平衡建议。

| 清单 | 条数 | 如何使用 |
| --- | ---: | --- |
| [卡图取材](../../outputs/power_icon_card_art_refs/manifest.csv) | 45 | `Source` 是原卡图，`Copied` 是参考副本；不证明当前能力图标采用了该副本。 |
| [透明来源第一批](../../outputs/power_icon_transparent_sources/manifest.csv) | 5 | 稳定图标名连接透明大图、当时的大／小图输出及校对图。中文标题按当前名称恢复。 |
| [透明来源第二批](../../outputs/power_icon_transparent_sources_batch2/manifest.csv) | 7 | 同上；与第一批同名的电子灯塔是不同来源记录，两条均保留。 |
| [透明化第三批](../../outputs/power_icon_white_to_transparent_batch3/manifest.csv) | 5 | 原下载文件到透明源图及 256／64／32 输出。 |
| [透明化第四批](../../outputs/power_icon_white_to_transparent_batch4/manifest.csv) | 6 | 保留本批完整加工路径，不按共用原图删除版本。 |
| [透明化第五批](../../outputs/power_icon_white_to_transparent_batch5/manifest.csv) | 5 | 同上。 |
| [透明化第六批](../../outputs/power_icon_white_to_transparent_batch6/manifest.csv) | 6 | 同上。 |
| [透明化第七批](../../outputs/power_icon_white_to_transparent_batch7/manifest.txt) | 5 | 原图位于 Downloads；输出在本批四个尺寸目录。像素统计是历史记录。 |
| [透明化第八批](../../outputs/power_icon_white_to_transparent_batch8/manifest.txt) | 4 | 同上，并收录 speed_gauge、mech_icon 的原局部加工记录。 |

七份 CSV 的显式路径以及两份 TXT 的原图／输出路径在本次整理时均可定位。没有重新测量图片，也没有据此认证某个历史输出仍是当前采用版本。此前缺少版本来源的 alpha／bbox 比较指标不再作为现行资料保留。

## 独立制作入口

2026-09-10 清理设计过程稿后，仍在使用的制作资料集中在以下入口。独立模型与动作工程的完成状态以入口中的当前版本说明为准。

| 制作内容 | 当前资料入口 |
| --- | --- |
| 导爆灰模、造型与动作 | [导爆制作入口](../../outputs/detonation_blender/README.md)，保留当前方案、动作要求和参考来源。 |
| 武藤游戏角色母模型与动作 | [角色动作制作入口](../../outputs/yugi_character_rig/README.md)，保留模型维护、动作触发、施法与原生参考资料。 |
| 建筑师结尾暗游戏 | [结尾立绘记录](../../outputs/ending_visual_yami/README.md)，保留母图、透明加工与对齐说明。 |

## 原生透明待机与卡牌动作（2026-09-14）

新角色动作直接来自用户提供的 `Downloads/待机`、`Downloads/单手魔法`、`Downloads/大魔法` PNG 原生 Alpha；没有沿用录屏或 MP4 的抠图遮罩。三段各25帧，待机保留7 fps（约3.57秒），两段施法均为16.666667 fps（1.5秒）。生产资源位于 `images/charui/actions/ludo_native/{idle,single,grand}`，统一768×614画布，`sequence.json`记录脚底锚点、人物高度、帧率和释放帧；控制器按旧静态立绘的鞋底位置和实际人物高度对齐。

`YugiNativeActionController`在战斗人物创建后接管待机。角色动作由统一的内部动作分配器按所属玩家从手牌手动打出的首次结算分配：非额外怪兽使用单手小魔法，额外卡组怪兽即使临时经过手牌也不触发角色动作；用户明确选择的19张非怪兽牌使用双手大魔法；“打击”使用原丢卡牌动作，其余非怪兽牌无角色动作。禁忌的圣杯（`ForbiddenChalice`）不再绑定试播动作。完整确认名单见[卡牌动作分配方案](../../outputs/yugi_character_rig/卡牌动作分配方案.md)。

小／大魔法分别在第5、12帧解锁牌效，画面与声音继续收尾再回待机；死者苏生（`MonsterReborn`）仍在选定有效回收目标后播放大魔法，资源不可用时回退原复苏特效。自动打出、自动召唤、变身、返场不触发角色动作；千年十字只播放自身的大魔法，自动召出的幻之召唤神不追加角色动作。同次出牌的复制效果、多段伤害与重复结算不重播。丢卡牌动作通过暂停租约共存，卡牌与怪兽自身的特效、声音及现有结算时序保留；未改变卡面文案、数值。

小／大音效来自用户提供的两份MP3，保留原速与音高，释放重音分别对齐0.30、0.72秒；生产声音在 `audio/ludo_cast/{single,grand}_aligned.ogg`，从动作起点同步播放。沿用已选1.5秒预览：小魔法延后0.235秒，大魔法截取原音效0.225–1.725秒并淡出，不变速。调用共享音效播放器，遵循游戏音量及后台静音设置。制作脚本、原始透明帧、关键帧和带声视频见 [原生透明制作目录](../../outputs/ludo_native_alpha_20260914/README.md)，当前时序应用入口为 [apply_selected_timing.py](../../outputs/ludo_native_alpha_20260914/apply_selected_timing.py)。

## 时空召回死亡退场（2026-09-14）

`YugiNativeActionController` 在原生死亡入口接管角色，使用 `images/charui/actions/ludo_native/death` 中用户提供的 36 张身体动作和裂缝 004–028 共 25 张原生透明 PNG。前 8 帧的生成式放大通过显示比例校正；原图不重绘、不抠图。召回演出 2.1 秒，人物绘制在裂缝前方，裁切线位于左侧裂缘边带的右侧内缘，越过后由透明裁切露出场景背景，末段收掉零散残片，人物完全隐藏后才关闭裂缝。局部光效包括人物朝向裂缝的紫色边缘受光、沿弧线吸入的紫金粒子，以及闭合瞬间的金白亮点，均按同一播放时钟在 2.1 秒内结束。整队战败时，`YugiGameOverDeathAnimationPatch` 等原生死亡界面的红色背景过渡完成后播放召回，再继续原版死亡标题和按钮；队友单独死亡保留原入口播放；建筑师胜利挡雷演出有效时由举手护罩接管，不播放召回。死亡终态阻止待机、受击、施法及旧投放恢复人物；凤凰阻死不进入此流程，真实复活恢复显示。生产与独立预览共用 `vfx/yugi_death_recall/yugi_death_recall.tscn`。素材、预览、验证范围见[死亡退场制作记录](../../outputs/yugi_death_recall_20260914/README.md)。本次先红色过渡的时序调整尚未写入真实 Mod 或进行游戏内验收。

## 独立 Godot 动画工具

独立动画构建、采样和批量渲染统一使用 [Invoke-GodotTool.ps1](../design/yugi-character-rig/Invoke-GodotTool.ps1)，直接等待真实 Godot EXE，并保留本次退出码和日志。不要通过 `_console.exe` 包装器判断或清理渲染进程。

Godot 4.5.1 在默认 `user://logs` 不可写时存在原生空指针崩溃。启动器必须把子进程的 `APPDATA`、`LOCALAPPDATA` 指向工作区可写目录；新建独立动画工程还应设置 `debug/file_logging/enable_file_logging=false` 和 `debug/file_logging/enable_file_logging.pc=false`，标准输出与错误由启动器保存。显式 `--log-file` 会覆盖此设置，若确需使用只能指向可写路径。

交互预览由用户决定何时关闭，不套用自动渲染的超时清理。此流程只用于独立动画工具，不授予启动或关闭真实游戏、写入真实 Mod 的权限。

## 打包与保留

普通构建不打包资源。显式资源安装从实际资源目录暂存并打一次 PCK，排除 `.bak`／`.bak-*` 和 `character_select_bg_yugi_loop`；这些排除不代表源文件可删除。Godot 的 `BasicExport` 也排除该选人动画目录，其配置与安装器各自生效，不能混为一个入口。

outputs、design 和临时目录中仍有原图、动画、设计母图、独立加工版本和代码备份。查找生产资源应从当前代码入口开始；查找可继续加工的原图应从上述来源清单开始。不要仅凭旧文件名、未打包、目录被忽略或缺少字符串引用就删除素材。
