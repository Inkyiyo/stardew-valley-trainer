using System;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Network;

namespace StardewTrainer
{
    /// <summary>
    /// 天气控制。1.6 的天气是「按地点」+「全局」两套，要同时写才不会出现
    /// 视觉在下雨但作物没被浇 / 明天又变回去这种半生效状态。
    /// </summary>
    public class WeatherControl
    {

        /// <summary>世界是否已加载（兼容 SaveGame.Load 路径）。</summary>
        private static bool WorldReady => ModEntry.WorldReady;
        public const int Sunny = 0;
        public const int Rainy = 1;
        public const int Stormy = 2;
        public const int Snowy = 3;
        public const int Windy = 4;
        public const int GreenRain = 5;

        public static readonly string[] Names =
        {
            "晴天", "雨天", "雷雨", "下雪", "大风(落叶)", "绿雨"
        };

        private readonly IMonitor Monitor;

        public WeatherControl(IMonitor monitor)
        {
            this.Monitor = monitor;
        }

        /// <summary>把天气同时应用到当前地点、明天、以及全局标志。</summary>
        public bool Apply(int kind, bool alsoTomorrow)
        {
            if (!ModEntry.WorldReady || Game1.currentLocation == null)
            {
                this.Monitor.Log("请先进入存档再改天气。", LogLevel.Warn);
                return false;
            }

            try
            {
                string weather = kind switch
                {
                    Sunny => Game1.weather_sunny,
                    Rainy => Game1.weather_rain,
                    Stormy => Game1.weather_lightning,
                    Snowy => Game1.weather_snow,
                    Windy => Game1.weather_debris,
                    GreenRain => Game1.weather_green_rain,
                    _ => Game1.weather_sunny
                };

                bool rain = kind == Rainy || kind == Stormy;
                bool storm = kind == Stormy;
                bool snow = kind == Snowy;
                bool wind = kind == Windy;
                bool green = kind == GreenRain;

                // ---- 1. 全局标志（决定雨滴/雪花渲染和作物浇水判定）----
                // 千万不要碰 Game1.rainDrops / Game1.debrisWeather 这两个数组：
                // 它们由游戏按固定长度维护（rainLoopLength），一旦被 Clear 或替换成空数组，
                // 游戏自己的 updateWeather() 就会抛 IndexOutOfRangeException，把主循环搞崩。
                Game1.isRaining = rain;
                Game1.isLightning = storm;
                Game1.isSnowing = snow;
                Game1.isDebrisWeather = wind;
                Game1.isGreenRain = green;

                Game1.updateWeatherIcon();

                // ---- 2. 当前地点的天气（真正决定这张地图下不下雨）----
                // 注意：不要写 Game1.netWorldState 里的 isRaining 等字段，它们是私有的；
                // LocationWeather 才是公开可写的那一层。
                LocationWeather lw = Game1.currentLocation.GetWeather();
                lw.Weather = weather;
                lw.IsRaining = rain;
                lw.IsSnowing = snow;
                lw.IsLightning = storm;
                lw.IsDebrisWeather = wind;
                lw.IsGreenRain = green;
                if (alsoTomorrow)
                    lw.WeatherForTomorrow = weather;

                // ---- 3. 明天（写公开属性，不碰私有字段）----
                if (alsoTomorrow)
                {
                    Game1.weatherForTomorrow = weather;
                    NetWorldState? ws = Game1.netWorldState?.Value;
                    if (ws != null)
                        ws.WeatherForTomorrow = weather;
                }

                this.Monitor.Log(
                    $"天气已改为「{Names[kind]}」" + (alsoTomorrow ? "，并且明天也是。" : "（只改今天）"),
                    LogLevel.Info);
                return true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"改天气失败：{ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>读出当前的天气名，给菜单显示用。</summary>
        public string Describe()
        {
            try
            {
                if (Game1.isGreenRain)
                    return Names[GreenRain];
                if (Game1.isLightning || Game1.isRaining)
                    return Game1.isLightning ? Names[Stormy] : Names[Rainy];
                if (Game1.isSnowing)
                    return Names[Snowy];
                if (Game1.isDebrisWeather)
                    return Names[Windy];
                return Names[Sunny];
            }
            catch
            {
                return "-";
            }
        }
    }
}
