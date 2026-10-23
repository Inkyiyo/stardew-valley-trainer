using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewValley;
using StardewValley.Menus;

namespace StardewTrainer
{
    /// <summary>
    /// 悬浮修改器菜单。按作用对象分成标签页：自身 / NPC / 环境 / 物品 / 关于。
    /// 所有文字都走 I18n.T(key)，翻译只需改 i18n/*.json。
    /// </summary>
    public class TrainerMenu : IClickableMenu
    {
        private const int RowHeight = 30;
        private const int PadX = 20;
        private const int TitleBarHeight = 52;
        private const int TabBarHeight = 44;
        private const int FooterHeight = 38;
        private const int ListTopOffset = TitleBarHeight + TabBarHeight;

        /// <summary>标签页的文案 key，顺序就是显示顺序。</summary>
        private static readonly string[] TabKeys =
        {
            "tab.self", "tab.npc", "tab.world", "tab.item", "tab.about"
        };

        private readonly ModConfig Config;
        private readonly CheatState State;
        private readonly Cheats Logic;
        private readonly Spawner Spawner;
        private readonly WeatherControl Weather;
        private readonly Relationships Relations;
        private readonly Animals Animals;
        private readonly Harvest Harvest;
        private readonly string ModVersion;

        private int _tab;
        private List<TrainerRow> Rows = new();
        private int Selected;
        private int Scroll;

        private int _itemIndex;
        private int _itemCount = 1;
        private int _spouseIndex;
        private int _monsterIndex;
        private int _npcSpawnIndex;

        private string? Toast;
        private int _toastTicks;

        private const int HoldDelayTicks = 24;
        private const int HoldFastAfterTicks = 90;
        private int? _holdRow;
        private int _holdDir;
        private int _holdTicks;

        private static readonly Color ColBack = new(14, 16, 22, 242);
        private static readonly Color ColShadow = new(0, 0, 0, 160);
        private static readonly Color ColBorder = new(214, 178, 92);
        private static readonly Color ColTitle = new(30, 36, 52, 255);
        private static readonly Color ColTabBar = new(22, 26, 38, 255);
        private static readonly Color ColTabActive = new(96, 82, 30, 255);
        private static readonly Color ColTabIdle = new(34, 40, 56, 255);
        private static readonly Color ColFooter = new(22, 26, 38, 255);
        private static readonly Color ColSelected = new(96, 82, 30, 220);
        private static readonly Color ColHover = new(70, 70, 90, 150);
        private static readonly Color ColSection = new(238, 205, 122);
        private static readonly Color ColText = new(238, 238, 238);
        private static readonly Color ColValueOn = new(120, 235, 120);
        private static readonly Color ColValueOff = new(190, 190, 190);
        private static readonly Color ColHint = new(150, 156, 172);
        private static readonly Color ColWarn = new(255, 170, 90);

        public TrainerMenu(ModConfig config, CheatState state, Cheats logic, Spawner spawner,
                           WeatherControl weather, Relationships relations, Animals animals,
                           Harvest harvest, string modVersion)
            : base(0, 0, 0, 0, true)
        {
            this.Config = config;
            this.State = state;
            this.Logic = logic;
            this.Spawner = spawner;
            this.Weather = weather;
            this.Relations = relations;
            this.Animals = animals;
            this.Harvest = harvest;
            this.ModVersion = modVersion;
            this.Layout();
            this.BuildTab();
        }

        public bool PausesGame => this.Config.PauseWhileMenuOpen;
        public int SelectedIndex => this.Selected;
        public int ActiveTab => this._tab;
        public int RowCount => this.Rows.Count;

        public string SelectedLabel =>
            this.Selected >= 0 && this.Selected < this.Rows.Count ? this.Rows[this.Selected].Label : I18n.T("msg.none");

        // ------------------------------------------------------------------
        // 布局 / 工具
        // ------------------------------------------------------------------
        private void Layout()
        {
            this.width = Math.Min(920, Game1.uiViewport.Width - 60);
            this.height = Math.Min(650, Game1.uiViewport.Height - 60);
            this.xPositionOnScreen = (Game1.uiViewport.Width - this.width) / 2;
            this.yPositionOnScreen = (Game1.uiViewport.Height - this.height) / 2;
        }

        public override void gameWindowSizeChanged(Rectangle oldBounds, Rectangle newBounds)
        {
            this.Layout();
            base.gameWindowSizeChanged(oldBounds, newBounds);
        }

        private int VisibleRows =>
            Math.Max(1, (this.height - ListTopOffset - FooterHeight - 8) / RowHeight);

        private static int Wrap(int value, int length)
        {
            if (length <= 0)
                return 0;
            value %= length;
            return value < 0 ? value + length : value;
        }

        private static string SeasonKey(int index) =>
            index switch { 0 => "spring", 1 => "summer", 2 => "fall", _ => "winter" };

        private static string FormatClock(int timeOfDay) =>
            $"{timeOfDay / 100:00}:{timeOfDay % 100:00}";

        /// <summary>日期显示成「第 2 年 夏季 17 日」，别只写「2 夏 17」。</summary>
        private static string FormatDate() =>
            I18n.T("unit.date_full_fmt",
                   I18n.T("unit.year_fmt", Game1.year),
                   I18n.Season(Game1.currentSeason),
                   I18n.T("unit.day_fmt", Game1.dayOfMonth));

        // ------------------------------------------------------------------
        // 标签页
        // ------------------------------------------------------------------
        public void SwitchTab(int tab)
        {
            int next = Wrap(tab, TabKeys.Length);
            if (next == this._tab)
                return;
            this._tab = next;
            this.Selected = 0;
            this.Scroll = 0;
            this.EndHold();
            this.BuildTab();
            Sound.Play(Sound.MenuConfirm);
        }

        public void NextTab() => this.SwitchTab(this._tab + 1);
        public void PrevTab() => this.SwitchTab(this._tab - 1);

        /// <summary>按文案选标签页（支持 key 或显示名），自检/命令用。</summary>
        public bool SelectTabByName(string name)
        {
            for (int i = 0; i < TabKeys.Length; i++)
            {
                if (TabKeys[i].Equals(name, StringComparison.OrdinalIgnoreCase)
                    || I18n.T(TabKeys[i]).Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    this.SwitchTab(i);
                    return true;
                }
            }
            // 也允许用 "自身"/"self" 这种简写
            for (int i = 0; i < TabKeys.Length; i++)
            {
                string key = TabKeys[i].Substring(TabKeys[i].IndexOf('.') + 1);
                if (key.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    this.SwitchTab(i);
                    return true;
                }
            }
            return false;
        }

        private Rectangle TabRect(int index)
        {
            int tabWidth = (this.width - 8) / TabKeys.Length;
            return new Rectangle(this.xPositionOnScreen + 4 + index * tabWidth,
                                 this.yPositionOnScreen + TitleBarHeight,
                                 tabWidth - 4, TabBarHeight - 6);
        }

        private int TabIndexAt(int x, int y)
        {
            for (int i = 0; i < TabKeys.Length; i++)
            {
                if (this.TabRect(i).Contains(x, y))
                    return i;
            }
            return -1;
        }

        private void BuildTab()
        {
            this.Rows = this._tab switch
            {
                0 => this.BuildSelfTab(),
                1 => this.BuildNpcTab(),
                2 => this.BuildWorldTab(),
                3 => this.BuildItemTab(),
                _ => this.BuildAboutTab()
            };

            if (this.Selected >= this.Rows.Count)
                this.Selected = Math.Max(0, this.Rows.Count - 1);
            if (this.Selected < 0 || !this.Rows[this.Selected].Selectable)
            {
                int first = this.Rows.FindIndex(r => r.Selectable);
                this.Selected = first >= 0 ? first : 0;
            }
            this.EnsureVisible();
        }

        // ---- 行构造小工具（都走 I18n）----
        private static void Section(List<TrainerRow> rows, string key) =>
            rows.Add(new TrainerRow { Kind = RowKind.Section, Label = I18n.T(key) });

        private static void Readonly(List<TrainerRow> rows, string key, Func<string> get) =>
            rows.Add(new TrainerRow { Kind = RowKind.Readonly, Label = I18n.T(key), GetValue = get });

        private static void Toggle(List<TrainerRow> rows, string key, Func<bool> get, Action<bool> set) =>
            rows.Add(new TrainerRow
            {
                Kind = RowKind.Toggle,
                Label = I18n.T(key),
                GetValue = () => get() ? I18n.T("value.on") : I18n.T("value.off"),
                Activate = () => set(!get())
            });

        private static void Number(List<TrainerRow> rows, string key, Func<string> get, Action<int> adjust) =>
            rows.Add(new TrainerRow { Kind = RowKind.Number, Label = I18n.T(key), GetValue = get, Adjust = adjust });

        private static void Action(List<TrainerRow> rows, string label, Action act, string? valueKey = null) =>
            rows.Add(new TrainerRow
            {
                Kind = RowKind.Action,
                Label = label,
                GetValue = () => I18n.T(valueKey ?? "value.execute"),
                Activate = act
            });

        /// <summary>需要动态文案的行（例如带物品名）。</summary>
        private static void ActionRaw(List<TrainerRow> rows, Func<string> label, Action act) =>
            rows.Add(new TrainerRow
            {
                Kind = RowKind.Action,
                Label = "",
                GetValue = () => I18n.T("value.execute"),
                Activate = act,
                GetLabel = label
            });

        // ------------------------------------------------------------------
        // ① 自身
        // ------------------------------------------------------------------
        private List<TrainerRow> BuildSelfTab()
        {
            var rows = new List<TrainerRow>();

            Section(rows, "section.status");
            Readonly(rows, "row.character", () => Game1.player == null
                ? I18n.T("msg.none")
                : $"{Game1.player.Name} @ {Game1.player.farmName}");
            Readonly(rows, "row.datetime", () => Game1.player == null
                ? I18n.T("msg.none")
                : $"{FormatDate()}  {FormatClock(Game1.timeOfDay)}");
            Readonly(rows, "row.hp_stamina", () => Game1.player == null
                ? I18n.T("msg.none")
                : $"{Game1.player.Stamina:0}/{Game1.player.MaxStamina}   {Game1.player.health}/{Game1.player.maxHealth}");
            Readonly(rows, "row.money", () => Game1.player == null ? I18n.T("msg.none") : Game1.player.Money.ToString("N0"));
            Readonly(rows, "row.speed", () => Game1.player == null
                ? I18n.T("msg.none")
                : $"{Game1.player.getMovementSpeed():0.00}  ({Game1.player.Speed:0.0} + {Game1.player.addedSpeed:0.0})");
            Readonly(rows, "row.position", () => Game1.player == null
                ? I18n.T("msg.none")
                : $"{Game1.player.Tile.X:0},{Game1.player.Tile.Y:0} @ {Game1.currentLocation?.Name}");
            Readonly(rows, "row.spouse", () => this.Relations.CurrentSpouse() ?? I18n.T("msg.no_spouse"));

            Section(rows, "section.survival");
            Toggle(rows, "row.infinite_stamina", () => this.State.InfiniteStamina, v => this.State.InfiniteStamina = v);
            Toggle(rows, "row.infinite_health", () => this.State.InfiniteHealth, v => this.State.InfiniteHealth = v);
            Number(rows, "row.stamina_fill", () => I18n.T("unit.percent_fmt", this.State.StaminaFillPercent), d =>
                this.State.StaminaFillPercent = Math.Clamp(this.State.StaminaFillPercent + d * 10, 0, 100));
            Number(rows, "row.health_fill", () => I18n.T("unit.percent_fmt", this.State.HealthFillPercent), d =>
                this.State.HealthFillPercent = Math.Clamp(this.State.HealthFillPercent + d * 10, 0, 100));

            Section(rows, "section.move");
            Number(rows, "row.speed_mult", () => I18n.T("unit.speed_fmt", this.State.SpeedMultiplier), d =>
                this.State.SpeedMultiplier = Math.Clamp(
                    this.State.SpeedMultiplier + d * this.Config.SpeedStep, 1f, this.Config.MaxSpeedMultiplier));
            Action(rows, I18n.T("row.speed_reset"), () => this.State.SpeedMultiplier = 1f);
            Toggle(rows, "row.instant_charge", () => this.State.InstantCharge, v => this.State.InstantCharge = v);

            Section(rows, "section.tool");
            Toggle(rows, "row.infinite_water", () => this.State.InfiniteWater, v => this.State.InfiniteWater = v);
            Toggle(rows, "row.instant_bite", () => this.State.InstantBite, v => this.State.InstantBite = v);
            Toggle(rows, "row.instant_catch", () => this.State.InstantCatch, v => this.State.InstantCatch = v);
            Toggle(rows, "row.easy_fishing", () => this.State.EasyFishing, v => this.State.EasyFishing = v);
            Readonly(rows, "row.current_tool", () => Game1.player?.CurrentTool?.Name ?? I18n.T("msg.hand_empty"));

            Section(rows, "section.skill");
            Number(rows, "row.skill_target", () => this.State.SkillTargetLevel.ToString(), d =>
                this.State.SkillTargetLevel = Math.Clamp(this.State.SkillTargetLevel + d, 1, 10));
            Action(rows, I18n.T("row.skill_apply"), this.ApplyAllSkills);
            for (int i = 0; i < 5; i++)
            {
                TrainerSkill skill = (TrainerSkill)i;
                rows.Add(new TrainerRow
                {
                    Kind = RowKind.Readonly,
                    Label = I18n.T("row.skill_level_fmt", SkillTable.SkillNames[i]),
                    GetValue = () => SkillTable.GetPlayerLevel(Game1.player, skill).ToString()
                });
            }
            Toggle(rows, "row.luck_max", () => this.State.AlwaysMaxLuck, v => this.State.AlwaysMaxLuck = v);
            Number(rows, "row.luck_level", () => this.State.LuckLevel.ToString(), d =>
                this.State.LuckLevel = Math.Clamp(this.State.LuckLevel + d, 1, 50));

            Section(rows, "section.money");
            Action(rows, I18n.T("row.money_add_1"), () => this.AddMoney(10000));
            Action(rows, I18n.T("row.money_add_2"), () => this.AddMoney(100000));
            Action(rows, I18n.T("row.money_add_3"), () => this.AddMoney(1000000));
            Number(rows, "row.money_step", () => this.State.MoneyStep.ToString("N0"), d =>
                this.State.MoneyStep = Math.Clamp(this.State.MoneyStep + d * 10000, 10000, 10000000));
            Action(rows, I18n.T("row.money_add_step"), () => this.AddMoney(this.State.MoneyStep));
            Action(rows, I18n.T("row.money_sub_step"), () => this.AddMoney(-this.State.MoneyStep));
            Action(rows, I18n.T("row.money_max"), () => this.SetMoney(2_000_000_000));

            Section(rows, "section.oneclick");
            Action(rows, I18n.T("row.harvest_all"), this.HarvestAll);
            Readonly(rows, "row.harvest_count", () => I18n.T("unit.harvest_count_fmt",
                this.Harvest.CountTrees(), this.Harvest.CountClumps(), this.Harvest.CountDebris()));
            Action(rows, I18n.T("row.give_tools"), this.GiveAllTools);
            Action(rows, I18n.T("row.cut_trees"), this.CutAllTrees);
            Action(rows, I18n.T("row.clear_debris"), this.ClearDebris);
            Action(rows, I18n.T("row.clear_clumps"), this.ClearClumps);

            Section(rows, "section.teleport");
            Number(rows, "row.tp_x", () => this.State.TeleportTileX.ToString(), d =>
                this.State.TeleportTileX = Math.Clamp(this.State.TeleportTileX + d, 0, 500));
            Number(rows, "row.tp_y", () => this.State.TeleportTileY.ToString(), d =>
                this.State.TeleportTileY = Math.Clamp(this.State.TeleportTileY + d, 0, 500));
            Action(rows, I18n.T("row.tp_read"), this.ReadCurrentTile);
            Action(rows, I18n.T("row.tp_go"), this.Teleport);

            return rows;
        }

        // ------------------------------------------------------------------
        // ② NPC
        // ------------------------------------------------------------------
        private List<TrainerRow> BuildNpcTab()
        {
            var rows = new List<TrainerRow>();

            Section(rows, "section.friendship");
            Readonly(rows, "row.npc_count", () => this.Relations.NpcCount().ToString());
            Action(rows, I18n.T("row.friendship_max"), this.MaxFriendships);

            Section(rows, "section.pets");
            Readonly(rows, "row.pet_count", () => this.Animals.AllPets().Count.ToString());
            Readonly(rows, "row.animal_count", () => this.Animals.AllFarmAnimals().Count.ToString());
            Action(rows, I18n.T("row.pet_friendship_max"), this.MaxPetFriendship);
            Action(rows, I18n.T("row.animal_happiness_max"), this.MaxAnimalHappiness);
            Action(rows, I18n.T("row.animal_fullness_max"), this.MaxAnimalFullness);

            Section(rows, "section.spouse");
            Readonly(rows, "row.spouse_current", () => this.Relations.CurrentSpouse() ?? I18n.T("msg.no_spouse"));
            Number(rows, "row.spouse_pick", () =>
            {
                string name = this.Relations.NpcNameAt(this._spouseIndex);
                return name.Length > 0 ? $"{name}   [{this.Relations.DescribeRelationship(name)}]" : I18n.T("msg.no_npc");
            }, d => this._spouseIndex = this.Relations.NpcCount() > 0
                ? Wrap(this._spouseIndex + d, this.Relations.NpcCount())
                : 0);
            Action(rows, I18n.T("row.spouse_set"), this.MarrySelected);
            Action(rows, I18n.T("row.spouse_abigail"), () =>
            {
                this.Relations.Marry("Abigail");
                this.ToastFor("msg.spouse_abigail");
            });
            Readonly(rows, "row.spouse_tip", () => I18n.T("row.spouse_tip_text"));

            Section(rows, "section.spawnnpc");
            Number(rows, "row.npc_pick", () => Spawner.SpawnableVillagers[this._npcSpawnIndex].Label, d =>
                this._npcSpawnIndex = Wrap(this._npcSpawnIndex + d, Spawner.SpawnableVillagers.Length));
            Action(rows, I18n.T("row.npc_spawn"), () =>
            {
                var (label, name) = Spawner.SpawnableVillagers[this._npcSpawnIndex];
                bool ok = this.Spawner.SpawnVillager(name);
                this.ToastFor(ok ? "msg.npc_spawned_fmt" : "msg.npc_spawn_fail_fmt", label);
            });
            Action(rows, I18n.T("row.npc_teleport"), () =>
            {
                string name = this.Relations.NpcNameAt(this._spouseIndex);
                bool ok = this.Spawner.TeleportNpcByName(name);
                this.ToastFor(ok ? "msg.npc_teleported_fmt" : "msg.npc_notfound_fmt", name);
            });
            Action(rows, I18n.T("row.npc_clear"), () =>
            {
                int n = this.Spawner.ClearSpawnedVillagers();
                this.ToastFor(n > 0 ? "msg.npc_cleared_fmt" : "msg.npc_nothing", n);
            });

            Section(rows, "section.monster");
            Number(rows, "row.monster_pick", () => Spawner.SpawnableMonsters[this._monsterIndex].Label, d =>
                this._monsterIndex = Wrap(this._monsterIndex + d, Spawner.SpawnableMonsters.Length));
            Action(rows, I18n.T("row.monster_spawn"), () =>
            {
                var (label, type) = Spawner.SpawnableMonsters[this._monsterIndex];
                bool ok = this.Spawner.SpawnMonster(type);
                this.ToastFor(ok ? "msg.monster_spawned_fmt" : "msg.monster_spawn_fail_fmt", label);
            });
            Toggle(rows, "row.one_hit_kill", () => this.State.OneHitKill, v => this.State.OneHitKill = v);
            Action(rows, I18n.T("row.monster_clear"), () =>
            {
                int n = this.Spawner.ClearMonsters();
                this.ToastFor(n > 0 ? "msg.monster_cleared_fmt" : "msg.monster_none", n);
            });
            Toggle(rows, "row.no_monsters", () => this.State.NoMonsters, v => this.State.NoMonsters = v);

            return rows;
        }

        // ------------------------------------------------------------------
        // ③ 环境
        // ------------------------------------------------------------------
        private List<TrainerRow> BuildWorldTab()
        {
            var rows = new List<TrainerRow>();

            Section(rows, "section.weather");
            Readonly(rows, "row.weather_current", () => this.Weather.Describe());
            Number(rows, "row.weather_pick", () => WeatherControl.Names[this.State.WeatherKind], d =>
                this.State.WeatherKind = Wrap(this.State.WeatherKind + d, WeatherControl.Names.Length));
            Toggle(rows, "row.weather_tomorrow", () => this.State.WeatherTomorrow, v => this.State.WeatherTomorrow = v);
            Action(rows, I18n.T("row.weather_apply"), this.ApplyWeather);
            for (int i = 0; i < WeatherControl.Names.Length; i++)
            {
                int kind = i;
                Action(rows, I18n.T("row.weather_direct_fmt", WeatherControl.Names[i]), () => this.SetWeather(kind));
            }

            Section(rows, "section.time");
            Readonly(rows, "row.time_current", () => FormatClock(Game1.timeOfDay));
            Number(rows, "row.time_hour", () => I18n.T("unit.hour_fmt", $"{this.State.SetHour:00}"), d =>
                this.State.SetHour = Math.Clamp(this.State.SetHour + d, 6, 26));
            Number(rows, "row.time_minute", () => I18n.T("unit.minute_fmt", $"{this.State.SetMinute:00}"), d =>
                this.State.SetMinute = Math.Clamp(this.State.SetMinute + d * 10, 0, 50));
            Number(rows, "row.time_step", () => I18n.T("unit.minutes_fmt", this.State.TimeStepMinutes), d =>
                this.State.TimeStepMinutes = Math.Clamp(this.State.TimeStepMinutes + d * 10, 10, 120));
            Action(rows, I18n.T("row.time_apply"), this.ApplySetTime);
            Action(rows, I18n.T("row.time_plus"), () => this.AddTime(this.State.TimeStepMinutes));
            Action(rows, I18n.T("row.time_minus"), () => this.AddTime(-this.State.TimeStepMinutes));
            Action(rows, I18n.T("row.time_jump_fmt", "06:00", I18n.T("time.morning")), () => this.SetClock(6, 0));
            Action(rows, I18n.T("row.time_jump_fmt", "12:00", I18n.T("time.noon")), () => this.SetClock(12, 0));
            Action(rows, I18n.T("row.time_jump_fmt", "18:00", I18n.T("time.evening")), () => this.SetClock(18, 0));
            Action(rows, I18n.T("row.time_jump_fmt", "22:00", I18n.T("time.night")), () => this.SetClock(22, 0));
            Action(rows, I18n.T("row.time_jump_fmt", "24:00", I18n.T("time.midnight")), () => this.SetClock(24, 0));
            Toggle(rows, "row.time_freeze", () => this.State.TimeFreeze, v => this.State.TimeFreeze = v);
            Number(rows, "row.time_scale", () => I18n.T("unit.times_fmt", this.State.TimeScale), d =>
                this.State.TimeScale = Math.Clamp(this.State.TimeScale + d * 0.5f, 0.5f, 10f));
            Readonly(rows, "row.time_tip", () => I18n.T("row.time_tip_text"));

            Section(rows, "section.date");
            Readonly(rows, "row.date_current", () => FormatDate());            Number(rows, "row.date_year", () => I18n.T("unit.year_fmt", this.State.SetYear), d =>
                this.State.SetYear = Math.Clamp(this.State.SetYear + d, 1, 99));
            Number(rows, "row.date_season", () => I18n.Season(SeasonKey(this.State.SetSeasonIndex)), d =>
                this.State.SetSeasonIndex = Wrap(this.State.SetSeasonIndex + d, 4));
            Number(rows, "row.date_day", () => I18n.T("unit.day_fmt", this.State.SetDay), d =>
                this.State.SetDay = Math.Clamp(this.State.SetDay + d, 1, 28));
            Action(rows, I18n.T("row.date_apply"), this.ApplySetDate);

            Section(rows, "section.other");
            Action(rows, I18n.T("row.save"), this.SaveGame);
            Readonly(rows, "row.save_tip", () => I18n.T("row.save_tip_text"));

            return rows;
        }

        // ------------------------------------------------------------------
        // ④ 物品
        // ------------------------------------------------------------------
        private List<TrainerRow> BuildItemTab()
        {
            var rows = new List<TrainerRow>();

            Section(rows, "section.getitem");
            Readonly(rows, "row.item_tip", () => I18n.T("row.item_tip_text"));
            Number(rows, "row.item_pick", () => Spawner.CommonItems[this._itemIndex].Label, d =>
                this._itemIndex = Wrap(this._itemIndex + d, Spawner.CommonItems.Length));
            Number(rows, "row.item_count", () => this._itemCount.ToString(), d =>
                this._itemCount = Math.Clamp(this._itemCount + d, 1, 999));
            Action(rows, I18n.T("row.item_give"), () => this.GiveItemAt(this._itemIndex, this._itemCount));
            Action(rows, I18n.T("row.item_next"), () =>
                this._itemIndex = Wrap(this._itemIndex + 1, Spawner.CommonItems.Length));

            Section(rows, "section.commonitems");
            for (int i = 0; i < Spawner.CommonItems.Length; i++)
            {
                int idx = i;
                string label = Spawner.CommonItems[i].Label;
                if (string.IsNullOrEmpty(label) || label.StartsWith("（", StringComparison.Ordinal))
                    continue;
                ActionRaw(rows, () => $"  {label}", () => this.GiveItemAt(idx, 1));
            }

            return rows;
        }

        // ------------------------------------------------------------------
        // ⑤ 关于
        // ------------------------------------------------------------------
        private List<TrainerRow> BuildAboutTab()
        {
            var rows = new List<TrainerRow>();

            Section(rows, "section.version");
            Readonly(rows, "row.version_mod", () => "v" + this.ModVersion);
            Readonly(rows, "row.version_game", () => Game1.version ?? "?");
            Readonly(rows, "row.version_smapi", () => StardewModdingAPI.Constants.ApiVersion.ToString());

            Section(rows, "section.hotkeys");
            Readonly(rows, "row.hotkey_menu", () => this.Config.MenuKey.ToString());
            Readonly(rows, "row.hotkey_stamina", () => this.Config.InfiniteStaminaKey.ToString());
            Readonly(rows, "row.hotkey_health", () => this.Config.InfiniteHealthKey.ToString());
            Readonly(rows, "row.hotkey_speed", () => $"{this.Config.SpeedUpKey} / {this.Config.SpeedDownKey}");

            Section(rows, "section.controls");
            Readonly(rows, "row.ctrl_tab", () => I18n.T("row.ctrl_tab_text"));
            Readonly(rows, "row.ctrl_select", () => I18n.T("row.ctrl_select_text"));
            Readonly(rows, "row.ctrl_adjust", () => I18n.T("row.ctrl_adjust_text"));
            Readonly(rows, "row.ctrl_exec", () => I18n.T("row.ctrl_exec_text"));
            Readonly(rows, "row.ctrl_scroll", () => I18n.T("row.ctrl_scroll_text"));
            Readonly(rows, "row.ctrl_close", () => I18n.T("row.ctrl_close_text"));

            Section(rows, "section.commands");
            Readonly(rows, "row.cmd_debug", () => "trainer_debug");
            Readonly(rows, "row.cmd_time", () => "trainer_time 1800 / trainer_date 2 1 15");
            Readonly(rows, "row.cmd_weather", () => "trainer_weather rain / trainer_spouse Abigail");
            Readonly(rows, "row.cmd_item", () => "trainer_item Diamond 20 / trainer_npcs");
            Readonly(rows, "row.cmd_save", () => "trainer_save");

            return rows;
        }

        // ------------------------------------------------------------------
        // 动作
        // ------------------------------------------------------------------
        private void ToastFor(string key, params object?[] args)
        {
            this.Toast = I18n.T(key, args);
            this._toastTicks = 0;
        }

        private void AddMoney(int amount)
        {
            if (Game1.player == null)
                return;
            this.SetMoney(Game1.player.Money + amount);
        }

        private void SetMoney(int value)
        {
            if (Game1.player == null)
                return;
            int clamped = Math.Clamp(value, 0, 2_000_000_000);
            Game1.player.Money = clamped;
            if (Game1.player.totalMoneyEarned < clamped)
                Game1.player.totalMoneyEarned = (uint)clamped;
            Sound.Play(Sound.Coin);
        }

        private void ApplyAllSkills()
        {
            if (Game1.player == null)
                return;
            for (int i = 0; i < 5; i++)
                SkillTable.SetPlayerLevel(Game1.player, (TrainerSkill)i, this.State.SkillTargetLevel);
            Sound.Play(Sound.LevelUp);
            this.ToastFor("msg.skill_applied_fmt", this.State.SkillTargetLevel);
        }

        private void MaxFriendships()
        {
            int n = this.Logic.MaxAllFriendships();
            Sound.Play(Sound.LevelUp);
            this.ToastFor("msg.friendship_done_fmt", n);
        }

        private void MaxPetFriendship()
        {
            int n = this.Animals.MaxPetFriendship();
            Sound.Play(Sound.LevelUp);
            this.ToastFor(n > 0 ? "msg.pet_friendship_fmt" : "msg.pet_none", n);
        }

        private void MaxAnimalHappiness()
        {
            int n = this.Animals.MaxAnimalHappiness();
            Sound.Play(Sound.LevelUp);
            this.ToastFor(n > 0 ? "msg.animal_happy_fmt" : "msg.animal_none", n);
        }

        private void MaxAnimalFullness()
        {
            int n = this.Animals.MaxAnimalFullness();
            Sound.Play(Sound.LevelUp);
            this.ToastFor(n > 0 ? "msg.animal_full_fmt" : "msg.animal_none", n);
        }

        // ---- 一键采集 ----
        private void GiveAllTools()
        {
            int n = this.Spawner.GiveAllTools();
            Sound.Play(Sound.Pickup);
            this.ToastFor(n > 0 ? "msg.tools_given_fmt" : "msg.tools_all_had", n);
        }

        private void HarvestAll()
        {
            (int trees, int clumps, int debris) = this.Harvest.HarvestAll();
            Sound.Play(Sound.Pickup);
            if (trees == 0 && clumps == 0 && debris == 0)
                this.ToastFor("msg.harvest_none");
            else
                this.ToastFor("msg.harvest_all_fmt", trees, clumps, debris);
        }

        private void CutAllTrees()
        {
            int n = this.Harvest.CutAllTrees();
            Sound.Play(Sound.Pickup);
            this.ToastFor(n > 0 ? "msg.trees_cut_fmt" : "msg.trees_none", n);
        }

        private void ClearDebris()
        {
            int n = this.Harvest.ClearDebris();
            Sound.Play(Sound.Pickup);
            this.ToastFor(n > 0 ? "msg.debris_cleared_fmt" : "msg.debris_none", n);
        }

        private void ClearClumps()
        {
            int n = this.Harvest.ClearClumps();
            Sound.Play(Sound.Pickup);
            this.ToastFor(n > 0 ? "msg.clumps_cleared_fmt" : "msg.clumps_none", n);
        }

        private void GiveItemAt(int index, int count)
        {
            if (index < 0 || index >= Spawner.CommonItems.Length)
                return;
            (string id, string english, string label) = Spawner.CommonItems[index];
            string query = id == "(O)0" ? label : id;
            int given = this.Spawner.GiveItem(query, count);
            this.ToastFor(given > 0 ? "msg.item_get_fmt" : "msg.item_fail_fmt", label, given);
        }

        private void MarrySelected()
        {
            string name = this.Relations.NpcNameAt(this._spouseIndex);
            if (name.Length == 0)
            {
                this.ToastFor("msg.npc_none");
                return;
            }
            bool ok = this.Relations.Marry(name);
            this.ToastFor(ok ? "msg.spouse_set_fmt" : "msg.spouse_fail_fmt", name);
        }

        private void ApplySetTime() => this.SetClock(this.State.SetHour, this.State.SetMinute);

        private void SetClock(int hour, int minute)
        {
            this.State.SetHour = Math.Clamp(hour, 6, 26);
            this.State.SetMinute = Math.Clamp(minute / 10 * 10, 0, 50);
            this.Logic.SetClock(this.State.SetHour, this.State.SetMinute);
            this.ToastFor("msg.time_set_fmt", FormatClock(Game1.timeOfDay));
        }

        private void AddTime(int minutes)
        {
            int before = Game1.timeOfDay;
            int result = this.Logic.AddMinutes(minutes);
            if (result < 0)
            {
                this.ToastFor("msg.time_limit");
                return;
            }
            this.State.SetHour = Game1.timeOfDay / 100;
            this.State.SetMinute = Game1.timeOfDay % 100;
            this.ToastFor("msg.time_changed_fmt", FormatClock(before), FormatClock(Game1.timeOfDay));
        }

        private void ApplySetDate()
        {
            Game1.year = Math.Clamp(this.State.SetYear, 1, 99);
            Game1.currentSeason = SeasonKey(this.State.SetSeasonIndex);
            Game1.dayOfMonth = Math.Clamp(this.State.SetDay, 1, 28);
            this.ToastFor("msg.date_set_fmt",
                          I18n.T("unit.year_fmt", Game1.year),
                          I18n.Season(Game1.currentSeason),
                          I18n.T("unit.day_fmt", Game1.dayOfMonth));
            Sound.Play(Sound.MenuConfirm);
        }

        private void ApplyWeather() => this.SetWeather(this.State.WeatherKind);

        private void SetWeather(int kind)
        {
            this.State.WeatherKind = kind;
            bool ok = this.Weather.Apply(kind, this.State.WeatherTomorrow);
            if (!ok)
            {
                this.ToastFor("msg.weather_fail");
                return;
            }
            string suffix = this.State.WeatherTomorrow
                ? I18n.T("msg.weather_with_tomorrow")
                : I18n.T("msg.weather_only_today");
            this.ToastFor("msg.weather_set_fmt", WeatherControl.Names[kind] + suffix);
        }

        private void SaveGame()
        {
            try
            {
                Game1.activeClickableMenu = new SaveGameMenu();
                this.ToastFor("msg.saving");
            }
            catch (Exception ex)
            {
                this.ToastFor("msg.save_fail_fmt", ex.Message);
            }
        }

        private void ReadCurrentTile()
        {
            if (Game1.player == null)
                return;
            this.State.TeleportTileX = (int)Game1.player.Tile.X;
            this.State.TeleportTileY = (int)Game1.player.Tile.Y;
        }

        private void Teleport()
        {
            Farmer? player = Game1.player;
            if (player == null)
                return;
            player.Position = new Vector2(this.State.TeleportTileX * 64f, this.State.TeleportTileY * 64f);
            Sound.Play(Sound.Wand);
        }

        // ------------------------------------------------------------------
        // 输入
        // ------------------------------------------------------------------
        public void MoveSelection(int delta)
        {
            if (this.Rows.Count == 0)
                return;
            int idx = this.Selected;
            for (int i = 0; i < this.Rows.Count; i++)
            {
                idx += delta;
                if (idx < 0)
                    idx = this.Rows.Count - 1;
                if (idx >= this.Rows.Count)
                    idx = 0;
                if (this.Rows[idx].Selectable)
                {
                    this.Selected = idx;
                    this.EnsureVisible();
                    Sound.Play(Sound.MenuMove);
                    return;
                }
            }
        }

        public void BeginHold(int direction)
        {
            this._holdRow = this.Selected;
            this._holdDir = direction;
            this._holdTicks = 0;
            this.AdjustSelection(direction);
        }

        public void EndHold()
        {
            this._holdRow = null;
            this._holdTicks = 0;
        }

        public void Tick()
        {
            if (this._holdRow == null)
                return;
            if (this._holdRow != this.Selected)
            {
                this.EndHold();
                return;
            }

            this._holdTicks++;
            if (this._holdTicks < HoldDelayTicks)
                return;

            int interval = this._holdTicks >= HoldFastAfterTicks ? 2 : 6;
            if ((this._holdTicks - HoldDelayTicks) % interval != 0)
                return;

            this.AdjustSelection(this._holdDir);
        }

        public void AdjustSelection(int direction)
        {
            if (this.Selected < 0 || this.Selected >= this.Rows.Count)
                return;
            TrainerRow row = this.Rows[this.Selected];
            if (row.Adjust != null)
            {
                row.Adjust(direction);
                Sound.Play(Sound.MenuMove);
            }
            else if (row.Activate != null && direction > 0)
            {
                row.Activate();
            }
        }

        public void ActivateSelection()
        {
            if (this.Selected < 0 || this.Selected >= this.Rows.Count)
                return;
            TrainerRow row = this.Rows[this.Selected];
            if (row.Activate != null)
                row.Activate();
            else if (row.Adjust != null)
                row.Adjust(1);
        }

        private const int ScrollRows = 1;

        public void ScrollBy(int direction)
        {
            if (direction == 0 || this.Rows.Count == 0)
                return;

            int sign = Math.Sign(direction);
            int idx = this.Selected;
            for (int i = 0; i < ScrollRows; i++)
                idx = Wrap(idx + sign, this.Rows.Count);

            int guard = 0;
            while (guard++ < this.Rows.Count && !this.Rows[idx].Selectable)
                idx = Wrap(idx + sign, this.Rows.Count);

            if (this.Rows[idx].Selectable)
            {
                this.Selected = idx;
                this.EnsureVisible();
                this.EndHold();
                Sound.Play(Sound.MenuMove);
            }
        }

        public void ClickAt(int screenX, int screenY)
        {
            int tab = this.TabIndexAt(screenX, screenY);
            if (tab >= 0)
            {
                this.SwitchTab(tab);
                return;
            }

            int listTop = this.yPositionOnScreen + ListTopOffset;
            if (screenY < listTop)
                return;
            int index = this.Scroll + (screenY - listTop) / RowHeight;
            if (index < 0 || index >= this.Rows.Count || !this.Rows[index].Selectable)
                return;

            if (index == this.Selected)
                this.ActivateSelection();
            else
            {
                this.Selected = index;
                this.EndHold();
                Sound.Play(Sound.MenuMove);
            }
        }

        /// <summary>按文案选中某一行（自检用）。</summary>
        public bool SelectRowByLabelContains(string text)
        {
            for (int i = 0; i < this.Rows.Count; i++)
            {
                if (this.Rows[i].Selectable
                    && this.Rows[i].CurrentLabel.Contains(text, StringComparison.Ordinal))
                {
                    this.Selected = i;
                    this.EnsureVisible();
                    this.EndHold();
                    return true;
                }
            }
            return false;
        }

        private void EnsureVisible()
        {
            int visible = this.VisibleRows;
            if (this.Selected < this.Scroll)
                this.Scroll = this.Selected;
            else if (this.Selected >= this.Scroll + visible)
                this.Scroll = this.Selected - visible + 1;
            this.Scroll = Math.Clamp(this.Scroll, 0, Math.Max(0, this.Rows.Count - visible));
        }

        // ------------------------------------------------------------------
        // 绘制
        // ------------------------------------------------------------------
        public override void draw(SpriteBatch b)
        {
            this.Layout();

            DrawRect(b, new Rectangle(0, 0, Game1.uiViewport.Width, Game1.uiViewport.Height),
                new Color(0, 0, 0, 95));

            var panel = new Rectangle(this.xPositionOnScreen, this.yPositionOnScreen, this.width, this.height);
            DrawRect(b, new Rectangle(panel.X + 5, panel.Y + 5, panel.Width, panel.Height), ColShadow);
            DrawRect(b, panel, ColBack);
            DrawBorder(b, panel, ColBorder, 2);

            DrawRect(b, new Rectangle(panel.X + 2, panel.Y + 2, panel.Width - 4, TitleBarHeight - 2), ColTitle);
            DrawText(b, I18n.T("title"), new Vector2(panel.X + PadX, panel.Y + 6), ColBorder, 0.95f);
            string ver = "v" + this.ModVersion;
            DrawText(b, ver, new Vector2(panel.Right - PadX - MeasureText(ver, 0.7f).X, panel.Y + 16),
                ColValueOn, 0.7f);

            DrawRect(b, new Rectangle(panel.X + 2, panel.Y + TitleBarHeight,
                panel.Width - 4, TabBarHeight - 2), ColTabBar);
            int mouseX = Game1.getMouseX();
            int mouseY = Game1.getMouseY();
            for (int i = 0; i < TabKeys.Length; i++)
            {
                Rectangle r = this.TabRect(i);
                bool active = i == this._tab;
                bool hover = r.Contains(mouseX, mouseY);
                DrawRect(b, r, active ? ColTabActive : (hover ? ColHover : ColTabIdle));
                string name = I18n.T(TabKeys[i]);
                Vector2 size = MeasureText(name, 0.8f);
                DrawText(b, name, new Vector2(r.Center.X - size.X / 2, r.Y + 6),
                    active ? Color.White : ColText, 0.8f);
            }

            b.End();
            b.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.PointClamp, null, null);

            int listTop = panel.Y + ListTopOffset;
            int visible = this.VisibleRows;
            for (int i = 0; i < visible; i++)
            {
                int rowIndex = this.Scroll + i;
                if (rowIndex >= this.Rows.Count)
                    break;
                TrainerRow row = this.Rows[rowIndex];
                var rowRect = new Rectangle(panel.X + 4, listTop + i * RowHeight, panel.Width - 8, RowHeight);
                bool isSelected = rowIndex == this.Selected;
                bool isHover = rowRect.Contains(mouseX, mouseY) && row.Selectable;

                if (isSelected)
                    DrawRect(b, rowRect, ColSelected);
                else if (isHover)
                    DrawRect(b, rowRect, ColHover);

                Color labelColor = row.Kind == RowKind.Section
                    ? ColSection
                    : (isSelected ? Color.White : ColText);
                DrawText(b, row.CurrentLabel, new Vector2(rowRect.X + PadX - 4, rowRect.Y + 6), labelColor,
                    row.Kind == RowKind.Section ? 0.78f : 0.75f);

                if (row.GetValue != null)
                {
                    string value = row.GetValue();
                    Color valueColor = ColValueOff;
                    if (row.Kind == RowKind.Toggle)
                        valueColor = value.Contains(I18n.T("value.on")) ? ColValueOn : ColValueOff;
                    else if (row.Kind == RowKind.Number)
                        valueColor = ColWarn;
                    else if (row.Kind == RowKind.Action)
                        valueColor = isSelected ? ColValueOn : new Color(200, 200, 120);

                    float scale = 0.75f;
                    Vector2 size = MeasureText(value, scale);
                    DrawText(b, value,
                        new Vector2(rowRect.Right - PadX - size.X, rowRect.Y + 6), valueColor, scale);
                }
            }

            if (this.Toast != null)
            {
                this._toastTicks++;
                if (this._toastTicks > 240)
                    this.Toast = null;
                else
                {
                    Vector2 ts = MeasureText(this.Toast, 0.78f);
                    DrawText(b, this.Toast,
                        new Vector2(panel.Center.X - ts.X / 2, listTop - 26),
                        new Color(255, 226, 140), 0.78f);
                }
            }

            DrawRect(b, new Rectangle(panel.X + 2, panel.Bottom - FooterHeight,
                panel.Width - 4, FooterHeight - 2), ColFooter);
            DrawText(b, $"{this.Selected + 1}/{this.Rows.Count}",
                new Vector2(panel.X + PadX, panel.Bottom - FooterHeight + 8), ColHint, 0.62f);
            string hint = I18n.T("hint.bar");
            Vector2 hs = MeasureText(hint, 0.66f);
            DrawText(b, hint,
                new Vector2(panel.Center.X - hs.X / 2 + 30, panel.Bottom - FooterHeight + 8), ColText, 0.66f);

            base.draw(b);
            this.drawMouse(b);
        }

        private static void DrawRect(SpriteBatch b, Rectangle rect, Color color)
        {
            b.Draw(Game1.staminaRect, rect, color);
        }

        private static void DrawBorder(SpriteBatch b, Rectangle rect, Color color, int thickness)
        {
            DrawRect(b, new Rectangle(rect.X, rect.Y, rect.Width, thickness), color);
            DrawRect(b, new Rectangle(rect.X, rect.Bottom - thickness, rect.Width, thickness), color);
            DrawRect(b, new Rectangle(rect.X, rect.Y, thickness, rect.Height), color);
            DrawRect(b, new Rectangle(rect.Right - thickness, rect.Y, thickness, rect.Height), color);
        }

        private static Vector2 MeasureText(string text, float scale)
        {
            return Game1.dialogueFont.MeasureString(text) * scale;
        }

        private static void DrawText(SpriteBatch b, string text, Vector2 position, Color color, float scale)
        {
            b.DrawString(Game1.dialogueFont, text, position, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 1f);
        }
    }
}
