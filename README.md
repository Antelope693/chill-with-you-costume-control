> **注意**
>
> 这里是 [1219323890/chill-with-you-costume-control](https://github.com/1219323890/chill-with-you-costume-control) 的 fork。
> 原仓库有一阵子没更新了，插件装好后游戏里看不到衣装按钮、F7~F10 也没反应，查下来是三个问题：
> 按钮坐标被 CanvasScaler 放大后跑到了屏幕外、这个游戏是 URP 管线导致插件的 Update 不会被调用、内置字体取不到中文。
>
> 修复已提 [PR #1](https://github.com/1219323890/chill-with-you-costume-control/pull/1)，代码在 `fix/ui-offscreen-and-hotkeys` 分支。
> 想直接用的话，[Releases](https://github.com/Antelope693/chill-with-you-costume-control/releases) 里有编译好的 DLL。
> 原作者合并之后这个 fork 就可以删了。

# Chill with You 衣服控制插件

这是一个用于《Chill with You Lo-Fi Story》的 BepInEx 插件，用来固定或手动切换角色当天穿的衣服。

## 这个插件能做什么

- 不再完全依赖游戏每天随机换衣服。
- 游戏内显示可拖动的 `衣装` 按钮和分页面板。
- 可以手动选择 `Default_1`、`Polo_1`、`Polo_2`、`Tee_1`、`Tee_2`。
- 可以点按钮随机一套。
- 可以尝试切换角色动作。
- 面板里有日志页，方便判断插件是否正常工作。
- `衣装` 按钮和展开后的面板都可以拖动，位置会保存到配置文件。

## 适用环境

- Windows 版《Chill with You Lo-Fi Story》。
- Steam 正版游戏。
- BepInEx 5 x64。

本仓库不包含游戏文件、Unity DLL、BepInEx 核心 DLL 或任何原游戏资源。

## 第一步：找到游戏目录

在 Steam 里右键游戏：

```text
管理 -> 浏览本地文件
```

打开后的目录一般长这样：

```text
...\steamapps\common\Chill with You Lo-Fi Story
```

这个目录里应该能看到：

```text
Chill With You.exe
Chill With You_Data
UnityPlayer.dll
```

后面文档里说的 `<游戏目录>`，指的就是这个文件夹。

## 第二步：安装 BepInEx 5 x64

如果你已经装过 BepInEx，可以跳过这一步。

1. 打开 BepInEx 5 发布页：

   ```text
   https://github.com/BepInEx/BepInEx/releases
   ```

2. 下载 Windows x64 版本，文件名类似：

   ```text
   BepInEx_win_x64_5.4.xx.zip
   ```

3. 解压这个压缩包。

4. 把压缩包里面的内容复制到 `<游戏目录>`。

复制完成后，`<游戏目录>` 里应该能看到这些文件或文件夹：

```text
BepInEx
doorstop_config.ini
winhttp.dll
```

5. 启动一次游戏，然后退出。

这一步是为了让 BepInEx 自动生成目录。退出后应该能看到：

```text
<游戏目录>\BepInEx\plugins
<游戏目录>\BepInEx\config
<游戏目录>\BepInEx\LogOutput.log
```

## 第三步：安装衣服控制插件

把编译好的插件文件：

```text
Sherry.CostumeControl.dll
```

复制到：

```text
<游戏目录>\BepInEx\plugins\
```

最终路径应该是：

```text
<游戏目录>\BepInEx\plugins\Sherry.CostumeControl.dll
```

然后重新启动游戏。

## 第四步：确认是否安装成功

进入游戏房间主界面后，右侧附近应该会出现：

```text
衣装
```

面板里如果显示：

```text
衣服服务：已捕获
```

说明插件已经捕获到游戏的换装服务，可以正常使用。

如果面板没有出现，打开这个日志文件：

```text
<游戏目录>\BepInEx\LogOutput.log
```

搜索：

```text
Sherry Costume Control
```

如果日志里没有这行，通常是 DLL 没放到 `BepInEx\plugins`，或者 BepInEx 没装成功。

## 使用方法

点击游戏里的 `衣装` 按钮会打开面板。`衣装` 按钮和面板都可以直接拖动，拖到顺手的位置后会自动保存。

面板底部有 `上页`、`下页`，目前分为三页：

```text
衣服页：切换和固定衣服
动作页：尝试切换角色动作
日志页：查看插件状态和错误原因
```

在衣服页里点击衣服按钮即可切换。

可选衣服：

```text
Default_1
Polo_1
Polo_2
Tee_1
Tee_2
```

常用按钮：

```text
应用并固定：固定当前选中的衣服
随机：随机切换到另一套衣服
重载配置：重新读取配置文件
收起：折叠面板
衣装：收起后点击它重新展开面板
上页/下页：切换面板分页
重置位置：把衣装按钮和面板恢复到默认位置
清空日志：清空面板里的临时日志
```

动作页里的按钮：

```text
伸展：尝试让角色做伸展动作
喝茶：尝试让角色喝茶
打气：尝试让角色做打气动作
读书：尝试让角色读书
休息：尝试切到休息状态
互动：尝试触发点击角色时的互动反应
```

动作功能会受游戏当前状态影响。如果正在剧情、工作计时或休息计时中，游戏可能会把动作切回原来的状态，这是正常情况。

## 配置文件

首次启动后会生成配置文件：

```text
<游戏目录>\BepInEx\config\Sherry.CostumeControl.json
```

里面的 `skinType` 可以手动修改：

```text
Default_1 = 1
Polo_1 = 1001
Polo_2 = 1002
Tee_1 = 2001
Tee_2 = 2002
```

示例：

```json
{
  "mode": "fixed",
  "skinType": 1002,
  "toggleX": -34,
  "toggleY": -222,
  "panelX": -92,
  "panelY": -150
}
```

改完配置后，可以点面板里的 `重载配置`，也可以重启游戏。

## 卸载插件

只卸载衣服控制插件，删除这两个文件即可：

```text
<游戏目录>\BepInEx\plugins\Sherry.CostumeControl.dll
<游戏目录>\BepInEx\config\Sherry.CostumeControl.json
```

如果想完全移除 BepInEx，还需要删除游戏根目录中的：

```text
BepInEx
winhttp.dll
doorstop_config.ini
.doorstop_version
```

## 常见问题

### 1. 游戏里没有出现面板

先检查插件是否放对位置：

```text
<游戏目录>\BepInEx\plugins\Sherry.CostumeControl.dll
```

再检查日志：

```text
<游戏目录>\BepInEx\LogOutput.log
```

搜索：

```text
Sherry Costume Control
```

### 2. 面板显示“服务：未捕获”

先进入游戏房间主界面。插件需要等游戏创建换装服务后才能切换衣服。

### 3. 衣服没有马上变化

先确认面板显示 `服务：已捕获`，然后点击衣服按钮或 `应用并固定`。

如果仍然没有变化，可以退出游戏重进一次。配置文件会保存当前选择。

### 4. 游戏无法启动

先临时禁用插件：

```text
把 <游戏目录>\BepInEx\plugins\Sherry.CostumeControl.dll 改名为 Sherry.CostumeControl.dll.off
```

如果还是无法启动，再检查 BepInEx 是否安装正确。

## 从源码编译

如果只是安装插件，可以跳过这一节。下面是给想自己编译源码的用户看的。

1. 复制本地引用配置：

   ```powershell
   Copy-Item .\GameReferences.props.example .\GameReferences.props
   ```

2. 如果你的游戏不在默认 Steam 路径，请编辑 `GameReferences.props`：

   ```xml
   <GameRoot>F:\STEAM\steamapps\common\Chill with You Lo-Fi Story</GameRoot>
   ```

3. 编译：

   ```powershell
   .\scripts\build.ps1
   ```

4. 安装到游戏：

   ```powershell
   .\scripts\install.ps1
   ```

编译产物位置：

```text
src\Sherry.CostumeControl\bin\Release\net472\Sherry.CostumeControl.dll
```

## 说明

本项目仅用于学习交流，不用于盈利或任何非法用途。请支持正版游戏，不要上传、分发或打包任何原游戏文件。

如果本项目存在侵权或不当内容，请联系 `1219323890@qq.com`，我会及时处理或删除。
