using System;
using StardewModdingAPI;
using StardewValley;

namespace StardewTrainer
{
    /// <summary>
    /// 音效。游戏只认识 XACT 音效库里真实存在的 cue 名，写错一个就会往控制台刷红字
    /// （"Can't get audio ID 'xxx' because it doesn't exist."），所以这里只保留已确认存在的名字。
    /// 宁可没声音，也绝不刷错误日志。
    /// </summary>
    public static class Sound
    {
        // 下面每个名字都是从游戏程序集里反查出来的、确实被 Game1.playSound 用过的 cue。
        // 教训：不要凭印象写音效名，写错一个就会往控制台刷 "Can't get audio ID ..." 红字。
        public const string MenuMove = "smallSelect";
        public const string MenuConfirm = "bigDeSelect";
        public const string Coin = "coin";
        public const string Wand = "wand";
        public const string LevelUp = "reward";
        public const string Purchase = "purchase";
        public const string Pickup = "pickUpItem";
        public const string Error = "cancel";

        private static bool _muted;

        /// <summary>全局静音（config 里可关掉，或者检测到音效名不对时自动静音）。</summary>
        public static void SetMuted(bool muted)
        {
            _muted = muted;
        }

        public static bool IsMuted => _muted;

        /// <summary>播放提示音；任何异常都吞掉，绝不影响功能。</summary>
        public static void Play(string cue)
        {
            if (_muted)
                return;
            try
            {
                Game1.playSound(cue);
            }
            catch (Exception)
            {
                // 音效不存在 / 音频设备异常：静音掉，避免刷屏
                _muted = true;
            }
        }
    }
}
