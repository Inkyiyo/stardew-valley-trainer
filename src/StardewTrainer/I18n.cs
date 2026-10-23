using System;
using System.Collections.Generic;
using StardewModdingAPI;

namespace StardewTrainer
{
    /// <summary>
    /// 文案表。所有界面文字都从 i18n/*.json 读取，想翻译成别的语言只要照抄一份改值。
    /// 加载顺序：i18n/&lt;游戏语言&gt;.json → i18n/default.json → 内置兜底（键名本身）。
    /// </summary>
    public static class I18n
    {
        private static readonly Dictionary<string, string> Strings = new(StringComparer.OrdinalIgnoreCase);
        private static IMonitor? _monitor;

        /// <summary>当前用的语言文件（给「关于」页显示）。</summary>
        public static string LoadedFile { get; private set; } = "(未加载)";

        public static void Init(IModHelper helper, IMonitor monitor)
        {
            _monitor = monitor;
            string locale = "default";
            try
            {
                locale = helper.Translation.LocaleEnum.ToString().ToLowerInvariant();
            }
            catch
            {
                // 拿不到就退回 default
            }

            // 先铺一层 default，再用当前语言覆盖，这样缺哪个键都不会空着
            if (!TryLoad(helper, "default"))
                monitor.Log("找不到 i18n/default.json，界面会退化成显示键名。", LogLevel.Warn);

            if (locale != "default" && locale != "en")
            {
                if (TryLoad(helper, locale))
                    LoadedFile = locale;
            }
        }

        private static bool TryLoad(IModHelper helper, string name)
        {
            try
            {
                Dictionary<string, string>? data = helper.Data.ReadJsonFile<Dictionary<string, string>>($"i18n/{name}.json");
                if (data == null)
                    return false;

                foreach (KeyValuePair<string, string> kv in data)
                {
                    if (kv.Key.StartsWith("_", StringComparison.Ordinal))
                        continue;                       // _comment 之类的说明
                    Strings[kv.Key] = kv.Value;
                }
                LoadedFile = name;
                return true;
            }
            catch (Exception ex)
            {
                _monitor?.Log($"读取 i18n/{name}.json 失败：{ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>取文案；没有这个键就返回键名本身（方便一眼看出漏翻了什么）。</summary>
        public static string T(string key)
        {
            return Strings.TryGetValue(key, out string? value) && !string.IsNullOrEmpty(value) ? value : key;
        }

        public static string T(string key, params object?[] args)
        {
            string format = T(key);
            try
            {
                return string.Format(format, args);
            }
            catch (FormatException)
            {
                return format;                          // 翻译写坏了也不要崩
            }
        }

        /// <summary>季节键转显示名（spring -> 春）。</summary>
        public static string Season(string key)
        {
            return key switch
            {
                "spring" => T("season.spring"),
                "summer" => T("season.summer"),
                "fall" or "autumn" => T("season.fall"),
                "winter" => T("season.winter"),
                _ => key
            };
        }
    }
}
