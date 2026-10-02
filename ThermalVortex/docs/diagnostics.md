# 武藤游戏构建、安装与精准探针

整理日期：2026-09-13。本文依据当前 [构建入口](../scripts/build-mod.ps1)、[项目文件](../ThermalVortex.csproj)、[安装器](../scripts/install-mod.ps1)、[测试运行器](../scripts/playtest.ps1)、[互斥模块](../scripts/ModLifecycle.psm1) 和 [探针宿主](../ThermalVortexCode/Diagnostics/TaskProbeHost.cs)。仅在需要构建、安装、精准探针或恢复时读取对应小节；操作授权、文案保护与默认验证范围见 [工作约定](../../AGENTS.md)。

## 普通构建

从仓库根目录使用共用入口：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\ThermalVortex\scripts\build-mod.ps1
```

入口默认调用 `dotnet build --no-restore`，显式保持 `DeployMod=false`、`PckPackerEnabled=false`，不携带探针，不写真实 Mod。安装准备与无探针恢复也复用这一实现；安装准备的构建计入本次所需构建，不先另做一次普通构建。直接向项目传 `DeployMod=true` 会触发 `RejectDirectDeployment` 错误。

普通构建可使用 `-Configuration`（默认 `Debug`）、`-DotNetPath`、`-Sts2Path`、`-OutputPath` 和 `-BuildLockTimeoutSeconds`（1–60 秒，默认 60）。`-Restore` 仅在已确认依赖配置变化时显式使用；依赖产物缺失或无恢复构建明确因依赖状态失败时，由共用实现进行条件式 restore，并补一次 build。`-TaskProbeSource` 只供授权探针流程使用；`-IncludeResources` 连同 `-ResourceRoot`、`-ResourceTemporaryRoot` 只供需要资源打包的准备流程使用，普通构建不传这些参数。

依赖声明为 Godot.NET.Sdk 4.5.1、net9.0、BaseLib 3.1.2、Publicizer 2.3.0、PckPacker 0.1.1 和版本通配的 ModAnalyzers；这些是项目声明，不是对运行中实际加载版本的检测。`Directory.Build.props` 提供本机路径，`Sts2PathDiscovery.props` 推导数据和 ModsPath。只有依赖配置改变、`project.assets.json` 缺失或无恢复构建明确因依赖状态失败时才恢复依赖。

普通代码修改默认构建一次；本次修改造成编译错误时，修复后允许必要复构建，成功后停止。没有代码或依赖状态变化时，不原样重复失败构建。纯外部文档修改不触发构建；JSON 或 PNG 的格式核对仅覆盖本次实际修改的资源。

## 显式安装

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\ThermalVortex\scripts\install-mod.ps1
```

现有默认调用在入口立即拒绝活游戏，再完成一次准备与提交。只有需要安装本次资源变化时添加 `-IncludeResources`。安装器不会启动游戏或结束现有游戏进程。

需要在游戏运行期间准备、最后时刻关闭后立即写入时，在同一 PowerShell 会话分两步使用：

```powershell
$preparedInstall = & .\ThermalVortex\scripts\install-mod.ps1 -PrepareOnly
# 安装内容已准备好；此处只按本次仍有效的 PID + StartTimeUtc 一次性授权处理游戏。
& .\ThermalVortex\scripts\install-mod.ps1 -PreparedInstall $preparedInstall.PreparedDirectory
```

上述注释不授予关闭进程的权限。`-PrepareOnly` 可在活游戏存在时执行，唯一结果对象包含 `PreparedDirectory`、`ManifestPath`、`InstalledModRoot`。`-PreparedInstall <目录>` 只校验并提交已准备产物，不重复构建或打包，也不关闭游戏。两个参数互斥。

| 参数 | 当前含义 |
| --- | --- |
| `PrepareOnly` | 仅准备安装产物；允许游戏运行。 |
| `PreparedInstall` | 提交准备目录，仅可另传两个锁等待参数，禁止覆盖准备时的配置、构建器、游戏／目标目录、资源、探针或 restore 参数。 |
| `IncludeResources` | 准备时构建并打包一次 PCK；不传则仅安装 DLL、PDB、Mod JSON，并核对已有 PCK 未改变。 |
| `Configuration` | 构建配置，默认 `Debug`。 |
| `DotNetPath` | 可覆盖本机 .NET；默认寻找工作区 `.tools/dotnet`。 |
| `Sts2Path` | 可覆盖供项目使用的游戏路径。 |
| `ModsPath` | 可直接指定 mods 父目录；不传时查询项目属性。安装目标子目录仍为 `ThermalVortex`。 |
| `LifecycleLockTimeoutSeconds` | 获取该真实 Mod 目录互斥锁的最长等待，1–60 秒，默认 60。 |
| `BuildLockTimeoutSeconds` | 共用构建互斥的最长等待，1–60 秒，默认 60；提交阶段不构建。 |
| `Restore` | 仅准备阶段可用，明确依赖配置改变时透传共用构建入口；提交阶段禁止传入。 |
| `TaskProbeSource` | 测试版专用的 C# 文件入口，由精准测试流程使用，不用于普通安装。 |

准备阶段：

1. 解析项目与固定安装目标，在工作区已忽略的 `.codex-temp/prepared-installs/ThermalVortex-install-<guid>/` 中创建本次独立目录。
2. 通过共用入口构建，只有依赖状态符合条件时 restore。资源准备会创建暂存目录，排除 `.bak`／`.bak-*` 文件以及 `images/charui/character_select_bg_yugi_loop/`，使用 `ThermalVortex` 资源前缀，只打包一次。普通资源使用 PckPacker；存在其不支持的 `.tscn`／`.gdshader`／`.gdscript`／`.gdextension` 时，由 `scripts/pack-native-resources.ps1` 在独立临时 Godot 工程中导入图片，再用原生 PCKPacker 打包原资源、导入映射和导入产物。临时 `project.godot` 不进入 Mod PCK，Godot 退出码及日志保留在工作区 `.codex-temp`。
3. 将 DLL、PDB、Mod JSON 和本次可选的 PCK 冻结在准备目录的 `build/` 下。`prepared-install.json` 固定记录项目、目标目录、配置、资源／探针属性和文件 SHA-256，旁边的 `prepared-install.sha256` 用于检查清单完整性。
4. 准备失败清理本次目录，成功则保留至提交。准备产物不保存关闭授权，也不触发后台安装；准备后源码继续修改，应重新准备，清单不证明产物只含本任务改动。

原生资源打包的临时工程设置 `editor/import/use_multiple_threads=false`，使用串行导入，避免本机 Godot 4.5.1 并行导入阶段的原生崩溃；源资源不因此删减。导入仍要求退出码为0且日志无错误，成功后才进入一次 PCK 打包。

提交阶段：

1. 校验目录归属、项目、冻结目标、清单和各文件 SHA-256；产物缺失、哈希不符或清单目标被改动时，在真实写入前拒绝。
2. 获取该目标 Mod 目录互斥锁，等待后再次检查游戏。目标取自准备清单，提交参数不能将产物改装到另一目录。
3. 备份本次目标文件集，所有真实文件操作复用受保护函数。每次向真实目录暂存复制前检查游戏，核对临时文件 SHA-256，原子替换前再检查，替换后核对源／目标 SHA-256；删除目标文件前同样检查。代码安装保留已有 PCK。
4. 某一步失败时保留原错误，无活游戏才尝试回滚；回滚与测试备份恢复复用同样的逐文件保护。出现新游戏立即停止后续替换或删除，并报告恢复未完成，不能保证多文件安装必定整体回滚。
5. 对通过校验并进入提交的产物，结束时释放互斥锁并清理本次准备目录；校验未通过的输入不会进入提交清理。提交被活游戏阻塞后不保留后台续装意图，后续需在无活游戏时重新完成已明确授权的安装。

单个文件替换与多个文件组成的安装集是两个层次。进程检查也是离散检查，不能声称脚本会连续监控并拦住任何瞬间的新启动。若新游戏阻止恢复，不能假定真实 Mod 已回到安装前状态，应在游戏关闭后重新完成明确授权的安装。

## 并发与游戏进程

`ModLifecycle.psm1` 对规范化的目标目录计算 `Local\ThermalVortex.RealMod.v1.` 前缀的互斥名称；同一 Windows 会话中使用该模块的同目标安装、探针、恢复串行进行。测试运行器在整个生命周期持锁，安装器在真实文件操作阶段持锁。这个锁不阻止不同文件的源码编辑，也不是全工作区锁；直接复制文件或其他 Windows 会话不受该命名互斥覆盖。

共用构建入口另按实际共享的中间目录串行 restore/build，覆盖共享依赖文件，等待最长 60 秒，结束或异常均释放。不共享构建目录的任务可并行；更换输出目录但继续共用 `obj` 时仍需同一把构建锁。构建锁与真实 Mod 锁分开，不阻止源码编辑。若另一任务正在编辑会参与整项目构建的文件，仅在构建边界做一次不超过 60 秒的有界等待；到期仍不稳定就报告阻塞并结束，不能靠构建锁声称源码快照已隔离，也不后台轮询。

游戏运行与源码编辑／不部署的本地构建兼容，与真实 Mod 文件替换互斥。安装器只拒绝活游戏，不负责关闭进程。若用户要求代理关闭游戏，必须遵循工作约定中的一次性 `PID + StartTimeUtc` 授权；新启动的实例不受旧授权覆盖。

## 一次性精准探针

仅在用户明确要求进游戏测试时使用：

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\ThermalVortex\scripts\playtest.ps1 -ProbeSource <.codex-temp/task-probes下的C#文件> -ProbeId <探针Id>
```

| 参数 | 当前含义 |
| --- | --- |
| `ProbeSource` | 必填，别名 `ProbePath`；必须位于工作区 `.codex-temp/task-probes` 内。 |
| `ProbeId` | 必填，与实现的 `Id` 完全一致。 |
| `IncludeResources` | 本次测试需要资源变化时，首次安装一起打包。 |
| `GamePath` | 游戏可执行文件路径，文件名必须是 `SlayTheSpire2.exe`；当前默认是本机 D 盘 Steam 路径，换机器应显式指定。 |
| `TimeoutSeconds` | 别名 `Timeout`；单次等待 5–3600 秒，默认 180。 |
| `DotNetPath` | .NET 路径覆盖。 |
| `LifecycleLockTimeoutSeconds` | 目标目录互斥锁等待，1–60 秒。 |

探针接口：

```csharp
internal interface ITaskProbe
{
    string Id { get; }
    Task RunAsync(TaskProbeContext context, CancellationToken cancellationToken);
}
```

`TaskProbeContext` 提供 `NGame`／`Game`、会话和探针 ID、战斗上下文、`Check`、`Assert` 及有界 `WaitUntilAsync`。`Check` 会记录检查；`Assert` 失败中止执行。等待超时返回 false，由探针确定断言含义。当前宿主传给 `RunAsync` 的令牌是 `CancellationToken.None`；脚本的进程等待时限不能理解为宿主会自动取消任意异步代码。宿主拒绝创建超过30分钟或超前当前时间超过5分钟的会话标记；这是标记有效期，不是探针执行超时。

运行器拒绝已有游戏、错误路径和已有会话标记。它复制本次探针到自己的会话目录，备份目标文件，安装仅含该探针的 DLL，写会话标记并启动一个实例。每次写会话标记前、实际启动前重新检查游戏，发现外部实例即停止，不启动第二实例或接管外部进程。产品断言失败不重试；仅基础设施失败可对相同构建原样重试一次，不重新构建，重试前仍须无活游戏。不会扫描第三方 Mod 日志来判断产品结果。

## 结果与清理

宿主结果包含 `schemaVersion`、`sessionId`、`probeId`、`status`、`failureKind`、`message`、`exceptionType`、`stackTrace`、`startedUtc`、`completedUtc` 和 `checks`。每条检查包含名称、状态、消息与记录时间。状态为 `PASS`／`FAIL`；失败类别为 `ASSERTION`／`INFRASTRUCTURE`。运行器校验结果属于本次会话及同一探针。

运行器的关闭目标来自自己启动的进程：温和关闭前核对 PID 与启动时间，不匹配或不能读取启动时间时拒绝关闭。温和关闭未成功时，强制关闭前再次读取该 PID，并核对进程名严格为 `SlayTheSpire2`、启动时间仍与本次启动身份一致；不匹配就拒绝关闭。清理删除本次探针源码、暂存副本、会话标记和结果，删除失败必须报告清理警告。

一旦尝试安装测试版，无论结果是通过、断言失败还是基础设施失败，清理阶段都通过 `-PrepareOnly` 准备一次当前源码的无探针代码产物；游戏运行不阻止这次本地构建。准备成功且无活游戏时，使用 `-PreparedInstall` 提交，提交不重复构建。此恢复仅处理代码文件，测试时安装的 PCK 保留。

运行器始终分别输出 `ProbeResult`（`PASS`、`FAIL ASSERTION` 或 `FAIL INFRASTRUCTURE`）与 `CleanupStatus`，清理问题不改写已有探针结果：

| 清理状态 | 含义与后续处理 |
| --- | --- |
| `COMPLETE` | 无探针恢复和本次临时文件清理均完成。 |
| `WARNING_RESTORE_REQUIRED` | 活游戏阻止无探针写入，保留原探针结果，不重跑；游戏关闭后只完成无探针安装。 |
| `ERROR_RESTORE_FAILED` | 无探针准备或文件操作实际失败。无活游戏时尝试测试前备份兜底；兜底也须逐文件检查游戏，成功不代表当前源码已经安装。 |
| `WARNING_CLEANUP_INCOMPLETE` | 临时源码、标记、目录等未完全清理，或存在其他清理警告；不能仅输出 `COMPLETE`，须阅读具体警告。 |

待恢复或恢复错误可以与临时清理警告同时输出，以逗号分隔；实际构建失败必须报告恢复错误，不能只报活游戏警告。被活游戏阻塞的无探针准备目录会清理，不保存后台安装意图。后续无探针恢复使用普通安装命令重新准备及安装，不重新测试。探针执行错误与清理错误并存时，两者都需报告。

## 共享选卡界面故障

原生 `NSimpleCardSelectScreen` 在战斗中使用首张候选牌的 `Owner` 初始化牌堆控件。本次大义贼购买金币档位卡住的日志先出现 `ArgumentNullException(player)`，调用链为 `PileTypeExtensions.GetPile` → `NCombatCardPile.Initialize` → `NSimpleCardSelectScreen.ConnectSignalsAndInitGrid`，之后点击才出现空引用；根因是档位展示副本没有 `Owner`，导致界面初始化中断。

临时选项必须是 mutable 牌。共享 [CardSelectionHelper](../ThermalVortexCode/Cards/CardSelectionHelper.cs) 的 `ChooseMany`／`ChooseManyWithBack` 在创建界面前，为尚无 `Owner` 的临时选项补上本次选择玩家，保留原实例引用和候选索引，不将选项注册为真实战斗牌；canonical 或空候选项应在创建界面前拒绝并写日志。自动选择上下文会跳过 Godot 界面，不能据此排除 `_Ready` 初始化或点击错误；真实界面验证仍仅按用户本次明确授权执行，不自动追加进游戏测试。
