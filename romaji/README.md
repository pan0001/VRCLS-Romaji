# VRCLS Romaji · 官方 VRCLS 的日语交流补丁

对着麦克风说「初次见面，请多关照」。VRCLS 译成日语后，**电脑桌面和 SteamVR 头显**各有一个「我的发言」小窗。第一行是可以照着念的罗马音，第二行是日语译文，第三行是你刚说的原话。和日本人聊天时，即使对方已经接话，你仍然可以把自己的这句话读完。小窗只在你再次发言时换句。

**电脑上**，照常打开官方 VRCLS，小窗会出现在主字幕窗上方。拖动「我的发言」标题可移动位置。长句会换行，超过窗口高度可用滚轮查看。PC 主字幕列表使用官方行距，罗马音集中在小窗显示。只用电脑时也可以使用。

![PC 小窗显示自己说的原文、日语译文和罗马音](Patch/images/my-speech-pc.png)

上图是 PC 小窗用示例句生成的实际画面。

麦克风设为「常开（实时识别）」时，**按住键盘 V 暂停翻译收音，松开恢复**，在 VRChat 窗口里也可以使用。Quest 左手柄 X 同样可以暂停翻译收音。两键都按住时，要等两个都松开才会恢复。对方的桌面声音仍可翻译。

**头显里**，开启 SteamVR 字幕后，在主字幕上方看「我的发言」。

![我的发言小窗：第一行是 hajimemashite, yoroshiku onegai shimasu.，第二行是日语译文，第三行是中文「初次见面，请多关照。」](Patch/images/my-speech.png)

补丁的小窗渲染器使用示例句生成的实际面板画面；图中只展示小窗，不是头显全景。

[下载原位补丁](https://github.com/pan0001/VRCLS-Romaji/releases/download/v1.2.1/VRCLS-Romaji-Patch-windows-x64.zip) · [查看安装教程](Patch/README.md)

开发者：在仓库根目录使用 `.\romaji\Build.ps1` 构建。历史 v1.0 模式见[旧版说明](Launcher/README.md)，使用当前补丁不需要它。
