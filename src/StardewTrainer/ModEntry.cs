using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;
using StardewValley.Menus;

namespace StardewTrainer
{
    /// <summary>修改器主入口。</summary>
    public class ModEntry : Mod
    {
        private ModConfig _config = new();
        private CheatState _state = new();
        private Cheats _logic = null!;
        private Spawner _spawner = null!;
        private WeatherControl _weather = null!;
        private Relationships _relations = null!;
        private Animals _animals = null!;
        private Harvest _harvest = null!;
        private TrainerMenu? _menu;
        private string _commandsPath = "";
        private int _tick;
        private bool _baseSpeedCaptured;

        /// <summary>
        /// 世界是否已加载。优先用 SMAPI 的标志，但它在 SaveGame.Load() 这条路径上不会被置位
        /// （trainer_load 就是走这条路），所以再直接看一眼游戏的真实状态作为兜底。
        /// </summary>
        internal static bool WorldReady =>
            Context.IsWorldReady || (Game1.player != null && Game1.currentLocation != null);

        public override void Entry(IModHelper helper)
        {
            this._config = helper.ReadConfig<ModConfig>();
            this._logic = new Cheats(this.Monitor);
            this._spawner = new Spawner(this.Monitor);
            this._weather = new WeatherControl(this.Monitor);
            this._relations = new Relationships(this.Monitor);
            this._animals = new Animals(this.Monitor);
            this._harvest = new Harvest(this.Monitor);
            I18n.Init(helper, this.Monitor);
            Sound.SetMuted(this._config.MuteSounds);

            helper.Events.GameLoop.GameLaunched += this.OnGameLaunched;
            helper.Events.GameLoop.UpdateTicked += this.OnUpdateTicked;
            helper.Events.GameLoop.ReturnedToTitle += this.OnReturnedToTitle;
            helper.Events.Input.ButtonPressed += this.OnButtonPressed;
            helper.Events.Input.ButtonReleased += this.OnButtonReleased;
            helper.Events.Input.MouseWheelScrolled += this.OnMouseWheelScrolled;
            helper.Events.Display.RenderingHud += this.OnRenderingHud;

            helper.ConsoleCommands.Add(
                "trainer",
                "打开星露谷实时修改器菜单。\n\n用法: trainer",
                (cmd, args) => this.ToggleMenu(true));

            helper.ConsoleCommands.Add(
                "trainer_money",
                "设置金钱。\n\n用法: trainer_money <金额>",
                this.CommandSetMoney);

            helper.ConsoleCommands.Add(
                "trainer_speed",
                "设置移动速度倍率（1 = 原速）。\n\n用法: trainer_speed <倍率>",
                this.CommandSetSpeed);

            helper.ConsoleCommands.Add(
                "trainer_item",
                "获取物品。支持数字ID、限定ID 或中文名。\n\n用法: trainer_item <名字或ID> [数量]\n例如: trainer_item 钻石 20",
                this.CommandGiveItem);

            helper.ConsoleCommands.Add(
                "trainer_finditem",
                "按名字搜索物品（确认准确的物品名）。\n\n用法: trainer_finditem <关键词>",
                this.CommandFindItem);

            helper.ConsoleCommands.Add(
                "trainer_monster",
                "在玩家旁边生成怪物。\n\n用法: trainer_monster <类型名> [x偏移]\n例如: trainer_monster GreenSlime",
                this.CommandSpawnMonster);

            helper.ConsoleCommands.Add(
                "trainer_monsters",
                "列出所有可生成的怪物类型名。\n\n用法: trainer_monsters",
                this.CommandListMonsters);

            helper.ConsoleCommands.Add(
                "trainer_npc",
                "在玩家旁边生成一个村民/角色。\n\n用法: trainer_npc <英文名>\n例如: trainer_npc Abigail",
                this.CommandSpawnNpc);

            helper.ConsoleCommands.Add(
                "trainer_clear",
                "清空当前地图上的所有怪物。\n\n用法: trainer_clear",
                (cmd, args) => this._spawner.ClearMonsters());

            helper.ConsoleCommands.Add(
                "trainer_clearnpc",
                "删除修改器生成的角色（不会动游戏原有 NPC 和宠物）。\n\n用法: trainer_clearnpc",
                (cmd, args) => this._spawner.ClearSpawnedVillagers());

            helper.ConsoleCommands.Add(
                "trainer_weather",
                "改天气。\n\n用法: trainer_weather <sunny|rain|storm|snow|wind|green> [tomorrow]\n" +
                "例如: trainer_weather rain        立刻下雨\n" +
                "      trainer_weather storm tomorrow   今天和明天都是雷雨",
                this.CommandWeather);

            helper.ConsoleCommands.Add(
                "trainer_tp",
                "把某个 NPC 传送到自己身边。\n\n用法: trainer_tp <英文名>\n例如: trainer_tp Robin",
                (cmd, args) => this._spawner.TeleportNpcByName(args.Length > 0 ? args[0] : ""));

            helper.ConsoleCommands.Add(
                "trainer_load",
                "载入第 N 个存档（用于脚本化/排错，不用手点标题菜单）。\n\n" +
                "用法: trainer_load [序号]\n不带参数时只列出所有存档。",
                this.CommandLoadSave);

            helper.ConsoleCommands.Add(
                "trainer_selftest",
                "自检：程序化验证长按调节和滚轮翻页（排错用）。\n\n用法: trainer_selftest",
                this.CommandSelfTest);

            helper.ConsoleCommands.Add(
                "trainer_time",
                "直接设置游戏时间。\n\n用法: trainer_time <时间>\n例如: trainer_time 1800  (= 18:00)",
                this.CommandSetTime);

            helper.ConsoleCommands.Add(
                "trainer_date",
                "直接设置日期。\n\n用法: trainer_date <年> <季 0-3> <日 1-28>\n" +
                "例如: trainer_date 2 1 15   (= 第2年 夏季 15日)",
                this.CommandSetDate);

            helper.ConsoleCommands.Add(
                "trainer_save",
                "立刻保存游戏（等价于在游戏菜单里退出到标题）。\n\n用法: trainer_save",
                this.CommandSave);

            helper.ConsoleCommands.Add(
                "trainer_house",
                "设置房子升级等级（0-3）。结婚需要至少 1 级。\n\n用法: trainer_house <0-3>",
                this.CommandHouse);

            helper.ConsoleCommands.Add(
                "trainer_toolcheck",
                "核对工具表：逐个用游戏数据反查名字和升级等级。\n\n用法: trainer_toolcheck",
                (cmd, args) =>
                {
                    this.Monitor.Log("===== 工具表核对 =====", LogLevel.Info);
                    foreach (string line in this._spawner.AuditTools())
                        this.Monitor.Log(line, LogLevel.Info);
                    this.Monitor.Log("===== 核对结束 =====", LogLevel.Info);
                });

            helper.ConsoleCommands.Add(
                "trainer_tools",
                "获取全部工具。\n\n用法: trainer_tools",
                (cmd, args) => this.Monitor.Log(
                    $"已获得 {this._spawner.GiveAllTools()} 件工具（已有的跳过）。", LogLevel.Info));

            helper.ConsoleCommands.Add(
                "trainer_cut",
                "一键砍树 / 清障碍。用法: trainer_cut | trainer_cut clump | trainer_cut all",
                (cmd, args) =>
                {
                    if (args.Length >= 1 && args[0] == "all")
                    {
                        (int t, int cl, int d) = this._harvest.ClearAll();
                        this.Monitor.Log($"一键采集：砍树 {t} 棵，清大障碍 {cl} 个，清石头杂草 {d} 个。", LogLevel.Info);
                    }
                    else if (args.Length >= 1 && args[0] == "clump")
                        this.Monitor.Log($"清掉大障碍 {this._harvest.ClearClumps()} 个。", LogLevel.Info);
                    else
                        this.Monitor.Log($"砍掉 {this._harvest.CutAllTrees()} 棵树。", LogLevel.Info);
                });

            helper.ConsoleCommands.Add(
                "trainer_itemcheck",
                "核对菜单里的物品表：逐个用游戏数据反查真实名字，列出对不上的。\n\n用法: trainer_itemcheck",
                (cmd, args) =>
                {
                    this.Monitor.Log("===== 常用物品表核对（菜单名 vs 游戏实际）=====", LogLevel.Info);
                    foreach (string line in this._spawner.AuditCommonItems())
                        this.Monitor.Log(line, LogLevel.Info);
                    this.Monitor.Log("===== 核对结束（[!!] 的就是对不上的）=====", LogLevel.Info);
                });

            helper.ConsoleCommands.Add(
                "trainer_pet",
                "把宠物好感度拉满。\n\n用法: trainer_pet",
                (cmd, args) => this.Monitor.Log(
                    $"宠物好感度：已处理 {this._animals.MaxPetFriendship()} 只。", LogLevel.Info));

            helper.ConsoleCommands.Add(
                "trainer_animal",
                "把农场动物的快乐度和饱足度拉满。\n\n用法: trainer_animal",
                (cmd, args) => this.Monitor.Log(
                    $"农场动物：快乐度 {this._animals.MaxAnimalHappiness()} 只，" +
                    $"饱足度 {this._animals.MaxAnimalFullness()} 只。", LogLevel.Info));

            helper.ConsoleCommands.Add(
                "trainer_friendship",
                "把所有 NPC 的好感度拉满。\n\n用法: trainer_friendship",
                (cmd, args) => this.Monitor.Log(
                    $"已把 {this._logic.MaxAllFriendships()} 个 NPC 的好感度拉满。", LogLevel.Info));

            helper.ConsoleCommands.Add(
                "trainer_npcs",
                "列出这个世界里所有 NPC 的英文名（设置配偶/传送时用）。\n\n用法: trainer_npcs",
                (cmd, args) =>
                {
                    List<string> names = this._relations.AllNpcNames();
                    this.Monitor.Log($"共 {names.Count} 个 NPC：{string.Join(", ", names)}", LogLevel.Info);
                });

            helper.ConsoleCommands.Add(
                "trainer_spouse",
                "把某个村民设成你的配偶（结婚）。\n\n用法: trainer_spouse <英文名>\n" +
                "例如: trainer_spouse Abigail\n不带参数时列出当前配偶和可结婚对象。",
                this.CommandSpouse);

            helper.ConsoleCommands.Add(
                "trainer_echo",
                "回显参数（测试控制台输入用）。\n\n用法: trainer_echo <任意内容>",
                (cmd, args) => this.Monitor.Log(
                    "echo: argc=" + args.Length + " args=[" + string.Join("|", args) + "]", LogLevel.Info));

            helper.ConsoleCommands.Add(
                "trainer_debug",
                "打印当前修改器状态和玩家实时数值（排错用）。\n\n用法: trainer_debug",
                this.CommandDebug);

            // 文件命令通道：Mods\StardewTrainer\commands.txt，每行一条命令，游戏内自动执行
            try
            {
                this._commandsPath = Path.Combine(helper.DirectoryPath, "commands.txt");
                string readme = Path.Combine(helper.DirectoryPath, "commands说明.txt");
                // 版本标记：内容变了就重写，避免旧说明留着误导人
                string marker = "VERSION:1.1";
                bool stale = !File.Exists(readme)
                             || !File.ReadAllText(readme).Contains(marker, StringComparison.Ordinal);
                if (stale)
                {
                    string[] help =
                    {
                        marker,
                        "把命令写进 commands.txt（每行一条，写法与 SMAPI 控制台相同），游戏内每 0.5 秒检查一次并自动执行。",
                        "以 # 开头的行会被忽略；执行完的文件会重命名为 commands.txt.done。",
                        "",
                        "可用命令：",
                        "  trainer_debug               打印当前状态和游戏里的实际数值",
                        "  trainer_money 500000        设置金钱",
                        "  trainer_speed 3             移动速度 3 倍（1 = 原速）",
                        "  trainer_stamina 1           无限体力开（0 = 关）",
                        "  trainer_health 1            无限生命开（0 = 关）",
                        "  trainer_item Diamond 20     获取物品（英文名或数字ID，如 72）",
                        "  trainer_finditem Diamond    按名字搜索物品",
                        "  trainer_monster GreenSlime  在身旁刷怪",
                        "  trainer_monsters            列出所有可生成的怪物类型",
                        "  trainer_npc Abigail         生成村民",
                        "  trainer_clear               清空当前地图怪物",
                        "  trainer_spew 1              打开速度诊断日志（排错用，平时别开）"
                    };
                    File.WriteAllLines(readme, help);
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("初始化 commands.txt 通道失败：" + ex.Message, LogLevel.Warn);
            }

            this.Monitor.Log("星露谷实时修改器已加载。按 " + this._config.MenuKey + " 呼出菜单。", LogLevel.Info);
        }

        // ------------------------------------------------------------------
        // 生命周期
        // ------------------------------------------------------------------
        private void OnGameLaunched(object? sender, GameLaunchedEventArgs e)
        {
            this.RemoveConflictingMods();

            if (this._config.ShowHintsOnLoad)
            {
                this.Monitor.Log(
                    $"操作提示：{this._config.MenuKey} 呼出菜单；" +
                    $"{this._config.InfiniteStaminaKey} 无限体力；" +
                    $"{this._config.InfiniteHealthKey} 无限生命；" +
                    $"{this._config.SpeedUpKey}/{this._config.SpeedDownKey} 调速。",
                    LogLevel.Info);
            }
        }

        /// <summary>如果检测到 CJB 作弊菜单之类的冲突模组，提示一下（不自动删除别人的东西）。</summary>
        private void RemoveConflictingMods()
        {
            string[] known =
            {
                "CJB Cheats Menu", "CJBCheatsMenu", "TrainerMod", "StardewValleyTrainer"
            };
            List<IManifest> found = new();
            foreach (IModInfo mod in this.Helper.ModRegistry.GetAll())
            {
                foreach (string name in known)
                {
                    if (mod.Manifest.UniqueID.Contains(name, StringComparison.OrdinalIgnoreCase)
                        || (mod.Manifest.Name ?? "").Contains(name, StringComparison.OrdinalIgnoreCase))
                    {
                        found.Add(mod.Manifest);
                        break;
                    }
                }
            }

            if (found.Count > 0)
            {
                foreach (IManifest m in found)
                    this.Monitor.Log($"检测到同类功能模组：{m.Name} ({m.UniqueID})，可能和本修改器的菜单快捷键冲突。", LogLevel.Warn);
            }
        }

        private void OnReturnedToTitle(object? sender, ReturnedToTitleEventArgs e)
        {
            this.ToggleMenu(false);
            this._state = new CheatState();
        }

        // ------------------------------------------------------------------
        // 每 tick 实时修改
        // ------------------------------------------------------------------
        private void OnUpdateTicked(object? sender, UpdateTickedEventArgs e)
        {
            // 长按加速调节：菜单开着时游戏是暂停的，所以这里必须在 EarlyReturn 之前调用
            this._menu?.Tick();

            // 文件命令通道：标题界面也要能用（trainer_load 就是在这个阶段用的）
            this._tick++;
            if (this._tick % 30 == 0)
                this.PollCommandFile();

            if (!ModEntry.WorldReady || Game1.player == null)
                return;

            // 如果菜单被游戏强制关掉（例如玩家进了节日/事件），同步状态
            if (this._menu != null && this._menu.PausesGame
                && !ReferenceEquals(Game1.activeClickableMenu, this._menu))
            {
                this._menu = null;
            }

            // 记录基础速度（只记一次）——注意 _tick 从标题界面就在累加，
            // 所以这里改用「是否已记录」来判断，不能再用 _tick == 1
            if (!this._baseSpeedCaptured)
            {
                this._state.BaseSpeed = Game1.player.Speed;
                this._state.TeleportTileX = (int)Game1.player.Tile.X;
                this._state.TeleportTileY = (int)Game1.player.Tile.Y;
                this._baseSpeedCaptured = true;
            }

            try
            {
                this._logic.Apply(this._state);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("应用修改时出错：" + ex, LogLevel.Error);
            }

            // 一击必杀 / 怪物不刷新：每 30 tick 清一次，避免每帧遍历地图
            if ((this._state.OneHitKill || this._state.NoMonsters) && this._tick % 30 == 0)
            {
                try
                {
                    this._logic.KillMonsters();
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce("清理怪物时出错：" + ex.Message, LogLevel.Error);
                }
            }

            // 时间流速
            if (Math.Abs(this._state.TimeScale - 1f) > 0.001f && !Game1.paused && !Game1.eventUp)
            {
                try
                {
                    if (this._state.TimeScale > 1f)
                    {
                        int extra = (int)(Game1.gameTimeInterval * (this._state.TimeScale - 1f));
                        if (extra > 0 && Game1.gameTimeInterval < 60000)
                            Game1.gameTimeInterval += extra;
                    }
                    else
                    {
                        Game1.gameTimeInterval = Math.Max(0, (int)(Game1.gameTimeInterval * this._state.TimeScale));
                    }
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce("时间调速失败：" + ex.Message, LogLevel.Warn);
                    this._state.TimeScale = 1f;
                }
            }

            // 工具瞬间蓄力
            if (this._state.InstantCharge && Game1.player.UsingTool)
            {
                try
                {
                    this.FastCharge();
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce("瞬间蓄力失败：" + ex.Message, LogLevel.Warn);
                    this._state.InstantCharge = false;
                }
            }

            // 每日运气
            if (this._state.DailyLuckPercent > 0 && e.IsMultipleOf(60))
            {
                try
                {
                    double target = this._state.DailyLuckPercent / 100.0;
                    if (Game1.player.team.sharedDailyLuck.Value < target)
                        Game1.player.team.sharedDailyLuck.Value = target;
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce("每日运气修改失败：" + ex.Message, LogLevel.Warn);
                    this._state.DailyLuckPercent = 0;
                }
            }
        }

        /// <summary>工具蓄力瞬间拉满。</summary>
        private void FastCharge()
        {
            Farmer player = Game1.player;
            if (player.toolPower.Value < 3)
                player.toolPower.Value = 3;
            player.toolHold.Value = 0;

            System.Reflection.FieldInfo? holdStart =
                typeof(Farmer).GetField("toolHoldStartTime",
                    System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.Public
                    | System.Reflection.BindingFlags.NonPublic);
            holdStart?.SetValue(player, 0);
        }

        // ------------------------------------------------------------------
        // 输入
        // ------------------------------------------------------------------
        private void OnButtonPressed(object? sender, ButtonPressedEventArgs e)
        {
            if (!ModEntry.WorldReady)
                return;

            // 菜单快捷键：随时可用
            if (this._config.MenuKey.JustPressed())
            {
                this.ToggleMenu(this._menu == null);
                this.Helper.Input.Suppress(e.Button);
                return;
            }

            if (this._menu != null)
            {
                switch (e.Button)
                {
                    case SButton.Up:
                    case SButton.W:
                        this._menu.MoveSelection(-1);
                        this.Helper.Input.Suppress(e.Button);
                        return;
                    case SButton.Down:
                    case SButton.S:
                        this._menu.MoveSelection(1);
                        this.Helper.Input.Suppress(e.Button);
                        return;
                    case SButton.Left:
                    case SButton.A:
                        this._menu.BeginHold(-1);          // 长按会持续调小
                        this.Helper.Input.Suppress(e.Button);
                        return;
                    case SButton.Right:
                    case SButton.D:
                        this._menu.BeginHold(1);           // 长按会持续调大
                        this.Helper.Input.Suppress(e.Button);
                        return;
                    case SButton.Enter:
                    case SButton.Space:
                        this._menu.ActivateSelection();
                        this.Helper.Input.Suppress(e.Button);
                        return;
                    case SButton.Escape:
                        this.ToggleMenu(false);
                        this.Helper.Input.Suppress(e.Button);
                        return;
                    case SButton.Q:
                    case SButton.LeftShift:
                        this._menu.PrevTab();
                        this.Helper.Input.Suppress(e.Button);
                        return;
                    case SButton.E:
                    case SButton.RightShift:
                        this._menu.NextTab();
                        this.Helper.Input.Suppress(e.Button);
                        return;
                    case SButton.Tab:
                        this._menu.NextTab();
                        this.Helper.Input.Suppress(e.Button);
                        return;
                    case SButton.MouseLeft:
                        this._menu.ClickAt((int)Game1.getMouseX(), (int)Game1.getMouseY());
                        this.Helper.Input.Suppress(e.Button);
                        return;
                }
                return;
            }

            // 菜单关闭时的快捷键
            if (this._config.InfiniteStaminaKey.JustPressed())
            {
                this._state.InfiniteStamina = !this._state.InfiniteStamina;
                this.Notify($"无限体力：{(this._state.InfiniteStamina ? "开" : "关")}");
                this.Helper.Input.Suppress(e.Button);
            }
            else if (this._config.InfiniteHealthKey.JustPressed())
            {
                this._state.InfiniteHealth = !this._state.InfiniteHealth;
                this.Notify($"无限生命：{(this._state.InfiniteHealth ? "开" : "关")}");
                this.Helper.Input.Suppress(e.Button);
            }
            else if (this._config.SpeedUpKey.JustPressed())
            {
                this._state.SpeedMultiplier = Math.Clamp(
                    this._state.SpeedMultiplier + this._config.SpeedStep, 1f, this._config.MaxSpeedMultiplier);
                this.Notify($"移动速度：×{this._state.SpeedMultiplier:0.0}");
                this.Helper.Input.Suppress(e.Button);
            }
            else if (this._config.SpeedDownKey.JustPressed())
            {
                this._state.SpeedMultiplier = Math.Clamp(
                    this._state.SpeedMultiplier - this._config.SpeedStep, 1f, this._config.MaxSpeedMultiplier);
                this.Notify($"移动速度：×{this._state.SpeedMultiplier:0.0}");
                this.Helper.Input.Suppress(e.Button);
            }
        }

        /// <summary>松开方向键就停止长按重复。</summary>
        private void OnButtonReleased(object? sender, ButtonReleasedEventArgs e)
        {
            if (this._menu == null)
                return;
            switch (e.Button)
            {
                case SButton.Left:
                case SButton.Right:
                case SButton.A:
                case SButton.D:
                    this._menu.EndHold();
                    break;
            }
        }

        /// <summary>菜单开着时用滚轮翻页。</summary>
        private void OnMouseWheelScrolled(object? sender, MouseWheelScrolledEventArgs e)
        {
            if (this._menu == null)
                return;

            this._menu.ScrollBy(e.Delta > 0 ? -1 : 1);
            this.Helper.Input.SuppressScrollWheel();
        }

        private void Notify(string message)
        {
            try
            {
                if (Game1.hudMessages != null && Game1.hudMessages.Count < 5)
                    Game1.addHUDMessage(new HUDMessage(message, HUDMessage.newQuest_type));
            }
            catch
            {
                // 忽略：HUD 提示失败不影响功能
            }
        }

        /// <summary>打开 / 关闭菜单。</summary>
        private void ToggleMenu(bool open)
        {
            if (open)
            {
                if (!ModEntry.WorldReady || Game1.player == null)
                    return;
                if (this._menu != null)
                    return;

                this._menu = new TrainerMenu(this._config, this._state, this._logic, this._spawner,
                                            this._weather, this._relations, this._animals,
                                            this._harvest,
                                            this.ModManifest.Version.ToString());
                if (this._menu.PausesGame)
                    Game1.activeClickableMenu = this._menu;
            }
            else
            {
                if (this._menu == null)
                    return;
                if (ReferenceEquals(Game1.activeClickableMenu, this._menu))
                    Game1.activeClickableMenu = null;
                this._menu = null;
            }
        }

        // ------------------------------------------------------------------
        // 控制台命令（方便排错和脚本化）
        // ------------------------------------------------------------------
        private void CommandSetMoney(string command, string[] args)
        {
            if (!ModEntry.WorldReady || Game1.player == null)
            {
                this.Monitor.Log("请先进入存档再执行这个命令。", LogLevel.Warn);
                return;
            }
            if (args.Length < 1 || !int.TryParse(args[0], out int amount))
            {
                this.Monitor.Log("用法: trainer_money <金额>", LogLevel.Info);
                return;
            }
            int before = Game1.player.Money;
            // 不设上限：存档里可能已经有超限值（别的修改器写入的），必须允许改小来修回来
            Game1.player.Money = Math.Max(0, amount);
            if (Game1.player.totalMoneyEarned < (uint)Game1.player.Money)
                Game1.player.totalMoneyEarned = (uint)Game1.player.Money;
            this.Monitor.Log($"金钱: {before:N0} -> {Game1.player.Money:N0}", LogLevel.Info);
        }

        private void CommandSetSpeed(string command, string[] args)
        {
            if (!ModEntry.WorldReady)
            {
                this.Monitor.Log("请先进入存档再执行这个命令。", LogLevel.Warn);
                return;
            }
            if (args.Length < 1 || !float.TryParse(args[0], out float mult))
            {
                this.Monitor.Log("用法: trainer_speed <倍率，例如 3>", LogLevel.Info);
                return;
            }
            this._state.SpeedMultiplier = Math.Clamp(mult, 1f, this._config.MaxSpeedMultiplier);
            this.Monitor.Log($"移动速度倍率: ×{this._state.SpeedMultiplier:0.0}", LogLevel.Info);
        }

        private void CommandGiveItem(string command, string[] args)
        {
            if (!ModEntry.WorldReady)
            {
                this.Monitor.Log("请先进入存档再执行这个命令。", LogLevel.Warn);
                return;
            }
            if (args.Length < 1)
            {
                this.Monitor.Log("用法: trainer_item <名字或ID> [数量]\n例如: trainer_item 钻石 20", LogLevel.Info);
                return;
            }

            // 最后一个参数如果是纯数字，就当数量；物品名允许带空格
            string name = string.Join(" ", args);
            int count = 1;
            if (args.Length >= 2 && int.TryParse(args[^1], out int parsed))
            {
                count = Math.Clamp(parsed, 1, 999);
                name = string.Join(" ", args.Take(args.Length - 1));
            }

            this._spawner.GiveItem(name, count);
        }

        private void CommandFindItem(string command, string[] args)
        {
            if (args.Length < 1)
            {
                this.Monitor.Log("用法: trainer_finditem <关键词>\n例如: trainer_finditem 种子", LogLevel.Info);
                return;
            }
            string keyword = string.Join(" ", args);
            var hits = this._spawner.SearchItems(keyword, 20);
            if (hits.Count == 0)
            {
                this.Monitor.Log($"没找到包含「{keyword}」的物品。", LogLevel.Info);
                return;
            }
            this.Monitor.Log($"包含「{keyword}」的物品（{hits.Count} 个）：", LogLevel.Info);
            foreach (var (id, name) in hits)
                this.Monitor.Log($"   {name}   ->   {id}", LogLevel.Info);
        }

        private void CommandSpawnMonster(string command, string[] args)
        {
            if (args.Length < 1)
            {
                this.Monitor.Log("用法: trainer_monster <类型名> [x偏移]\n类型名见 trainer_monsters", LogLevel.Info);
                return;
            }
            int offset = 2;
            if (args.Length >= 2)
                int.TryParse(args[1], out offset);
            this._spawner.SpawnMonster(args[0], Math.Clamp(offset, -20, 20));
        }

        private void CommandListMonsters(string command, string[] args)
        {
            List<string> types = this._spawner.ListMonsterTypes();
            this.Monitor.Log($"可生成的怪物类型（{types.Count} 个）：", LogLevel.Info);
            foreach (string t in types)
                this.Monitor.Log("   " + t, LogLevel.Info);
        }

        private void CommandSpawnNpc(string command, string[] args)
        {
            if (args.Length < 1)
            {
                this.Monitor.Log("用法: trainer_npc <英文名>\n例如: trainer_npc Abigail", LogLevel.Info);
                return;
            }
            this._spawner.SpawnVillager(args[0]);
        }

        /// <summary>
        /// 载入第 N 个存档。直接用游戏自己的 SaveGame.Load(存档文件夹名)，
        /// 和标题界面点「加载」是同一套流程。主要给脚本化/排错用。
        /// </summary>
        private void CommandLoadSave(string command, string[] args)
        {
            if (ModEntry.WorldReady)
            {
                this.Monitor.Log("已经在存档里了（要先退回标题才能载入别的存档）。", LogLevel.Warn);
                return;
            }

            try
            {
                string dir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "StardewValley", "Saves");
                List<string> folders = Directory.Exists(dir)
                    ? Directory.GetDirectories(dir)
                        .Select(Path.GetFileName)
                        .Where(n => !string.IsNullOrEmpty(n)
                                    && !n!.StartsWith("_backup_", StringComparison.Ordinal))
                        .Select(n => n!)
                        .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                        .ToList()
                    : new List<string>();

                if (args.Length < 1)
                {
                    this.Monitor.Log($"共 {folders.Count} 个存档：", LogLevel.Info);
                    for (int i = 0; i < folders.Count; i++)
                        this.Monitor.Log($"   [{i}] {folders[i]}", LogLevel.Info);
                    this.Monitor.Log("用法: trainer_load <序号>", LogLevel.Info);
                    return;
                }

                if (!int.TryParse(args[0], out int index) || index < 0 || index >= folders.Count)
                {
                    this.Monitor.Log($"序号不对，应该在 0..{Math.Max(0, folders.Count - 1)} 之间。", LogLevel.Warn);
                    return;
                }

                string target = folders[index];
                this.Monitor.Log($"正在载入存档 [{index}] {target} ...", LogLevel.Info);
                SaveGame.Load(target);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"载入存档失败：{ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>
        /// 自检：程序化地验证「长按加速调节」和「滚轮翻页」两条代码路径，
        /// 不依赖真实按键/鼠标（给自动化测试和排错用）。
        /// </summary>
        private void CommandSelfTest(string command, string[] args)
        {
            if (!ModEntry.WorldReady)
            {
                this.Monitor.Log("请先进入存档再自检。", LogLevel.Warn);
                return;
            }

            this.ToggleMenu(true);
            if (this._menu == null)
            {
                this.Monitor.Log("自检失败：菜单打不开。", LogLevel.Warn);
                return;
            }

            this.Monitor.Log("===== 修改器自检 =====", LogLevel.Info);

            // ---- 长按加速调节 ----
            float before = this._state.SpeedMultiplier;
            bool selected = this._menu.SelectRowByLabelContains("移动速度倍率");
            this._menu.BeginHold(1);
            for (int i = 0; i < 150; i++)      // 模拟按住 150 tick ≈ 2.5 秒
                this._menu.Tick();
            this._menu.EndHold();
            float after = this._state.SpeedMultiplier;
            this.Monitor.Log(
                $"长按调节：选中={selected}，按住 150 tick，速度倍率 ×{before:0.00} -> ×{after:0.00} " +
                (after > before + 0.2f ? "OK" : "失败"), LogLevel.Info);

            // ---- 松手后必须停止 ----
            float paused = this._state.SpeedMultiplier;
            for (int i = 0; i < 60; i++)
                this._menu.Tick();
            this.Monitor.Log(
                $"松手后停止：×{paused:0.00} -> ×{this._state.SpeedMultiplier:0.00} " +
                (Math.Abs(this._state.SpeedMultiplier - paused) < 0.001f ? "OK" : "失败"), LogLevel.Info);

            // ---- 滚轮翻页 ----
            this._menu.SelectRowByLabelContains("移动速度倍率");
            int rowBefore = this._menu.SelectedIndex;
            this._menu.ScrollBy(1);
            int rowDown = this._menu.SelectedIndex;
            this._menu.ScrollBy(-1);
            int rowUp = this._menu.SelectedIndex;
            this.Monitor.Log(
                $"滚轮翻页：行 {rowBefore} -下滚-> {rowDown} -上滚-> {rowUp} " +
                (rowDown != rowBefore ? "OK" : "失败（没有移动）"), LogLevel.Info);
            this.Monitor.Log($"当前选中行：{this._menu.SelectedLabel}", LogLevel.Info);

            // ---- 时间：直接设置 ----
            int clockBefore = Game1.timeOfDay;
            this._logic.SetClock(18, 30);
            this.Monitor.Log(
                $"设置时间：{clockBefore:D4} -> {Game1.timeOfDay:D4} " +
                (Game1.timeOfDay == 1830 ? "OK" : "失败（期望 1830）"), LogLevel.Info);

            // ---- 时间：按分钟推进 ----
            int t0 = Game1.timeOfDay;
            this._logic.AddMinutes(70);
            this.Monitor.Log(
                $"时间 +70 分钟：{t0:D4} -> {Game1.timeOfDay:D4} " +
                (Game1.timeOfDay == 1940 ? "OK" : "失败（期望 1940）"), LogLevel.Info);

            // ---- 时间：跨天边界应被夹到 26:00 ----
            this._logic.SetClock(25, 30);
            this._logic.AddMinutes(120);
            this.Monitor.Log(
                $"跨天钳制：25:30 +120 分钟 -> {Game1.timeOfDay:D4} " +
                (Game1.timeOfDay == 2600 ? "OK（夹到 26:00）" : "失败（期望 2600）"), LogLevel.Info);

            // ---- 时间：回退越过 06:00 应被拒绝 ----
            this._logic.SetClock(6, 0);
            int rejected = this._logic.AddMinutes(-60);
            this.Monitor.Log(
                $"回退下限：06:00 -60 分钟 -> {(rejected < 0 ? "被拒绝" : rejected.ToString())} " +
                (rejected < 0 ? "OK" : "失败（应该拒绝）"), LogLevel.Info);

            // ---- 日期 ----
            int y0 = Game1.year;
            string s0 = Game1.currentSeason;
            int d0 = Game1.dayOfMonth;
            Game1.year = 2;
            Game1.currentSeason = "summer";
            Game1.dayOfMonth = 15;
            this.Monitor.Log(
                $"设置日期：{y0}年{s0}{d0}日 -> {Game1.year}年{Game1.currentSeason}{Game1.dayOfMonth}日 " +
                (Game1.year == 2 && Game1.currentSeason == "summer" && Game1.dayOfMonth == 15 ? "OK" : "失败"),
                LogLevel.Info);
            Game1.year = y0;
            Game1.currentSeason = s0;
            Game1.dayOfMonth = d0;

            // ---- 滚轮步长应该变小（不再一屏一屏跳）----
            this._menu.SelectRowByLabelContains("移动速度倍率");
            int r0 = this._menu.SelectedIndex;
            this._menu.ScrollBy(1);
            int r1 = this._menu.SelectedIndex;
            int moved = Math.Abs(r1 - r0);
            this.Monitor.Log(
                $"滚轮步长：一次滚动移动 {moved} 行 " +
                (moved <= 4 ? "OK（比原来的一整屏小多了）" : "失败（还是太快）"), LogLevel.Info);

            // ---- 配偶：设成任意 NPC，并验证"换人时上一任被解除" ----
            string? spouse0 = this._relations.CurrentSpouse();
            this.Monitor.Log($"配偶自检：初始 = {spouse0 ?? "（无）"}", LogLevel.Info);

            int npcCount = this._relations.NpcCount();
            this.Monitor.Log($"配偶自检：世界里的 NPC 数量 = {npcCount}", LogLevel.Info);

            bool m1 = this._relations.Marry("Abigail");
            string? s1 = this._relations.CurrentSpouse();
            this.Monitor.Log(
                $"配偶自检：设为 Abigail -> {s1 ?? "null"} " +
                (m1 && s1 == "Abigail" && Game1.player.getSpouse() != null ? "OK" : "失败"), LogLevel.Info);

            // 换人：上一任应该自动变成离异
            bool m2 = this._relations.Marry("Penny");
            string? s2 = this._relations.CurrentSpouse();
            bool abigailDivorced = Game1.player.friendshipData.TryGetValue("Abigail", out Friendship? af)
                                   && af != null
                                   && af.Status == FriendshipStatus.Divorced;
            this.Monitor.Log(
                $"配偶自检：改设 Penny -> {s2 ?? "null"}，Abigail 状态 = " +
                $"{(abigailDivorced ? "Divorced" : "未解除")} " +
                (m2 && s2 == "Penny" && abigailDivorced ? "OK（上一任已自动解除）" : "失败"),
                LogLevel.Info);

            this.Monitor.Log($"配偶自检：getSpouse() = {Game1.player.getSpouse()?.Name ?? "null"}，" +
                             $"isMarriedOrRoommates() = {Game1.player.isMarriedOrRoommates()}", LogLevel.Info);

            // 自检不该改变玩家的实际婚姻状态：把配偶恢复成原来的样子
            if (!string.IsNullOrEmpty(spouse0))
            {
                this._relations.Marry(spouse0);
                this.Monitor.Log($"配偶自检：已把配偶恢复成 {spouse0}", LogLevel.Info);
            }

            this.Monitor.Log($"天气读取：{this._weather.Describe()}", LogLevel.Info);

            // ---- 新功能：浇水器 / 钓鱼 / 好感度 ----
            this.Monitor.Log($"新版功能自检：无限浇水={this._state.InfiniteWater} " +
                             $"瞬间咬钩={this._state.InstantBite} 瞬间捕获={this._state.InstantCatch} " +
                             $"钓鱼难度归零={this._state.EasyFishing}", LogLevel.Info);

            // 水壶：临时开一下无限水，看看能不能把水量灌满
            try
            {
                bool hadWater = this._state.InfiniteWater;
                this._state.InfiniteWater = true;
                this._logic.Apply(this._state);
                this._state.InfiniteWater = hadWater;

                if (Game1.player.CurrentTool is StardewValley.Tools.WateringCan can)
                    this.Monitor.Log(
                        $"浇水器自检：当前手持水壶，水量 {can.WaterLeft}/{can.waterCanMax}，" +
                        $"无限水标志={can.IsBottomless}", LogLevel.Info);
                else
                    this.Monitor.Log("浇水器自检：当前没拿水壶（拿上水壶再试）", LogLevel.Info);
            }
            catch (Exception ex)
            {
                this.Monitor.Log("浇水器自检失败：" + ex.Message, LogLevel.Warn);
            }

            // 好感度：拉满并报告改了多少人
            int friends = this._logic.MaxAllFriendships();
            this.Monitor.Log($"好感度自检：已把 {friends} 个 NPC 的好感度拉到 {Cheats.MaxFriendshipPoints}", LogLevel.Info);

            // ---- 宠物 / 农场动物 ----
            int petCount = this._animals.AllPets().Count;
            int animalCount = this._animals.AllFarmAnimals().Count;
            this.Monitor.Log($"宠物自检：宠物 {petCount} 只，农场动物 {animalCount} 只", LogLevel.Info);
            this.Monitor.Log(
                $"宠物自检：好感度拉满 {this._animals.MaxPetFriendship()} 只，" +
                $"动物快乐度 {this._animals.MaxAnimalHappiness()} 只，" +
                $"饱足度 {this._animals.MaxAnimalFullness()} 只", LogLevel.Info);

            // ---- 文案表 ----
            this.Monitor.Log($"文案自检：当前语言文件 = {I18n.LoadedFile}，" +
                             $"标签页 = {string.Join(" / ", new[] { I18n.T("tab.self"), I18n.T("tab.npc"), I18n.T("tab.world"), I18n.T("tab.item"), I18n.T("tab.about") })}",
                LogLevel.Info);
            this.Monitor.Log($"文案自检：缺键检查（应为键名本身才说明漏了）" +
                             $"[title={I18n.T("title")}] [hint={I18n.T("hint.bar").Length} 字]", LogLevel.Info);

            // 日期格式：确认渲染出来是「第 2 年 夏季 17 日」这种写全的，而不是「2 夏 17」
            string rendered = I18n.T("unit.date_full_fmt",
                                     I18n.T("unit.year_fmt", Game1.year),
                                     I18n.Season(Game1.currentSeason),
                                     I18n.T("unit.day_fmt", Game1.dayOfMonth));
            this.Monitor.Log($"日期格式自检：游戏当前日期显示为「{rendered}」", LogLevel.Info);

            // ---- 菜单标签页 ----
            bool tabsOk = this._menu.SelectTabByName("NPC");
            this.Monitor.Log($"菜单自检：切到 NPC 页 = {this._menu.ActiveTab}（{tabsOk}），" +
                             $"行数 = {this._menu.RowCount}", LogLevel.Info);
            this._menu.SelectTabByName("关于");
            this.Monitor.Log($"菜单自检：切到关于页，版本显示 = v{this.ModManifest.Version}", LogLevel.Info);
            this._menu.SelectTabByName("自身");
            this.Monitor.Log($"菜单自检：回到自身页，行数 = {this._menu.RowCount}", LogLevel.Info);

            this._state.SpeedMultiplier = 1f;
            this.ToggleMenu(false);
            this.Monitor.Log("===== 自检结束 =====", LogLevel.Info);
        }

        /// <summary>
        /// 直接设置游戏时钟。timeOfDay 的格式是「小时 * 100 + 分钟」，比如 1830 = 18:30。
        /// 游戏内部时间只有 10 分钟粒度，所以分钟会被对齐到 10 的倍数。
        /// </summary>
        private void CommandSetTime(string command, string[] args)
        {
            if (!ModEntry.WorldReady)
            {
                this.Monitor.Log("请先进入存档。", LogLevel.Warn);
                return;
            }
            if (args.Length < 1)
            {
                this.Monitor.Log("用法: trainer_time <时间>\n例如: trainer_time 1800  (= 18:00)\n" +
                                 "      trainer_time 6:30 也可以", LogLevel.Info);
                return;
            }

            string raw = args[0].Replace(":", "");
            if (!int.TryParse(raw, out int value))
            {
                this.Monitor.Log($"看不懂的时间「{args[0]}」，请用 1800 或 18:00 这种写法。", LogLevel.Warn);
                return;
            }

            int hour = value / 100;
            int minute = value % 100;
            if (hour > 26 && value <= 2400)
            {
                // 玩家可能写 "630" 表示 6:30
                hour = value / 100;
                minute = value % 100;
            }
            int before = Game1.timeOfDay;
            this.ApplyClock(hour, minute);
            this.Monitor.Log($"时间：{FormatClock(before)} -> {FormatClock(Game1.timeOfDay)}", LogLevel.Info);
        }

        /// <summary>把时钟设到指定小时/分钟，并同步过场用的时间。</summary>
        private void ApplyClock(int hour, int minute)
        {
            hour = Math.Clamp(hour, 6, 26);              // 6:00 到次日 2:00
            minute = Math.Clamp(minute / 10 * 10, 0, 50); // 只有 10 分钟粒度
            int newTime = (hour >= 24 ? hour : hour) * 100 + minute;

            Game1.timeOfDay = newTime;
            Game1.timeOfDayAfterFade = newTime;
            if (Game1.gameTimeInterval > 0)
                Game1.gameTimeInterval = 0;
        }

        private static string FormatClock(int timeOfDay)
        {
            int h = timeOfDay / 100;
            int m = timeOfDay % 100;
            return $"{h:00}:{m:00}";
        }

        /// <summary>直接设置日期（年/季/日一起写）。</summary>
        private void CommandSetDate(string command, string[] args)
        {
            if (!ModEntry.WorldReady)
            {
                this.Monitor.Log("请先进入存档。", LogLevel.Warn);
                return;
            }
            if (args.Length < 3
                || !int.TryParse(args[0], out int year)
                || !int.TryParse(args[1], out int season)
                || !int.TryParse(args[2], out int day))
            {
                this.Monitor.Log("用法: trainer_date <年> <季 0-3> <日 1-28>\n" +
                                 "例如: trainer_date 2 1 15   (= 第2年 夏季 15日)", LogLevel.Info);
                return;
            }

            year = Math.Clamp(year, 1, 999);
            season = Math.Clamp(season, 0, 3);
            day = Math.Clamp(day, 1, 28);

            string before = $"{Game1.year}年 {Game1.currentSeason} {Game1.dayOfMonth}日";
            Game1.year = year;
            Game1.currentSeason = SeasonNames[season];
            Game1.dayOfMonth = day;
            this.Monitor.Log($"日期：{before} -> {Game1.year}年 {Game1.currentSeason} {Game1.dayOfMonth}日" +
                             "（部分内容要过一天才会刷新）", LogLevel.Info);
        }

        /// <summary>季节名的两种写法：索引 -> 英文键 / 中文显示。</summary>
        private static readonly string[] SeasonNames = { "spring", "summer", "fall", "winter" };
        private static readonly string[] SeasonLabels = { "春", "夏", "秋", "冬" };

        /// <summary>
        /// 立刻保存。走游戏自己的 SaveGameMenu——它负责序列化并在完成后继续，
        /// 比直接调 SaveGame.Save() 协程安全。
        /// </summary>
        private void CommandSave(string command, string[] args)
        {
            if (!ModEntry.WorldReady)
            {
                this.Monitor.Log("请先进入存档。", LogLevel.Warn);
                return;
            }
            try
            {
                this.Monitor.Log("正在保存游戏（会出现保存进度条，等一下就好）...", LogLevel.Info);
                Game1.activeClickableMenu = new SaveGameMenu();
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"保存失败：{ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>设置房子升级等级（0-3）。结婚需要至少 1 级。</summary>
        private void CommandHouse(string command, string[] args)
        {
            if (!ModEntry.WorldReady)
            {
                this.Monitor.Log("请先进入存档。", LogLevel.Warn);
                return;
            }
            if (args.Length < 1 || !int.TryParse(args[0], out int level))
            {
                this.Monitor.Log("用法: trainer_house <0-3>   （0=小木屋 1=有厨房 2=有儿童房 3=地窖）", LogLevel.Info);
                return;
            }
            level = Math.Clamp(level, 0, 3);
            int before = Game1.player.HouseUpgradeLevel;
            Game1.player.HouseUpgradeLevel = level;
            try
            {
                if (Game1.getLocationFromName("FarmHouse") is StardewValley.Locations.FarmHouse house)
                    house.updateMap();
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("刷新农舍地图失败：" + ex.Message, LogLevel.Warn);
            }
            this.Monitor.Log($"房子等级：{before} -> {level}", LogLevel.Info);
        }

        /// <summary>设置配偶：trainer_spouse &lt;英文名&gt;</summary>
        private void CommandSpouse(string command, string[] args)
        {
            if (!ModEntry.WorldReady)
            {
                this.Monitor.Log("请先进入存档。", LogLevel.Warn);
                return;
            }

            if (args.Length < 1)
            {
                string current = this._relations.CurrentSpouse() ?? "（还没有配偶）";
                this.Monitor.Log($"当前配偶：{current}", LogLevel.Info);
                List<string> datable = this._relations.ListDatable();
                this.Monitor.Log($"可结婚对象（{datable.Count} 个）：{string.Join(", ", datable)}", LogLevel.Info);
                this.Monitor.Log("用法: trainer_spouse <英文名>，例如 trainer_spouse Abigail", LogLevel.Info);
                return;
            }

            this._relations.Marry(args[0]);
        }

        /// <summary>改天气：trainer_weather &lt;类型&gt; [tomorrow]</summary>
        private void CommandWeather(string command, string[] args)
        {
            if (args.Length < 1)
            {
                this.Monitor.Log("用法: trainer_weather <sunny|rain|storm|snow|wind|green> [tomorrow]", LogLevel.Info);
                return;
            }

            int kind = args[0].ToLowerInvariant() switch
            {
                "sunny" or "sun" or "clear" => WeatherControl.Sunny,
                "rain" or "rainy" => WeatherControl.Rainy,
                "storm" or "stormy" or "lightning" => WeatherControl.Stormy,
                "snow" or "snowy" => WeatherControl.Snowy,
                "wind" or "windy" or "debris" => WeatherControl.Windy,
                "green" or "greenrain" => WeatherControl.GreenRain,
                _ => -1
            };
            if (kind < 0)
            {
                this.Monitor.Log($"不认识的天气「{args[0]}」。可用：sunny / rain / storm / snow / wind / green",
                    LogLevel.Warn);
                return;
            }

            bool tomorrow = args.Length >= 2
                            && (args[1].Equals("tomorrow", StringComparison.OrdinalIgnoreCase) || args[1] == "1");
            this._state.WeatherKind = kind;
            this._weather.Apply(kind, tomorrow);
        }

        /// <summary>
        /// 文件命令通道：读 commands.txt，每行一条命令（写法与控制台完全一致），执行后归档。
        /// 用途：脚本化、以及在无法操作控制台窗口时远程下命令。
        /// </summary>
        private void PollCommandFile()
        {
            try
            {
                if (!File.Exists(this._commandsPath))
                    return;

                string[] lines;
                try
                {
                    lines = File.ReadAllLines(this._commandsPath);
                }
                catch (IOException)
                {
                    return;                       // 文件正被写入，下一轮再读
                }

                // 先把文件挪走，避免重复执行
                string done = this._commandsPath + ".done";
                try
                {
                    if (File.Exists(done))
                        File.Delete(done);
                    File.Move(this._commandsPath, done);
                }
                catch (IOException)
                {
                    return;
                }

                foreach (string raw in lines)
                {
                    string line = raw.Trim();
                    if (line.Length == 0 || line.StartsWith("#", StringComparison.Ordinal))
                        continue;
                    string[] parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    this.RunFileCommand(parts[0], parts.Skip(1).ToArray());
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("读取 commands.txt 失败：" + ex.Message, LogLevel.Warn);
            }
        }

        private void RunFileCommand(string name, string[] args)
        {
            this.Monitor.Log($"[commands.txt] {name} {string.Join(" ", args)}".TrimEnd(), LogLevel.Info);
            switch (name.ToLowerInvariant())
            {
                case "trainer": this.ToggleMenu(true); break;
                case "trainer_debug": this.CommandDebug(name, args); break;
                case "trainer_echo": this.Monitor.Log(
                    "echo: argc=" + args.Length + " args=[" + string.Join("|", args) + "]", LogLevel.Info); break;
                case "trainer_money": this.CommandSetMoney(name, args); break;
                case "trainer_speed": this.CommandSetSpeed(name, args); break;
                case "trainer_item": this.CommandGiveItem(name, args); break;
                case "trainer_finditem": this.CommandFindItem(name, args); break;
                case "trainer_monster": this.CommandSpawnMonster(name, args); break;
                case "trainer_monsters": this.CommandListMonsters(name, args); break;
                case "trainer_npc": this.CommandSpawnNpc(name, args); break;
                case "trainer_clear": this._spawner.ClearMonsters(); break;
                case "trainer_clearnpc": this._spawner.ClearSpawnedVillagers(); break;
                case "trainer_weather": this.CommandWeather(name, args); break;
                case "trainer_tp": this._spawner.TeleportNpcByName(args.Length > 0 ? args[0] : ""); break;
                case "trainer_load": this.CommandLoadSave(name, args); break;
                case "trainer_selftest": this.CommandSelfTest(name, args); break;
                case "trainer_time": this.CommandSetTime(name, args); break;
                case "trainer_date": this.CommandSetDate(name, args); break;
                case "trainer_spouse": this.CommandSpouse(name, args); break;
                case "trainer_house": this.CommandHouse(name, args); break;
                case "trainer_save": this.CommandSave(name, args); break;
                case "trainer_water":
                    this._state.InfiniteWater = args.Length > 0 && args[0] != "0";
                    this.Monitor.Log($"无限浇水器：{(this._state.InfiniteWater ? "开" : "关")}", LogLevel.Info);
                    break;
                case "trainer_fishing":
                    if (args.Length >= 1)
                    {
                        bool on = args[0] != "0";
                        this._state.InstantBite = on;
                        this._state.InstantCatch = on;
                        this._state.EasyFishing = on;
                        this.Monitor.Log($"钓鱼修改（咬钩/捕获/难度）：{(on ? "全开" : "全关")}", LogLevel.Info);
                    }
                    else
                    {
                        this.Monitor.Log($"瞬间咬钩={this._state.InstantBite} " +
                                         $"瞬间捕获={this._state.InstantCatch} " +
                                         $"难度归零={this._state.EasyFishing}", LogLevel.Info);
                    }
                    break;
                case "trainer_friendship":
                    this.Monitor.Log($"已把 {this._logic.MaxAllFriendships()} 个 NPC 的好感度拉满。", LogLevel.Info);
                    break;
                case "trainer_itemcheck":
                    this.Monitor.Log("===== 常用物品表核对 =====", LogLevel.Info);
                    foreach (string line in this._spawner.AuditCommonItems())
                        this.Monitor.Log(line, LogLevel.Info);
                    this.Monitor.Log("===== 核对结束 =====", LogLevel.Info);
                    break;
                case "trainer_toolcheck":
                    this.Monitor.Log("===== 工具表核对 =====", LogLevel.Info);
                    foreach (string line in this._spawner.AuditTools())
                        this.Monitor.Log(line, LogLevel.Info);
                    this.Monitor.Log("===== 核对结束 =====", LogLevel.Info);
                    break;
                case "trainer_tools":
                    this.Monitor.Log($"已获得 {this._spawner.GiveAllTools()} 件工具（已有的跳过）。", LogLevel.Info);
                    break;
                case "trainer_debrisinfo":
                    this.Monitor.Log("===== 杂物分类统计 =====", LogLevel.Info);
                    foreach (string line in this._harvest.DescribeDebris())
                        this.Monitor.Log(line, LogLevel.Info);
                    this.Monitor.Log("===== 结束 =====", LogLevel.Info);
                    break;
                case "trainer_toolids":
                    this.Monitor.Log("===== Data/Tools 里的工具 =====", LogLevel.Info);
                    foreach (string id in Harvest.ListRegisteredTools())
                        this.Monitor.Log("   " + id, LogLevel.Info);
                    this.Monitor.Log("===== 结束 =====", LogLevel.Info);
                    break;
                case "trainer_toolprobe":
                    this.Monitor.Log("===== 候选工具 ID 探测 =====", LogLevel.Info);
                    foreach (string line in this._harvest.ProbeToolIds(new[]
                    {
                        "(T)Axe", "(T)Pickaxe", "(T)Hoe", "(T)WateringCan",
                        "(T)BambooPole", "(T)TrainingRod", "(T)FiberglassRod",
                        "(T)IridiumRod", "(T)AdvancedIridiumRod",
                        "(T)MilkPail", "(T)Shears", "(T)Pan",
                        "(T)CopperPan", "(T)SteelPan", "(T)GoldPan", "(T)IridiumPan",
                        "(T)Scythe", "(T)GoldenScythe", "(T)ReturnScepter",
                        "(T)Lantern", "(T)Slingshot", "(T)MasterSlingshot",
                    }))
                        this.Monitor.Log(line, LogLevel.Info);
                    this.Monitor.Log("===== 结束 =====", LogLevel.Info);
                    break;
                case "trainer_harvestinfo":
                    this.Monitor.Log(
                        $"当前地图可采集：树 {this._harvest.CountTrees()} 棵，" +
                        $"大障碍 {this._harvest.CountClumps()} 个，" +
                        $"石头杂草树枝 {this._harvest.CountDebris()} 个 " +
                        $"@ {Game1.currentLocation?.Name}", LogLevel.Info);
                    break;
                case "trainer_tp_farm":
                    try
                    {
                        Game1.warpFarmer("Farm", 64, 15, false);
                        this.Monitor.Log("已传送到农场", LogLevel.Info);
                    }
                    catch (Exception ex)
                    {
                        this.Monitor.Log("传送到农场失败：" + ex.Message, LogLevel.Warn);
                    }
                    break;
                case "trainer_cut":
                    if (args.Length >= 1 && args[0] == "all")
                    {
                        (int t, int cl, int d) = this._harvest.ClearAll();
                        this.Monitor.Log($"一键采集：砍树 {t} 棵，清大障碍 {cl} 个，清石头杂草 {d} 个。", LogLevel.Info);
                    }
                    else if (args.Length >= 1 && args[0] == "clump")
                        this.Monitor.Log($"清掉大障碍 {this._harvest.ClearClumps()} 个。", LogLevel.Info);
                    else
                        this.Monitor.Log($"砍掉 {this._harvest.CutAllTrees()} 棵树。", LogLevel.Info);
                    break;
                case "trainer_pet":
                    this.Monitor.Log($"宠物好感度：已处理 {this._animals.MaxPetFriendship()} 只。", LogLevel.Info);
                    break;
                case "trainer_animal":
                    this.Monitor.Log(
                        $"农场动物：快乐度 {this._animals.MaxAnimalHappiness()} 只，" +
                        $"饱足度 {this._animals.MaxAnimalFullness()} 只。", LogLevel.Info);
                    break;
                case "trainer_tab":
                    if (args.Length >= 1)
                    {
                        this.ToggleMenu(true);
                        bool ok = this._menu != null && this._menu.SelectTabByName(args[0]);
                        string detail = ok ? "OK" : "没有这个标签页（可用：自身 / NPC / 环境 / 物品 / 关于）";
                        this.Monitor.Log($"切到标签页「{args[0]}」：{detail}", LogLevel.Info);
                    }
                    break;
                case "trainer_npcs":
                    this.Monitor.Log($"共 {this._relations.AllNpcNames().Count} 个 NPC：" +
                                     string.Join(", ", this._relations.AllNpcNames()), LogLevel.Info);
                    break;
                case "trainer_stamina": this._state.InfiniteStamina = args.Length > 0 && args[0] != "0"; break;
                case "trainer_health": this._state.InfiniteHealth = args.Length > 0 && args[0] != "0"; break;
                case "trainer_spew": Cheats.DebugSpew = args.Length > 0 && args[0] != "0"; break;
                default:
                    this.Monitor.Log($"commands.txt 里有不认识的命令：{name}", LogLevel.Warn);
                    break;
            }
        }

        /// <summary>打印实时数值，用来确认每一项修改到底有没有生效。</summary>
        private void CommandDebug(string command, string[] args)
        {
            if (!ModEntry.WorldReady || Game1.player == null)
            {
                this.Monitor.Log("请先进入存档。", LogLevel.Warn);
                return;
            }

            Farmer p = Game1.player;
            this.Monitor.Log("===== 修改器状态 =====", LogLevel.Info);
            this.Monitor.Log($"  无限体力={this._state.InfiniteStamina}  无限生命={this._state.InfiniteHealth}", LogLevel.Info);
            this.Monitor.Log($"  体力填充={this._state.StaminaFillPercent}%  生命填充={this._state.HealthFillPercent}%", LogLevel.Info);
            this.Monitor.Log($"  速度倍率=×{this._state.SpeedMultiplier:0.0}  时间流速=×{this._state.TimeScale:0.0}", LogLevel.Info);
            this.Monitor.Log($"  幸运拉满={this._state.AlwaysMaxLuck}  幸运等级={this._state.LuckLevel}", LogLevel.Info);
            this.Monitor.Log($"  一击必杀={this._state.OneHitKill}  怪物不刷新={this._state.NoMonsters}", LogLevel.Info);

            this.Monitor.Log("===== 游戏里的实际数值 =====", LogLevel.Info);
            this.Monitor.Log(
                $"  游戏时间 {Game1.timeOfDay / 100:00}:{Game1.timeOfDay % 100:00}   " +
                $"日期 {Game1.year}年 {Game1.currentSeason} {Game1.dayOfMonth}日", LogLevel.Info);
            this.Monitor.Log(
                $"  配偶：{p.spouse ?? "（无）"}   getSpouse()={p.getSpouse()?.Name ?? "null"}   " +
                $"已婚={p.isMarriedOrRoommates()}", LogLevel.Info);
            this.Monitor.Log($"  体力 {p.Stamina:0} / {p.MaxStamina}   生命 {p.health} / {p.maxHealth}", LogLevel.Info);
            this.Monitor.Log($"  金钱 {p.Money:N0}", LogLevel.Info);
            this.Monitor.Log($"  基础速度(Speed)={p.Speed:0.00}  附加速度(addedSpeed)={p.addedSpeed:0.00}", LogLevel.Info);
            this.Monitor.Log($"  实用移速(getMovementSpeed)={p.getMovementSpeed():0.00}", LogLevel.Info);

            try
            {
                this.Monitor.Log($"  生效中的 buff：{p.buffs.AppliedBuffs.Count} 个", LogLevel.Info);
                foreach (KeyValuePair<string, StardewValley.Buff> kv in p.buffs.AppliedBuffs)
                    this.Monitor.Log($"     [{kv.Key}] {kv.Value.displayName}  速度={kv.Value.effects.Speed.Value}", LogLevel.Info);
                this.Monitor.Log($"  buff 合成的速度加成 = {p.buffs.Speed}", LogLevel.Info);
            }
            catch (Exception ex)
            {
                this.Monitor.Log("  读取 buff 失败：" + ex.Message, LogLevel.Warn);
            }

            this.Monitor.Log($"  当前位置 {p.Tile.X:0},{p.Tile.Y:0} @ {Game1.currentLocation?.Name}", LogLevel.Info);
            this.Monitor.Log($"  当前地图怪物数：{Game1.currentLocation?.characters?.Count(c => c is StardewValley.Monsters.Monster)}", LogLevel.Info);

            // 游戏认不认识某个 NPC（生成/传送逻辑依赖这个查找）
            try
            {
                NPC? probe = Game1.getCharacterFromName("Abigail");
                this.Monitor.Log(
                    $"  游戏能否找到 Abigail：{(probe != null ? $"能，现在在 {probe.currentLocation?.Name}" : "不能（说明她还不属于这个世界）")}",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("查找 NPC 失败：" + ex.Message, LogLevel.Warn);
            }

            // 天气：同时看全局标志和当前地图的 LocationWeather，确认两边都改了
            try
            {
                StardewValley.Network.LocationWeather lw = Game1.currentLocation!.GetWeather();
                this.Monitor.Log(
                    $"  天气 全局[雨={Game1.isRaining} 雷={Game1.isLightning} 雪={Game1.isSnowing} " +
                    $"风={Game1.isDebrisWeather} 绿雨={Game1.isGreenRain}] " +
                    $"地图[{lw.Weather} 雨={lw.IsRaining} 雷={lw.IsLightning} 雪={lw.IsSnowing} " +
                    $"风={lw.IsDebrisWeather}] 明天={Game1.weatherForTomorrow}",
                    LogLevel.Info);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("读取天气失败：" + ex.Message, LogLevel.Warn);
            }
        }

        // ------------------------------------------------------------------
        // 绘制
        // ------------------------------------------------------------------
        private void OnRenderingHud(object? sender, RenderingHudEventArgs e)
        {
            // 非暂停模式：直接画在 HUD 上
            if (this._menu != null && !this._menu.PausesGame)
                this._menu.draw(e.SpriteBatch);
        }
    }
}
