using StardewModdingAPI;
using StardewModdingAPI.Utilities;

namespace StardewTrainer
{
    /// <summary>修改器的可配置项（可在 SMAPI 的 config.json 或通用模组配置菜单里改）。</summary>
    public class ModConfig
    {
        /// <summary>呼出悬浮菜单的按键。</summary>
        public KeybindList MenuKey { get; set; } = KeybindList.Parse("F5");

        /// <summary>开关「无限体力」的快捷键。</summary>
        public KeybindList InfiniteStaminaKey { get; set; } = KeybindList.Parse("F6");

        /// <summary>开关「无限生命」的快捷键。</summary>
        public KeybindList InfiniteHealthKey { get; set; } = KeybindList.Parse("F7");

        /// <summary>提高移动速度倍率的快捷键。</summary>
        public KeybindList SpeedUpKey { get; set; } = KeybindList.Parse("F8");

        /// <summary>降低移动速度倍率的快捷键。</summary>
        public KeybindList SpeedDownKey { get; set; } = KeybindList.Parse("F9");

        /// <summary>速度倍率的调节步长。</summary>
        public float SpeedStep { get; set; } = 0.1f;

        /// <summary>速度倍率允许的最大值。</summary>
        public float MaxSpeedMultiplier { get; set; } = 10f;

        /// <summary>金钱上限（游戏内建上限约 21 亿，超过会溢出成负数）。</summary>
        public int MaxMoney { get; set; } = 2000000000;

        /// <summary>打开菜单时是否暂停游戏（雷霆修改器那种不暂停；暂停更安全）。</summary>
        public bool PauseWhileMenuOpen { get; set; } = true;

        /// <summary>静音修改器的提示音（如果音效 ID 不兼容可以打开）。</summary>
        public bool MuteSounds { get; set; }

        /// <summary>启动游戏后是否自动显示一次操作提示。</summary>
        public bool ShowHintsOnLoad { get; set; } = true;
    }
}
