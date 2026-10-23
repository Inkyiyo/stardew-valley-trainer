using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

// StardewValley.Object 和 System.Object 同名，这里明确指游戏的那个
using SObject = StardewValley.Object;

namespace StardewTrainer
{
    /// <summary>
    /// 一键采集：快速砍树、挖石头、敲矿石、割杂草、捡树枝、清大障碍。
    ///
    /// 走游戏自己的 performToolAction / performRemoveAction，
    /// 所以木材、石头、矿石、掉落物都是游戏正常给的，不会白清。
    /// 默认不动庄稼（HoeDirt 上的作物），免得把辛苦种的东西一起铲了。
    /// </summary>
    public class Harvest
    {
        private readonly IMonitor Monitor;

        public Harvest(IMonitor monitor)
        {
            this.Monitor = monitor;
        }

        /// <summary>工具只作为参数传给游戏，不需要装备在手上。</summary>
        private static Tool MakeAxe() => new Axe();
        private static Tool MakePickaxe() => new Pickaxe();

        // ==================================================================
        // 分类统计
        // ==================================================================

        /// <summary>树木数量（含树苗、树桩）。</summary>
        public int CountTrees(GameLocation? loc = null)
        {
            GameLocation? where = loc ?? Game1.currentLocation;
            if (where?.terrainFeatures == null)
                return 0;
            try { return where.terrainFeatures.Pairs.Count(p => p.Value is Tree); }
            catch { return 0; }
        }

        /// <summary>大障碍数量（大石头、大树桩、原木、陨石）。</summary>
        public int CountClumps(GameLocation? loc = null)
        {
            GameLocation? where = loc ?? Game1.currentLocation;
            if (where?.resourceClumps == null)
                return 0;
            try { return where.resourceClumps.Count; }
            catch { return 0; }
        }

        /// <summary>石头 / 矿石 / 杂草 / 树枝数量（不含庄稼）。</summary>
        public int CountDebris(GameLocation? loc = null)
        {
            GameLocation? where = loc ?? Game1.currentLocation;
            if (where?.objects == null)
                return 0;
            try { return this.CollectDebrisTiles(where).Count; }
            catch { return 0; }
        }

        /// <summary>当前地图可采集物总数（树 + 大障碍 + 石头杂草）。</summary>
        public int CountAll(GameLocation? loc = null) =>
            this.CountTrees(loc) + this.CountClumps(loc) + this.CountDebris(loc);

        // ==================================================================
        // 清理
        // ==================================================================

        /// <summary>砍掉所有树（含树桩）。返回棵数。</summary>
        public int CutAllTrees(GameLocation? loc = null)
        {
            GameLocation? where = loc ?? Game1.currentLocation;
            if (where?.terrainFeatures == null)
                return 0;

            // 先快照「格坐标 + 树对象」，避免边遍历边改集合
            var targets = new List<(Vector2 Tile, Tree Tree)>();
            try
            {
                foreach (KeyValuePair<Vector2, TerrainFeature> pair in where.terrainFeatures.Pairs)
                {
                    if (pair.Value is Tree tree)
                        targets.Add((pair.Key, tree));
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("扫描树木失败：" + ex.Message, LogLevel.Warn);
                return 0;
            }

            GameLocation target = where;
            Tool axe = MakeAxe();
            int done = 0;
            int fallback = 0;

            foreach ((Vector2 tile, Tree tree) in targets)
            {
                try
                {
                    // 让游戏自己处理倒下和掉落（木材 / 树液 / 种子）
                    tree.performToolAction(axe, 0, tile);
                }
                catch
                {
                    // 有些树（幼苗之类）走游戏逻辑会抛异常，直接删掉兜底
                    fallback++;
                }

                // 无论如何都要清干净：游戏没删就自己删
                try
                {
                    if (target.terrainFeatures.ContainsKey(tile)
                        && target.terrainFeatures[tile] == tree)
                        target.terrainFeatures.Remove(tile);
                    done++;
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce("移除树木失败：" + ex.Message, LogLevel.Warn);
                }
            }

            if (fallback > 0)
                this.Monitor.Log($"砍树：其中 {fallback} 棵走的是兜底删除（游戏逻辑对它们会报错）。", LogLevel.Trace);
            return done;
        }

        /// <summary>清掉所有大障碍（大石头、大树桩、原木、陨石）。返回个数。</summary>
        public int ClearClumps(GameLocation? loc = null)
        {
            GameLocation? where = loc ?? Game1.currentLocation;
            if (where?.resourceClumps == null)
                return 0;

            GameLocation target = where;
            Tool pickaxe = MakePickaxe();
            int done = 0;

            try
            {
                // 倒着删最稳，避免索引错位
                for (int i = target.resourceClumps.Count - 1; i >= 0; i--)
                {
                    ResourceClump clump = target.resourceClumps[i];
                    if (clump == null)
                        continue;
                    try
                    {
                        // damage 给大一点，保证一次敲掉
                        clump.performToolAction(pickaxe, 9999, clump.Tile);
                        done++;
                    }
                    catch (Exception ex)
                    {
                        this.Monitor.LogOnce("敲障碍出错（已跳过）：" + ex.Message, LogLevel.Warn);
                    }

                    // 万一游戏没自己移除，就手动删掉
                    if (target.resourceClumps.Contains(clump))
                        target.resourceClumps.Remove(clump);
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("清理障碍失败：" + ex.Message, LogLevel.Warn);
            }
            return done;
        }

        /// <summary>
        /// 敲掉石头、矿石、割掉杂草、清掉树枝。返回处理个数。
        /// 不碰庄稼和已经放好的物品（箱子之类）。
        /// </summary>
        public int ClearDebris(GameLocation? loc = null)
        {
            GameLocation? where = loc ?? Game1.currentLocation;
            if (where?.objects == null)
                return 0;

            // 快照「格坐标 + 对象」，避免边遍历边改
            var targets = new List<(Vector2 Tile, SObject Obj)>();
            try
            {
                foreach (KeyValuePair<Vector2, SObject> pair in where.objects.Pairs)
                {
                    if (IsDebris(pair.Value))
                        targets.Add((pair.Key, pair.Value));
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("扫描杂物失败：" + ex.Message, LogLevel.Warn);
                return 0;
            }

            GameLocation target = where;
            Tool axe = MakeAxe();
            Tool pickaxe = MakePickaxe();
            int done = 0;
            int fallback = 0;

            foreach ((Vector2 tile, SObject obj) in targets)
            {
                try
                {
                    // 石头 / 矿石用镐子，杂草 / 树枝用斧头；掉落由游戏正常给
                    obj.performToolAction(IsStoneLike(obj) ? pickaxe : axe);
                }
                catch
                {
                    fallback++;
                }

                // 兜底：游戏没移除就自己删，保证清干净
                try
                {
                    if (target.objects.ContainsKey(tile) && target.objects[tile] == obj)
                        target.objects.Remove(tile);
                    done++;
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce("移除杂物失败：" + ex.Message, LogLevel.Warn);
                }
            }

            if (fallback > 0)
                this.Monitor.Log($"清杂物：其中 {fallback} 个走的是兜底删除。", LogLevel.Trace);
            return done;
        }

        /// <summary>ClearAll 是 HarvestAll 的别名（命令里用得多）。</summary>
        public (int Trees, int Clumps, int Debris) ClearAll(GameLocation? loc = null) =>
            this.HarvestAll(loc);

        /// <summary>一键采集：树 + 大障碍 + 石头杂草树枝，全清。</summary>
        public (int Trees, int Clumps, int Debris) HarvestAll(GameLocation? loc = null)
        {
            int trees = this.CutAllTrees(loc);
            int clumps = this.ClearClumps(loc);
            int debris = this.ClearDebris(loc);
            return (trees, clumps, debris);
        }

        /// <summary>
        /// 诊断用：把当前地图上被判定为「杂物」的对象按名字分组统计，
        /// 用来检查是不是把不该清的东西（庄稼之类）也算进来了。
        /// </summary>
        public List<string> DescribeDebris(GameLocation? loc = null)
        {
            GameLocation? where = loc ?? Game1.currentLocation;
            var report = new List<string>();
            if (where?.objects == null)
                return report;

            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            int total = 0;
            try
            {
                foreach (KeyValuePair<Vector2, SObject> pair in where.objects.Pairs)
                {
                    SObject obj = pair.Value;
                    if (obj == null || !IsDebris(obj))
                        continue;
                    total++;
                    string key = $"{obj.Name}/{obj.QualifiedItemId} (type={obj.Type})";
                    byName[key] = byName.TryGetValue(key, out int c) ? c + 1 : 1;
                }
            }
            catch (Exception ex)
            {
                report.Add("  统计失败：" + ex.Message);
                return report;
            }

            report.Add($"  判定为杂物的共 {total} 个 @ {where.Name}");
            foreach (KeyValuePair<string, int> kv in byName.OrderByDescending(k => k.Value).Take(25))
                report.Add($"     {kv.Value,6}  {kv.Key}");
            return report;
        }

        /// <summary>
        /// 列出游戏里所有已注册的工具 ID（含钓竿的各种等级），
        /// 用来确认 (T) 后面到底该写什么，不靠猜。
        /// </summary>
        public static List<string> ListRegisteredTools(GameLocation? probe = null)
        {
            var list = new List<string>();

            // 直接问游戏：遍历 Data/Tools 这个数据表
            try
            {
                System.Collections.IEnumerable? table =
                    Game1.content.Load<Dictionary<string, StardewValley.GameData.Tools.ToolData>>("Data\\Tools")
                    is Dictionary<string, StardewValley.GameData.Tools.ToolData> tools
                        ? tools
                        : null;

                if (table != null)
                {
                    foreach (object? entry in table)
                    {
                        if (entry is KeyValuePair<string, StardewValley.GameData.Tools.ToolData> kv)
                            list.Add($"(T){kv.Key}  ->  ClassName={kv.Value?.ClassName}");
                    }
                }
            }
            catch (Exception ex)
            {
                list.Add("读取 Data/Tools 失败：" + ex.Message);
            }

            if (list.Count == 0)
                list.Add("(Data/Tools 里没读到内容)");

            list.Sort(StringComparer.Ordinal);
            return list;
        }

        /// <summary>
        /// 逐个试候选工具 ID，报告游戏是否真的认识它（用来敲定工具表，不靠猜）。
        /// </summary>
        public List<string> ProbeToolIds(IEnumerable<string> candidates)
        {
            var report = new List<string>();
            foreach (string id in candidates)
            {
                try
                {
                    bool exists = ItemRegistry.Exists(id);
                    string detail = exists ? "" : "  <-- 游戏不认识";
                    if (exists)
                    {
                        Item probe = ItemRegistry.Create(id, 1);
                        detail = probe is Tool t
                            ? $"  名字=「{probe.DisplayName}」/{probe.Name} 升级={t.UpgradeLevel}"
                            : $"  名字=「{probe.DisplayName}」 但不是 Tool（{probe.GetType().Name}）";
                    }
                    report.Add($"  {(exists ? "[有]" : "[无]")} {id,-26}{detail}");
                }
                catch (Exception ex)
                {
                    report.Add($"  [错] {id,-26} {ex.Message}");
                }
            }
            return report;
        }

        // ==================================================================
        // 判断哪些算"可采集"
        // ==================================================================

        private List<Vector2> CollectDebrisTiles(GameLocation where)
        {
            var tiles = new List<Vector2>();
            try
            {
                foreach (KeyValuePair<Vector2, SObject> pair in where.objects.Pairs)
                {
                    if (IsDebris(pair.Value))
                        tiles.Add(pair.Key);
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("扫描杂物失败：" + ex.Message, LogLevel.Warn);
            }
            return tiles;
        }

        /// <summary>石头、矿石、杂草、树枝、还有地上的采集物。</summary>
        internal static bool IsDebris(SObject? obj)
        {
            if (obj == null)
                return false;
            try
            {
                if (IsStoneLike(obj))
                    return true;
                if (obj.IsWeeds())
                    return true;
                if (obj.QualifiedItemId == "(O)294")                                   // 树枝
                    return true;
                if (obj.isForage())
                    return true;                                                       // 地上的野果/贝壳等
                // 游戏自己的判断，兜住漏网的石头类
                if (obj.isDebrisOrForage())
                    return true;
                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>石头 / 矿石 / 晶球（用镐子敲的）。</summary>
        internal static bool IsStoneLike(SObject? obj)
        {
            if (obj == null)
                return false;
            try
            {
                // 游戏自己的判断：石头类物品都是 type "Stone"
                if (obj.Type == "Stone")
                    return true;

                // 大石头的纹理图是 1，或者带这些名字
                string name = obj.Name ?? "";
                if (name.Equals("Stone", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (name.EndsWith("Node", StringComparison.OrdinalIgnoreCase))
                    return true;
                if (name.Equals("Geode", StringComparison.OrdinalIgnoreCase)
                    || name.Contains("Geode", StringComparison.OrdinalIgnoreCase))
                    return true;
                return false;
            }
            catch
            {
                return false;
            }
        }
    }
}
