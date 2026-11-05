# VRCLS 日语交流补丁：把你说的话变成可以照着读的日语

在 VRChat 里想用日语跟人打招呼，却不知道翻译出来的日文怎么念？你可以先对着麦克风说中文，比如「初次见面，请多关照」。VRCLS 把这句话译成日语后，补丁会在 **SteamVR 主字幕上方**开一个「我的发言」小窗：最上面是可以照着读的**罗马音**，中间是日语译文，下面是你刚说的中文。

对方接着说话，主字幕会继续滚动，但小窗还留着你的这句话。你可以慢慢读给对方听，直到你自己说下一句，小窗才会换成新内容。

![我的发言小窗：第一行是 hajimemashite, yoroshiku onegai shimasu.，第二行是日语译文，第三行是中文「初次见面，请多关照。」](romaji/Patch/images/my-speech.png)

补丁的小窗渲染器使用示例句生成的实际面板画面；图中只展示小窗，不是头显全景。

**这是装在官方 VRCLS 上的补丁。** 装好后照常打开原来的 VRCLS 或 SteamVR 入口，不需要另外启动程序。

[下载 Windows 补丁 ZIP](https://github.com/pan0001/VRCLS-Romaji/releases/download/v1.1.1/VRCLS-Romaji-Patch-windows-x64.zip) · [安装、使用和卸载说明](romaji/Patch/README.md)

首次使用：退出 VRCLS（包括托盘），解压 ZIP 并运行 `Install-Patch.cmd`。再打开官方 VRCLS，开启 SteamVR 字幕，把麦克风的目标语言设为日语即可。

使用 Quest 时，按住左手柄 X 会暂时停止翻译程序收音，松开恢复。兼容的官方更新可以在 VRCLS 退出后自动补回补丁。

本项目是 [VoiceLinkVR/VRCLS](https://github.com/VoiceLinkVR/VRCLS) 的非官方补丁。
