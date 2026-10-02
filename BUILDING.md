# 从源码构建

本仓库包含当前模组源码与运行时资源。游戏本体、原生游戏 DLL/PCK、本机配置和临时探针不随源码分发。

Windows 开发环境需要 .NET SDK（支持 net9.0）、已安装的《杀戮尖塔 2》，资源打包另需 Godot 4.5.1。BaseLib 3.1.2 与构建工具依赖由项目的 NuGet 配置恢复；游戏 DLL 和 Harmony 从自己的游戏安装目录读取。

在仓库根目录执行（把游戏路径换成自己的安装目录）：

```powershell
$dotnet = (Get-Command dotnet).Source
./ThermalVortex/scripts/build-mod.ps1 -DotNetPath $dotnet -Sts2Path 'D:/SteamLibrary/steamapps/common/Slay the Spire 2' -Restore
```

普通构建保持 DeployMod=false、PckPackerEnabled=false，不会改动已安装游戏。

资源包准备与安装请参照 [diagnostics.md](ThermalVortex/docs/diagnostics.md)。资源打包脚本使用仓库内的 `ThermalVortex/design/yugi-character-rig/Invoke-GodotTool.ps1` 和本机 `.tools/godot-rig/Godot_v4.5.1-stable_win64.exe`；请自行将对应官方版本的真实 EXE 放到该位置（不是 `_console.exe`）。工具和 SDK 不随源码分发。

融合卡框、立绘边框、标题条、类型牌匾和稀有度 Shader 在运行时读取玩家游戏资源；源码和模组包不含这九项原版副本。独立融合预览须先加载合法安装的游戏 PCK。

游玩请下载 Release 的 `Yugi-Mod-v0.1.1.zip`，不要把 GitHub 自动生成的源码压缩包当作可安装模组。
