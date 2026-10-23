# 星露谷修改器 · Stardew Valley Trainer

游戏内按 **F5** 呼出的实时修改器。不是改存档文件，而是直接修改运行中的数据——改完立刻生效，不用退出游戏。

基于 SMAPI 4.x 开发，适用于 **星露谷 1.6.x**。

---

## 功能

菜单按作用对象分成 5 页：

| 页面 | 内容 |
|---|---|
| **角色** | 状态面板、无限体力、无限生命、体力/生命填充、移动速度、工具瞬间蓄力、无限浇水器、钓鱼三项、技能等级、幸运、金钱、**一键采集**、**获取全部工具**、传送 |
| **NPC** | NPC 好感度拉满、宠物好感度拉满、农场动物快乐/饱足拉满、**把任意 NPC 设为配偶**、生成村民、传送 NPC、刷怪、一击必杀、清怪 |
| **环境** | 天气（6 种）、时间（设定/快进/跳转/静止/流速）、日期（年/季/日）、立刻保存 |
| **物品** | 常用物品一键获取（36 种，数量可调） |
| **关于** | 版本号、快捷键、操作方式、命令表 |

几个值得一提的：

- **一键采集** —— 一次清掉当前地图的树、石头、矿石、杂草、树枝、大石头、树桩、原木。掉落走游戏自己的采集逻辑（木材、矿石、纤维都会正常到手），且**不动庄稼**
- **任意 NPC 设为配偶** —— 含矮人、科罗布斯等非村民；换人时上一任自动解除（改为离异）；会自动把房子升到 1 级，避免游戏去找不存在的婚姻版农舍地图
- **文本全外置** —— 界面上每一个字都在 `src/StardewTrainer/i18n/default.json`，翻译只需加一个 json

完整说明见 **[docs/使用说明.md](docs/使用说明.md)**。

---

## 安装

### 1. 装前置 SMAPI

从 [smapi.io](https://smapi.io/) 下载安装器，运行 `install on Windows.bat`，输入 `1`（Install）回车。

### 2. 装修改器

把 `StardewTrainer` 文件夹（含 `StardewTrainer.dll` / `manifest.json` / `i18n/`）放进：

```
<星露谷安装目录>\Mods\StardewTrainer\
```

`i18n` 文件夹必须一起放，否则界面会显示成键名。

### 3. 用 SMAPI 启动

Steam → 右键「星露谷物语」→ 属性 → 通用 → 启动选项，填入：

```
"D:\steam\steamapps\common\Stardew Valley\StardewModdingAPI.exe" %command%
```

（路径换成你自己的）之后正常点「开始游戏」即可。

进存档后按 **F5** 呼出菜单。

> ⚠ 直接点 Steam 的「开始游戏」是原版，不带 Mod。
> ⚠ 启动会多一个控制台窗口，那是 SMAPI，不要关。

---

## 快捷操作

| 按键 | 作用 |
|---|---|
| `F5` | 呼出 / 关闭菜单 |
| `F6` / `F7` | 无限体力 / 无限生命 开关 |
| `F8` / `F9` | 移动速度 +0.1× / −0.1× |
| `Q` / `E` | 切换标签页 |
| `↑↓` / `←→` | 选择 / 调节（可长按加速） |
| `回车` | 执行 |
| `滚轮` | 翻一行 |

---

## 从源码构建

需要 .NET SDK 6.0 或更高（用 8.0 也可以，会向下兼容编译 `net6.0`）。

```powershell
# 指定你的星露谷安装目录
dotnet build src/StardewTrainer/StardewTrainer.csproj -c Release `
  -p:GamePath="D:\steam\steamapps\common\Stardew Valley"
```

也可以用环境变量：

```powershell
$env:STARDEW_GAME_PATH = "D:\steam\steamapps\common\Stardew Valley"
dotnet build src/StardewTrainer/StardewTrainer.csproj -c Release
```

编译完产物在 `dist/StardewTrainer/`，整个文件夹拖进 `Mods` 即可。

编译前请**先装好 SMAPI**——本项目引用游戏目录下的 `StardewModdingAPI.dll` 和
`smapi-internal/SMAPI.Toolkit.CoreInterfaces.dll`，这两个是 SMAPI 安装时放进游戏目录的。

---

## 翻译成其他语言

所有界面文字都在 `i18n/default.json`，格式是 `"键": "文字"`。

1. 复制 `default.json`，改名成语言代码，例如 `en.json`、`ja.json`
2. **只改右边的值**，左边的键不要动
3. 放回同一个 `i18n` 文件夹
4. 重启游戏

加载顺序：`i18n/<游戏语言>.json` → `i18n/default.json` → 键名兜底。
缺哪个键都会自动回落到中文，不会出现空白。`{0}`、`{1}` 是占位符，保留位置即可。

欢迎把翻译 PR 回来。

---

## 项目结构

```
src/StardewTrainer/
├── ModEntry.cs        入口：SMAPI 事件、快捷键、控制台命令
├── TrainerMenu.cs     菜单 UI（标签页、行、输入、绘制）
├── Cheats.cs          实时数值修改逻辑
├── Harvest.cs         一键采集（树 / 石头 / 杂物 / 大障碍）
├── Spawner.cs         物品、工具、怪物、NPC 生成
├── Relationships.cs   NPC 关系、配偶
├── Animals.cs         宠物好感度、农场动物快乐/饱足
├── WeatherControl.cs  天气
├── I18n.cs            文案表
├── Sound.cs           音效
├── ModConfig.cs       配置项
├── manifest.json      SMAPI 清单
└── i18n/default.json  界面文字
```

---

## 说明与免责

- 这是单人游戏的修改器。多人联机时改别人的数据或世界状态可能影响他人体验，请自行斟酌
- 修改只作用于内存，**游戏自己存档时才落盘**（睡觉 / 退出到标题 / `trainer_save`）
- **一键采集、改配偶、改日期都是破坏性操作**，建议先存档再动手
- 使用修改器可能影响成就获取，介意的话请另开存档
- 本项目与 ConcernedApe、SMAPI 官方均无关联；星露谷物语版权归 ConcernedApe 所有

## 许可

[MIT](LICENSE)
