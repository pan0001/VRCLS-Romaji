# VRCLS 日语交流补丁 · 安装与使用

在 VRChat 里想用日语跟人打招呼，却不知道翻译出来的日文怎么念？你可以先对着麦克风说中文，比如「初次见面，请多关照」。VRCLS 把这句话译成日语后，补丁会在 **SteamVR 主字幕上方**开一个「我的发言」小窗：最上面是可以照着读的**罗马音**，中间是日语译文，下面是你刚说的中文。

对方接着说话，主字幕会继续滚动，但小窗还留着你的这句话。你可以慢慢读给对方听，直到你自己说下一句，小窗才会换成新内容。

![我的发言小窗：第一行是 hajimemashite, yoroshiku onegai shimasu.，第二行是日语译文，第三行是中文「初次见面，请多关照。」](images/my-speech.png)

补丁的小窗渲染器使用示例句生成的实际面板画面；图中只展示小窗，不是头显全景。

<details><summary>还没说话时，小窗是什么样子？</summary>

![SteamVR 小窗等待你的发言](images/waiting-steamvr.png)

这是从实际运行的 SteamVR 读取的小窗画面。开启字幕但还没有麦克风译文时，会显示「等待你的发言…」。

</details>

## 安装并试说一句

1. [下载 Windows x64 补丁 ZIP](https://github.com/pan0001/VRCLS-Romaji/releases/download/v1.1.1/VRCLS-Romaji-Patch-windows-x64.zip)，完整解压。
2. 退出官方 VRCLS，包括托盘图标，然后双击 `Install-Patch.cmd`。首次安装会从本机缓存或 NuGet 获取并校验日语词典等依赖。
3. 照常打开官方 VRCLS，开启 SteamVR 字幕，把**麦克风目标语言设为日语**。戴上头显，说「初次见面，请多关照」。译文出来后，看主字幕上方的小窗，照着第一行读即可。

小窗按麦克风通道判断哪些是「你说的话」。Quest 左手柄按住 X 可暂停翻译收音，松开恢复；VRChat 自己的麦克风不受影响。

## 更新或卸载

兼容的官方更新完成后，后台修复工具会等 VRCLS 退出、文件稳定后自动补回。如果更新器直接打开了新版，请正常退出一次，等检查完成再打开。遇到不兼容的版本不会写入，需要等待适配。状态记录在 `%LOCALAPPDATA%\VRCLS-Romaji-Patch\status.txt`。

要卸载，先退出 VRCLS，再双击 `Uninstall-Patch.cmd`。它会停用自动修复，恢复匹配的官方文件。账号和设置不受影响。

如果 VRCLS 装在别的位置，在解压目录用 PowerShell 运行：

~~~powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Setup.ps1 -InstallDir "D:\VRCLS"
~~~

加 `-ManualOnly` 可不启用后台修复。已实测基础版本为 VRCLS 0.7.0.1015。曾使用 v1.0 启动器的用户，改从官方 VRCLS 打开，并在其设置中重新注册 SteamVR 自动启动。
