# 旧版 v1.0：独立启动器模式

这是 VoiceLinkVR/VRCLS 的非官方分支。麦克风译成日语时，SteamVR 主字幕上方的小窗保留最近一句罗马音、日文和原文。其他人的字幕不会将它顶走。Quest 左手柄按住 X 时暂停翻译收音，松开恢复原状态；桌面音频和 VRChat 自己的麦克风不受影响。

## 一次安装

1. 先安装官方 VRCLS。首个已实测的基础版本为 **0.7.0.1015**。上游 GitHub 公开快照为 0.7.0.1006，和该发行版不同。
2. 从 [Releases](https://github.com/pan0001/VRCLS-Romaji/releases/tag/v1.0.0) 下载 `VRCLS-Romaji-windows-x64.zip`，完整解压后运行 `Install.cmd`。首次安装需要联网获取固定版本、校验 SHA-256 的 NuGet 依赖（已有本机缓存也可使用）。普通用户权限即可，不需要 .NET SDK。
3. 退出正在运行的官方程序，从开始菜单打开 **VRCLS Romaji**。麦克风目标语言选日语，并在 VRCLS 中开启 SteamVR 字幕。首次注册 SteamVR 自动启动时请从分支入口进行。

如果官方 VRCLS 安装在其他位置，在解压目录运行：

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Install.ps1 -InstallDir 'D:\VRCLS'
```

## 官方更新之后

照常更新官方程序。**退出更新器自动启动的官方实例，再打开 VRCLS Romaji**。启动器每次检查官方安装；相关接口仍兼容时，从新版文件生成新的独立副本，运行真实字幕/词典测试后才切换。不需要重新运行安装包，也不会把旧 DLL 写回官方安装目录。

如果新版改了关键接口、输入循环或钩子方法，启动器会保留并使用上一次已验证的副本，原因记在分支目录的 `status.txt`。这时自定义功能仍可用，但该副本还没有新版的变化；适配新接口后才可切换。首次安装没有旧副本且版本不兼容时会明确失败。直接启动官方快捷方式仍是官方版本；分支与官方实例不应同时运行。

分支默认目录：`%LOCALAPPDATA%\Programs\VRCLS-Romaji`。分支的 SteamVR 注册路径指向固定启动器，不指向某一版副本。每个新副本占用一份运行文件的磁盘空间。要腾出空间，请先退出程序，保留 `current.txt` 指向的目录，可删除其他旧的 `app-…` 目录。

## 配置与移除

两个入口使用 VRCLS 原有用户配置。仅在本人麦克风通道显示日语注音；不做声纹识别。X 键读取依附活动的 SteamVR 输入循环，不会额外启动访问 OpenVR 的后台线程。小窗按上传完成事件回收资源，一次只提交一张在途纹理。

要移除分支，退出 VRCLS，删除分支目录及开始菜单中的 VRCLS Romaji 快捷方式；用官方入口启动并重新注册其 SteamVR 自动启动。原来的独立 DLL 补丁若已安装，仍按其自带的恢复脚本卸载。本安装器不修改、恢复或删除官方文件。

## 开发

Windows x64，.NET 8 SDK，提供本机已安装 VRCLS 作为编译引用：

```powershell
.\romaji\Build.ps1 -ReferenceDir "$env:LOCALAPPDATA\Programs\VRCLS"
```

生成包位于 `romaji/dist/VRCLS-Romaji-windows-x64.zip`。发行包不含官方 EXE/DLL、个人设置、凭据、日志或语音记录。小窗与补丁生成器的源码在本目录；上游应用源码沿用仓库原有历史。第三方依赖由 NuGet 获取：Mono.Cecil 0.11.6、Kawazu 1.1.4、LibNMeCab / LibNMeCab.IpaDicBin 0.10.0，词典附带 AUTHORS 和 COPYING。
