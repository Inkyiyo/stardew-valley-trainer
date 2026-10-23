using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buffs;
using StardewValley.Menus;

namespace StardewTrainer
{
    /// <summary>玩家技能的类型（对应 Farmer.experiencePoints 的下标）。</summary>
    public enum TrainerSkill
    {
        Farming = 0,
        Fishing = 1,
        Foraging = 2,
        Mining = 3,
        Combat = 4,
        Luck = 5
    }

    /// <summary>所有修改项的实时状态。数值型项目直接保存在这里。</summary>
    public class CheatState
    {
        // ---- 开关型 ----
        public bool InfiniteStamina;
        public bool InfiniteHealth;
        public bool OneHitKill;
        public bool NoMonsters;
        public bool InstantCharge;
        public bool TimeFreeze;
        public bool AlwaysMaxLuck;

        // ---- 新增：工具 / 钓鱼 / 好感度 ----
        /// <summary>无限浇水器（水壶水量始终是满的）。</summary>
        public bool InfiniteWater;

        /// <summary>钓鱼瞬间咬钩。</summary>
        public bool InstantBite;

        /// <summary>钓鱼瞬间捕获（收杆进度直接拉满）。</summary>
        public bool InstantCatch;

        /// <summary>钓鱼难度归零（鱼不动，条不抖）。</summary>
        public bool EasyFishing;

        // ---- 数值型 ----
        public float SpeedMultiplier = 1f;
        public float TimeScale = 1f;
        public int MoneyStep = 10000;

        /// <summary>体力/生命的自动填充下限（百分比）。必须是 0，否则会盖过开关。</summary>
        public int StaminaFillPercent;
        public int HealthFillPercent;

        public int LuckLevel = 10;
        public int DailyLuckPercent = 10;
        public int SkillTargetLevel = 10;
        public int TeleportTileX = 10;
        public int TeleportTileY = 10;

        /// <summary>菜单里选中的天气（取值见 WeatherControl 常量）。</summary>
        public int WeatherKind = WeatherControl.Sunny;

        /// <summary>改天气时是否连明天一起改。</summary>
        public bool WeatherTomorrow;

        // ---- 时间 / 日期（菜单里可调，点「应用」才生效）----
        public int SetHour = 6;
        public int SetMinute;

        /// <summary>时间按 10 分钟推进时的步长（游戏时间只有 10 分钟粒度）。</summary>
        public int TimeStepMinutes = 10;

        public int SetYear = 1;

        /// <summary>0=春 1=夏 2=秋 3=冬。</summary>
        public int SetSeasonIndex;

        public int SetDay = 1;

        /// <summary>启动时的基础移动速度（用于换算倍率）。</summary>
        public float BaseSpeed = 2f;
    }

    /// <summary>技能等级表：Stardew Valley 的每级经验门槛。</summary>
    public static class SkillTable
    {
        /// <summary>第 N 级所需的累计经验（index 0 = 1级）。</summary>
        public static readonly int[] LevelXp = { 100, 380, 770, 1300, 2150, 3300, 4800, 6900, 10000, 15000 };

        public static readonly string[] SkillNames =
        {
            "耕种", "钓鱼", "采集", "采矿", "战斗", "幸运"
        };

        /// <summary>把累计经验换算成等级（0-10）。</summary>
        public static int GetLevel(int xp)
        {
            for (int i = LevelXp.Length - 1; i >= 0; i--)
            {
                if (xp >= LevelXp[i])
                    return i + 1;
            }
            return 0;
        }

        /// <summary>把等级换算成该级的最低累计经验。</summary>
        public static int GetXpForLevel(int level)
        {
            if (level <= 0)
                return 0;
            if (level > LevelXp.Length)
                level = LevelXp.Length;
            return LevelXp[level - 1];
        }

        /// <summary>取某个技能当前的等级。</summary>
        public static int GetPlayerLevel(Farmer player, TrainerSkill skill)
        {
            if (player == null || player.experiencePoints == null)
                return 0;
            int idx = (int)skill;
            int xp = idx < player.experiencePoints.Count ? player.experiencePoints[idx] : 0;
            return GetLevel(xp);
        }

        /// <summary>设置某个技能的等级（写经验值）。</summary>
        public static void SetPlayerLevel(Farmer player, TrainerSkill skill, int level)
        {
            if (player == null || player.experiencePoints == null)
                return;
            int idx = (int)skill;
            while (player.experiencePoints.Count <= idx)
                player.experiencePoints.Add(0);
            player.experiencePoints[idx] = GetXpForLevel(level);
        }
    }

    /// <summary>菜单里一行的类型。</summary>
    public enum RowKind
    {
        Section,    // 分组标题，不可点
        Toggle,     // 开关
        Number,     // 数值调节
        Action,     // 执行一次的动作
        Readonly    // 只读显示
    }

    /// <summary>菜单的一行。</summary>
    public class TrainerRow
    {
        public RowKind Kind;
        public string Label = "";
        public Func<string>? GetValue;
        public Action? Activate;
        public Action<int>? Adjust;   // 参数是方向：-1 / +1

        /// <summary>需要动态文案时用（例如带上物品名）。给了它就以它为准。</summary>
        public Func<string>? GetLabel;

        /// <summary>实际显示的文字。</summary>
        public string CurrentLabel => this.GetLabel != null ? this.GetLabel() : this.Label;

        public bool Selectable => Kind != RowKind.Section && Kind != RowKind.Readonly;
    }

    /// <summary>实时修改逻辑本体。</summary>
    public class Cheats
    {
        private const string LuckBuffId = "DeepSeekHarness.TrainerLuck";
        private const string SpeedBuffId = "DeepSeekHarness.TrainerSpeed";

        /// <summary>打开后会把速度 buff 的每一步决策写进日志（排错用，正常游戏保持关闭）。</summary>
        public static bool DebugSpew;

        private readonly IMonitor Monitor;

        public Cheats(IMonitor monitor)
        {
            this.Monitor = monitor;
        }

        /// <summary>每 tick 应用一次实时修改。</summary>
        public void Apply(CheatState s)
        {
            Farmer? player = Game1.player;
            if (player == null)
                return;

            // ---- 无限体力 ----
            // 注意：填充比例默认必须是 0，否则「无限体力」会永远处于开启状态而关不掉（曾经的 bug）
            if (s.InfiniteStamina)
            {
                if (player.Stamina < player.MaxStamina)
                    player.Stamina = player.MaxStamina;
            }
            else if (s.StaminaFillPercent > 0)
            {
                float target = player.MaxStamina * (s.StaminaFillPercent / 100f);
                if (player.Stamina < target)
                    player.Stamina = target;
            }

            // ---- 无限生命 / 生命填充 ----
            if (s.InfiniteHealth)
            {
                if (player.health < player.maxHealth)
                    player.health = player.maxHealth;
            }
            else if (s.HealthFillPercent > 0)
            {
                int target = (int)(player.maxHealth * (s.HealthFillPercent / 100f));
                if (player.health < target)
                    player.health = target;
            }

            // ---- 移动速度 ----
            // 关键：不要直接写 player.addedSpeed。游戏每帧会从 buff 系统重算这个值，
            // 直接写会被立刻覆盖（这就是"改了速度没反应"的原因）。
            // 正确做法是走游戏自己的通道：加一个 Speed 效果的 buff（咖啡/食物就是这么做的）。
            this.ApplySpeedBuff(player, s);

            // ---- 幸运 ----
            if (s.AlwaysMaxLuck)
                this.ApplyLuckBuff(player, s.LuckLevel);

            // ---- 无限浇水器 ----
            if (s.InfiniteWater)
                this.ApplyInfiniteWater(player);

            // ---- 钓鱼相关 ----
            if (s.InstantBite || s.InstantCatch || s.EasyFishing)
                this.ApplyFishing(s);

            // ---- 时间 ----
            if (s.TimeFreeze)
            {
                // 把游戏时间往回拨回整十分钟的起点，等效于时间静止
                int now = Game1.timeOfDay;
                int minutes = now % 100;
                if (minutes != 0)
                    Game1.timeOfDay = now - minutes;
                if (Game1.gameTimeInterval > 100)
                    Game1.gameTimeInterval = 0;
            }
        }

        /// <summary>
        /// 移动速度：靠 buff 生效。倍率 1.0 = 不加 buff（完全还原原版）。
        /// 用倍率而不是固定值，玩家自己的咖啡/食物加速依然会被游戏叠加进来。
        /// </summary>
        private void ApplySpeedBuff(Farmer player, CheatState s)
        {
            if (player.buffs == null)
                return;

            try
            {
                float bonus = s.BaseSpeed * (s.SpeedMultiplier - 1f);

                bool applied = player.buffs.IsApplied(SpeedBuffId);
                if (DebugSpew)
                    this.Monitor.Log(
                        $"[速度] 倍率={s.SpeedMultiplier:0.0} base={s.BaseSpeed:0.0} bonus={bonus:0.0} " +
                        $"已应用={applied} buff数={player.buffs.AppliedBuffs.Count} addedSpeed={player.addedSpeed:0.0}",
                        LogLevel.Info);

                if (Math.Abs(bonus) < 0.001f)
                {
                    if (applied)
                        player.buffs.Remove(SpeedBuffId);
                    return;
                }

                float value = bonus;
                if (applied)
                {
                    if (player.buffs.AppliedBuffs.TryGetValue(SpeedBuffId, out Buff? cur)
                        && cur != null
                        && Math.Abs(cur.effects.Speed.Value - value) < 0.001f)
                        return;                       // 已经是目标值，不折腾
                    player.buffs.Remove(SpeedBuffId);
                }

                var effects = new BuffEffects();
                effects.Speed.Value = value;

                var buff = new Buff(
                    id: SpeedBuffId,
                    source: "StardewTrainer",
                    displaySource: "修改器",
                    duration: Buff.ENDLESS,
                    iconTexture: null,
                    iconSheetIndex: 0,
                    effects: effects,
                    isDebuff: false,
                    displayName: "修改器加速",
                    description: $"移动速度 ×{s.SpeedMultiplier:0.0}");
                buff.visible = false;                 // 不在 HUD 上刷图标
                player.buffs.Apply(buff);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("速度 buff 应用失败：" + ex.Message, LogLevel.Warn);
            }
        }

        /// <summary>把当前手持的水壶灌满（含喷壶附魔状态）。</summary>
        private void ApplyInfiniteWater(Farmer player)
        {
            try
            {
                if (player.CurrentTool is not StardewValley.Tools.WateringCan can)
                    return;
                // IsBottomless 是游戏自己的「无限水」标志，和水量一起写最稳
                if (!can.IsBottomless)
                    can.IsBottomless = true;
                if (can.WaterLeft < can.waterCanMax)
                    can.WaterLeft = can.waterCanMax;
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("无限浇水器失败：" + ex.Message, LogLevel.Warn);
            }
        }

        /// <summary>
        /// 钓鱼：瞬间咬钩 / 瞬间捕获 / 降低难度。
        /// 咬钩阶段看 FishingRod.timeUntilFishingBite，收杆阶段看 BobberBar.distanceFromCatching。
        /// </summary>
        private void ApplyFishing(CheatState s)
        {
            try
            {
                // ---- 咬钩 ----
                if (s.InstantBite && Game1.player.CurrentTool is StardewValley.Tools.FishingRod rod)
                {
                    if (rod.isFishing && !rod.isNibbling && !rod.isReeling
                        && !rod.castedButBobberStillInAir && !rod.hadBobber)
                    {
                        rod.timeUntilFishingBite = 0.01f;
                        rod.fishingBiteAccumulator = 0f;
                        rod.timeUntilFishingNibbleDone = 0.01f;
                    }
                }

                // ---- 收杆小游戏 ----
                if (Game1.activeClickableMenu is not StardewValley.Menus.BobberBar bar)
                    return;

                if (s.EasyFishing)
                {
                    bar.difficulty = 0f;
                    bar.distanceFromCatchPenaltyModifier = 0f;
                }

                if (s.InstantCatch)
                {
                    bar.distanceFromCatching = 1f;
                    bar.treasureCatchLevel = 1f;
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("钓鱼修改失败：" + ex.Message, LogLevel.Warn);
            }
        }

        /// <summary>
        /// 把某个 NPC 的好感度拉满。已离异的不动，否则会把关系状态搞乱。
        /// 返回实际调整的人数。
        /// </summary>
        public int MaxAllFriendships()
        {
            Farmer? player = Game1.player;
            if (player == null)
                return 0;

            int changed = 0;
            try
            {
                foreach (KeyValuePair<string, Friendship> kv in player.friendshipData.Pairs)
                {
                    Friendship? f = kv.Value;
                    if (f == null)
                        continue;
                    if (f.Status == FriendshipStatus.Divorced)
                        continue;                      // 别把前任复活成好朋友
                    if (f.Points < MaxFriendshipPoints)
                    {
                        f.Points = MaxFriendshipPoints;
                        changed++;
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("拉满好感度失败：" + ex.Message, LogLevel.Warn);
            }
            return changed;
        }

        /// <summary>好感度上限（满心）。</summary>
        public const int MaxFriendshipPoints = 2500;

        /// <summary>应用幸运 buff（满级幸运）。相同 ID 会自替换，不会叠加。</summary>
        private void ApplyLuckBuff(Farmer player, int luckLevel)
        {
            if (luckLevel <= 0 || player.buffs == null)
                return;
            try
            {
                if (player.buffs.IsApplied(LuckBuffId))
                {
                    // 已经在生效：等级没变就直接返回，变了就换掉
                    if (player.buffs.AppliedBuffs.TryGetValue(LuckBuffId, out Buff? current)
                        && current != null
                        && Math.Abs(current.effects.LuckLevel.Value - luckLevel) < 0.001f)
                        return;
                    player.buffs.Remove(LuckBuffId);
                }

                var effects = new BuffEffects();
                effects.LuckLevel.Value = luckLevel;
                var buff = new Buff(
                    id: LuckBuffId,
                    source: "StardewTrainer",
                    displaySource: "修改器",
                    duration: Buff.ENDLESS,
                    iconTexture: null,
                    iconSheetIndex: 0,
                    effects: effects,
                    isDebuff: false,
                    displayName: "修改器幸运",
                    description: $"幸运 +{luckLevel}");
                buff.visible = true;
                player.buffs.Apply(buff);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("幸运 buff 应用失败：" + ex.Message, LogLevel.Warn);
            }
        }

        /// <summary>一击必杀（怪物血量直接打到 0）。</summary>
        public void KillMonsters()
        {
            int killed = 0;
            Netcode.NetCollection<NPC>? characters = Game1.currentLocation?.characters;
            if (characters == null)
                return;

            foreach (NPC npc in characters.ToList())
            {
                if (npc is StardewValley.Monsters.Monster monster)
                {
                    monster.Health = 0;
                    killed++;
                }
            }
            if (killed > 0)
                this.Monitor.Log($"一击必杀：清理了 {killed} 只怪物。", LogLevel.Info);
        }

        /// <summary>播放一个提示音（音效名不存在时自动静音，不会刷错误日志）。</summary>
        public void PlayCue(string cueName)
        {
            Sound.Play(cueName);
        }

        // ------------------------------------------------------------------
        // 时钟
        // ------------------------------------------------------------------
        /// <summary>
        /// 设置游戏时钟。timeOfDay 是「小时 * 100 + 分钟」（1830 = 18:30），
        /// 游戏内部只有 10 分钟粒度，所以分钟会对齐到 10 的倍数。
        /// </summary>
        public void SetClock(int hour, int minute)
        {
            hour = Math.Clamp(hour, 6, 26);                 // 06:00 ~ 次日 02:00
            minute = Math.Clamp(minute / 10 * 10, 0, 50);
            int value = hour * 100 + minute;

            Game1.timeOfDay = value;
            Game1.timeOfDayAfterFade = value;
            if (Game1.gameTimeInterval > 0)
                Game1.gameTimeInterval = 0;
        }

        /// <summary>
        /// 把时间推进（或回退）若干分钟。跨过凌晨 2:00 的部分会被丢掉，
        /// 因为再往后是「昏倒结算」，直接改时钟跳过去会破坏存档状态。
        /// 返回新的 timeOfDay；如果因为会跨过 06:00 下限而无法回退，返回 -1。
        /// </summary>
        public int AddMinutes(int minutes)
        {
            int total = (Game1.timeOfDay / 100) * 60 + Game1.timeOfDay % 100 + minutes;

            const int dayStart = 6 * 60;      // 06:00
            const int dayEnd = 26 * 60;       // 次日 02:00

            if (total < dayStart)
                return -1;
            if (total > dayEnd)
                total = dayEnd;

            this.SetClock(total / 60, total % 60);
            return Game1.timeOfDay;
        }
    }
}
