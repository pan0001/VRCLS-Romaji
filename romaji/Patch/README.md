# VRCLS Romaji 原位补丁（Windows x64）

**继续用官方 VRCLS.exe 或原来的 SteamVR 入口。** 不创建分支启动器或第二套常驻 VRCLS。本人日语罗马音、VR 三行小窗和 Quest 左手 X 临时闭麦与分支版一致。

1. 完全退出 VRCLS（含托盘），将 ZIP 全部解压。
2. 双击 `Install-Patch.cmd`。首次安装会使用本机 NuGet 缓存或联网下载固定版本并校验散列。
3. 以后照常打开官方 VRCLS。曾用分支启动器的，请从官方入口打开一次并在设置里重新注册 SteamVR 自动启动，不再使用 **VRCLS Romaji** 快捷方式。

**官方更新后不用重新安装兼容补丁。** 默认在当前用户登录时启动小型后台修复程序，只看本地安装文件的变化，不联网、不录音、不启动或强行关闭 VRCLS。文件稳定且程序已退出后，它会在临时副本上检查接口和字幕功能，再补回。若更新器已自动打开程序，请正常退出一次，等约半分钟（首次生成和验证可能更久）再打开。不兼容时停止写入，需等待新的适配包。

工具、对应版本的备份和 `status.txt` 位于 `%LOCALAPPDATA%\VRCLS-Romaji-Patch`。可用 `powershell -NoProfile -ExecutionPolicy Bypass -File .\Setup.ps1 -InstallDir "D:\VRCLS"` 指定目录；加 `-ManualOnly` 则不启用后台修复。要卸载，请先退出 VRCLS，再双击 `Uninstall-Patch.cmd`，它会关闭自动修复并还原匹配的原版文件，不把旧版备份覆盖到未知新版上。未加载的词典和备份可以保留。账号、配置、语音记录不在安装或还原范围内。

已实测基础版本 0.7.0.1015，并用模拟更新测试兼容迁移和不兼容拒绝。源码及原分支模式见 [GitHub](https://github.com/pan0001/VRCLS-Romaji)。
