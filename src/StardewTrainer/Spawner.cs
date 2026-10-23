using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Monsters;
using StardewValley.Objects;

namespace StardewTrainer
{
    /// <summary>生成物品 / NPC。全部带异常保护，失败只记日志不影响游戏。</summary>
    public class Spawner
    {

        /// <summary>世界是否已加载（兼容 SaveGame.Load 路径）。</summary>
        private static bool WorldReady => ModEntry.WorldReady;
        private readonly IMonitor Monitor;

        /// <summary>记录由修改器生成的角色，方便一键清理。</summary>
        private static readonly HashSet<string> SpawnedVillagers = new(StringComparer.OrdinalIgnoreCase);

        public Spawner(IMonitor monitor)
        {
            this.Monitor = monitor;
        }

        /// <summary>删除所有由修改器生成的角色（当前地图上的）。</summary>
        public int ClearSpawnedVillagers()
        {
            if (!ModEntry.WorldReady || Game1.currentLocation == null)
                return 0;

            int removed = 0;
            foreach (NPC npc in Game1.currentLocation.characters.ToList())
            {
                // 只删自己生成的，并且不要误删宠物
                if (npc is not Monster && npc is not StardewValley.Characters.Pet
                    && SpawnedVillagers.Contains(npc.Name))
                {
                    Game1.currentLocation.characters.Remove(npc);
                    removed++;
                }
            }
            if (removed > 0)
                this.Monitor.Log($"已移除修改器生成的 {removed} 个角色。", LogLevel.Info);
            else
                this.Monitor.Log("这张地图上没有修改器生成的角色。", LogLevel.Info);
            return removed;
        }

        // ==================================================================
        // 物品
        // ==================================================================

        /// <summary>
        /// 生成物品（ID 为主，最稳）。第二项是游戏的英文内部名，用来兜底查找——
        /// ObjectData.Name 是英文键，中文只在 DisplayName 里，所以中文名搜索不一定命中。
        ///
        /// 这张表里的每个 ID 都用游戏数据反查核对过（trainer_itemcheck 可以重新核对），
        /// 不要凭记忆改 ID。
        /// </summary>
        public static readonly (string Id, string Name, string Label)[] CommonItems =
        {
            // ---- 宝石 ----
            ("(O)72",  "Diamond",        "钻石"),
            ("(O)74",  "Prismatic Shard","五彩碎片"),
            ("(O)60",  "Emerald",        "绿宝石"),
            ("(O)64",  "Ruby",           "红宝石"),
            ("(O)66",  "Amethyst",       "紫水晶"),
            ("(O)68",  "Topaz",          "黄水晶"),
            ("(O)70",  "Jade",           "翡翠"),
            ("(O)62",  "Aquamarine",     "海蓝宝石"),
            ("(O)338", "Refined Quartz", "精炼石英"),
            // ---- 金属 ----
            ("(O)386", "Iridium Ore",    "铱矿石"),
            ("(O)337", "Iridium Bar",    "铱锭"),
            ("(O)334", "Copper Bar",     "铜锭"),
            ("(O)335", "Iron Bar",       "铁锭"),
            ("(O)336", "Gold Bar",       "金锭"),
            // ---- 材料 ----
            ("(O)787", "Battery Pack",   "电池组"),
            ("(O)428", "Cloth",          "布料"),
            ("(O)709", "Hardwood",       "硬木"),
            ("(O)388", "Wood",           "木材"),
            ("(O)390", "Stone",          "石头"),
            // ---- 饮品 ----
            ("(O)348", "Wine",           "果酒"),
            ("(O)346", "Beer",           "啤酒"),
            ("(O)303", "Pale Ale",       "淡啤酒"),
            // ---- 稀有物 ----
            ("(O)107", "Dinosaur Egg",   "恐龙蛋"),
            ("(O)434", "Stardrop",       "星之果实"),
            ("(O)749", "Omni Geode",     "万象晶球"),
            ("(O)872", "Fairy Dust",     "仙尘"),
            ("(O)858", "Qi Gem",         "齐钻"),
            ("(O)73",  "Golden Walnut",  "金色核桃"),
            ("(O)928", "Golden Egg",     "金蛋"),
            // ---- 种子 ----
            ("(O)499", "Ancient Seeds",  "上古种子"),
            ("(O)479", "Melon Seeds",    "甜瓜种子"),
            ("(O)490", "Pumpkin Seeds",  "南瓜种子"),
            ("(O)486", "Starfruit Seeds","杨桃种子"),
            ("(O)431", "Sunflower Seeds","向日葵种子"),
            ("(O)472", "Parsnip Seeds",  "防风草种子"),
            ("(O)425", "Fairy Seeds",    "玫瑰仙子种子"),
        };

        /// <summary>
        /// 所有可获取的工具 ID（限定类型 T）。
        /// 每个 ID 都用 trainer_toolprobe 逐个问过游戏，确认存在且名字正确——
        /// 例如 (T)Scythe / (T)GoldenScythe 在 1.6 里就不是可获取工具，别加。
        /// </summary>
        public static readonly (string Id, string Label)[] AllTools =
        {
            // 基础工具
            ("(T)Axe",                "斧头"),
            ("(T)Pickaxe",            "十字镐"),
            ("(T)Hoe",                "锄头"),
            ("(T)WateringCan",        "喷壶"),
            ("(T)MilkPail",           "挤奶桶"),
            ("(T)Shears",             "剪刀"),
            // 鱼竿（从入门到顶配）
            ("(T)TrainingRod",        "训练用鱼竿"),
            ("(T)BambooPole",         "竹鱼竿"),
            ("(T)FiberglassRod",      "玻璃纤维棒"),
            ("(T)IridiumRod",         "铱金鱼竿"),
            ("(T)AdvancedIridiumRod", "高级铱金鱼竿"),
            // 淘金盘
            ("(T)Pan",                "铜淘盘"),
            ("(T)SteelPan",           "钢淘盘"),
            ("(T)GoldPan",            "金淘盘"),
            ("(T)IridiumPan",         "铱淘盘"),
            // 实用工具
            ("(T)Lantern",            "灯笼"),
            ("(T)ReturnScepter",      "回程魔杖"),
        };

        /// <summary>玩家背包里有没有某件工具（按工具类型名判断，不靠本地化名字）。</summary>
        private static bool HasTool(Farmer player, string toolItemId)
        {
            string shortName = toolItemId.StartsWith("(T)", StringComparison.Ordinal)
                ? toolItemId.Substring(3)
                : toolItemId;

            foreach (Item? item in player.Items)
            {
                if (item is Tool tool
                    && tool.GetType().Name.Equals(shortName, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            return false;
        }

        /// <summary>
        /// 把整套工具给玩家。已经有的工具会被跳过（不会重复占背包）。
        /// 返回实际新给了几件。
        /// </summary>
        public int GiveAllTools()
        {
            if (Game1.player == null)
                return 0;

            int given = 0;
            foreach ((string id, string label) in AllTools)
            {
                try
                {
                    if (!ItemRegistry.Exists(id))
                        continue;

                    // 已经拿过这件工具就不重复给
                    if (HasTool(Game1.player, id))
                        continue;

                    Item tool = ItemRegistry.Create(id, 1);
                    if (Game1.player.addItemToInventoryBool(tool))
                        given++;
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce($"获取工具 {id} 失败：{ex.Message}", LogLevel.Warn);
                }
            }
            return given;
        }

        /// <summary>
        /// 核对工具表：用游戏数据反查每个 ID 的真实名字和升级等级，
        /// 顺便报告玩家背包里已经有哪几件。
        /// </summary>
        public List<string> AuditTools()
        {
            var report = new List<string>();
            int bad = 0;

            foreach ((string id, string label) in AllTools)
            {
                if (!ItemRegistry.Exists(id))
                {
                    report.Add($"  [X] {id,-18} 这个 ID 不存在！菜单写的是「{label}」");
                    bad++;
                    continue;
                }

                try
                {
                    Item probe = ItemRegistry.Create(id, 1);
                    string upgrade = probe is Tool t ? $"升级等级={t.UpgradeLevel}" : "（不是 Tool？）";
                    bool owned = Game1.player != null && HasTool(Game1.player, id);
                    bool nameOk = probe.DisplayName != null
                                 && probe.DisplayName.Equals(label, StringComparison.OrdinalIgnoreCase);
                    if (!nameOk)
                        bad++;
                    report.Add($"  {(nameOk ? "[OK]" : "[标签?]")} {id,-18} 菜单=「{label}」 " +
                               $"游戏=「{probe.DisplayName}」/{probe.Name} {upgrade} 已有={(owned ? "是" : "否")}");
                }
                catch (Exception ex)
                {
                    report.Add($"  [X] {id,-18} 创建失败：{ex.Message}");
                    bad++;
                }
            }

            report.Add(bad == 0
                ? "  ✓ 工具表全部核对通过。"
                : $"  ✗ 有 {bad} 项对不上（见上面 [X] / [标签?]）。");
            return report;
        }

        /// <summary>
        /// 核对常用物品表：用游戏自己的数据反查每个 ID 的真实名字，
        /// 同时校验「英文内部名」和「中文显示名」，找出对不上的（防止给错物品）。
        /// </summary>
        public List<string> AuditCommonItems()
        {
            var report = new List<string>();
            int bad = 0;

            foreach ((string id, string english, string label) in CommonItems)
            {
                if (id == "(O)0")
                {
                    report.Add($"  跳过占位项「{label}」");
                    continue;
                }

                string gameName;
                string gameDisplay;
                try
                {
                    if (!ItemRegistry.Exists(id))
                    {
                        report.Add($"  [X] {id,-9} 这个 ID 不存在！菜单写的是「{label}」");
                        bad++;
                        continue;
                    }
                    Item probe = ItemRegistry.Create(id, 1);
                    gameName = probe.Name;
                    gameDisplay = probe.DisplayName;
                }
                catch (Exception ex)
                {
                    report.Add($"  [X] {id,-9} 创建失败：{ex.Message}");
                    bad++;
                    continue;
                }

                bool idOk = gameName.Contains(english, StringComparison.OrdinalIgnoreCase);
                bool labelOk = gameDisplay.Equals(label, StringComparison.OrdinalIgnoreCase);
                bool ok = idOk && labelOk;
                if (!ok)
                    bad++;

                string flag = ok ? "[OK]" : (idOk ? "[标签?]" : "[ID!]");
                report.Add($"  {flag} {id,-9} 菜单=「{label}」 游戏=「{gameDisplay}」/{gameName}");
            }

            report.Add(bad == 0
                ? "  ✓ 全部核对通过，菜单显示与实际给到的物品一致。"
                : $"  ✗ 有 {bad} 项对不上，需要修正（见上面 [ID!] / [标签?]）。");
            return report;
        }

        /// <summary>
        /// 按名字搜索物品。同时匹配英文内部名（ObjectData.Name）和本地化显示名（DisplayName），
        /// 所以中文和英文都能搜。
        /// </summary>
        public List<(string Id, string Name)> SearchItems(string keyword, int limit = 20)
        {
            var result = new List<(string, string)>();
            try
            {
                IDictionary<string, StardewValley.GameData.Objects.ObjectData>? data = Game1.objectData;
                if (data == null)
                {
                    this.Monitor.Log("拿不到物品数据表（Game1.objectData）。", LogLevel.Warn);
                    return result;
                }

                foreach (KeyValuePair<string, StardewValley.GameData.Objects.ObjectData> kv in data)
                {
                    StardewValley.GameData.Objects.ObjectData? d = kv.Value;
                    if (d == null)
                        continue;

                    string english = d.Name ?? "";
                    string localized = d.DisplayName ?? "";
                    bool hit = kv.Key.Equals(keyword, StringComparison.OrdinalIgnoreCase)
                               || english.Contains(keyword, StringComparison.OrdinalIgnoreCase)
                               || localized.Contains(keyword, StringComparison.OrdinalIgnoreCase);
                    if (!hit)
                        continue;

                    string id = ItemRegistry.QualifyItemId(kv.Key);
                    if (string.IsNullOrEmpty(id))
                        continue;
                    result.Add((id, localized.Length > 0 ? localized : english));
                    if (result.Count >= limit)
                        break;
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("搜索物品失败：" + ex.Message, LogLevel.Warn);
            }
            return result;
        }

        /// <summary>创建一个物品实例；找不到就返回 null（不会给出"错误物品"）。</summary>
        public Item? CreateItem(string idOrName, int count = 1)        {
            try
            {
                string query = idOrName.Trim();
                string? id = null;

                if (query.StartsWith("(", StringComparison.Ordinal))
                {
                    id = query;                                   // 限定 ID，例如 (O)72
                }
                else if (int.TryParse(query, out int numeric))
                {
                    id = "(O)" + numeric;                         // 纯数字当物品 ID
                }
                else
                {
                    List<(string Id, string Name)> hits = this.SearchItems(query, 1);
                    if (hits.Count == 0)
                    {
                        this.Monitor.Log($"找不到名字包含「{query}」的物品。用 trainer_finditem <关键词> 查准确名字，" +
                                         "或者直接用数字 ID。", LogLevel.Warn);
                        return null;
                    }
                    id = hits[0].Id;
                }

                if (!ItemRegistry.Exists(id))
                {
                    this.Monitor.Log($"物品 ID「{id}」不存在。", LogLevel.Warn);
                    return null;
                }

                Item item = ItemRegistry.Create(id, count);
                if (item == null)
                {
                    this.Monitor.Log($"创建物品「{id}」返回空。", LogLevel.Warn);
                    return null;
                }
                return item;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"创建物品 '{idOrName}' 失败：{ex.Message}", LogLevel.Warn);
                return null;
            }
        }

        /// <summary>给玩家物品；背包满了就掉在脚下。返回实际给出的数量。</summary>
        public int GiveItem(string idOrName, int count = 1)
        {
            if (!ModEntry.WorldReady || Game1.player == null)
            {
                this.Monitor.Log("请先进入存档。", LogLevel.Warn);
                return 0;
            }

            Item? item = this.CreateItem(idOrName, count);
            if (item == null)
                return 0;

            int remaining = count;
            try
            {
                // 一次给一个，保证部分成功时不会凭空消失
                for (int i = 0; i < count; i++)
                {
                    Item single = ItemRegistry.Create(item.QualifiedItemId, 1);
                    if (Game1.player.addItemToInventoryBool(single))
                        remaining--;
                    else
                        break;
                }

                int dropped = remaining;
                if (dropped > 0)
                {
                    // 背包满：把剩下的掉在脚下（Debris(Item, Vector2)）
                    Item stack = ItemRegistry.Create(item.QualifiedItemId, dropped);
                    Vector2 pos = Game1.player.Tile * 64f + new Vector2(32f, 32f);
                    Game1.currentLocation.debris.Add(new Debris(stack, pos));
                }

                int given = count - remaining;
                this.Monitor.Log(
                    $"已获得 {item.DisplayName} ×{given}" + (dropped > 0 ? $"（背包已满，{dropped} 个掉在脚下）" : ""),
                    LogLevel.Info);
                Sound.Play(Sound.Purchase);
                return given;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"给予物品失败：{ex.Message}", LogLevel.Warn);
                return 0;
            }
        }

        // ==================================================================
        // NPC / 怪物
        // ==================================================================

        /// <summary>菜单里可选的生成目标：显示名 -> 类型名。</summary>
        public static readonly (string Label, string TypeName)[] SpawnableMonsters =
        {
            ("绿色史莱姆", "GreenSlime"),
            ("蓝色史莱姆", "BlueSquid"),
            ("蝙蝠", "Bat"),
            ("冰冻蝙蝠", "FrostBat"),
            ("骷髅", "Skeleton"),
            ("幽灵", "Ghost"),
            ("石魔", "StoneGolem"),
            ("尘土精灵", "DustSpirit"),
            ("史莱姆王", "BigSlime"),
            ("影子狂徒", "ShadowBrute"),
            ("影子萨满", "ShadowShaman"),
            ("金属头", "MetalHead"),
            ("虫子", "Bug"),
            ("苍蝇", "Fly"),
            ("螃蟹", "RockCrab"),
            ("木乃伊", "Mummy"),
            ("霸王龙", "DinoMonster"),
            ("岩浆精灵", "LavaSprite"),
            ("蛇", "Serpent"),
            ("岩浆蝙蝠", "MagmaSprite"),
        };

        public static readonly (string Label, string NpcName)[] SpawnableVillagers =
        {
            ("阿比盖尔", "Abigail"),
            ("亚历克斯", "Alex"),
            ("艾米丽", "Emily"),
            ("海莉", "Haley"),
            ("莉亚", "Leah"),
            ("潘妮", "Penny"),
            ("玛鲁", "Maru"),
            ("塞巴斯蒂安", "Sebastian"),
            ("山姆", "Sam"),
            ("谢恩", "Shane"),
            ("罗宾", "Robin"),
            ("皮埃尔", "Pierre"),
            ("卡洛琳", "Caroline"),
            ("克林特", "Clint"),
            ("威利", "Willy"),
            ("莱纳斯", "Linus"),
            ("玛妮", "Marnie"),
            ("艾芙琳", "Evelyn"),
            ("乔治", "George"),
            ("刘易斯", "Lewis"),
            ("乔迪", "Jodi"),
            ("肯特", "Kent"),
            ("帕姆", "Pam"),
            ("冈瑟", "Gunther"),
            ("桑迪", "Sandy"),
            ("矮人", "Dwarf"),
            ("科罗布斯", "Krobus"),
            ("法师", "Wizard"),
            ("莱纳斯", "Linus"),
        };

        private static readonly Dictionary<string, Type> MonsterTypes = new(StringComparer.OrdinalIgnoreCase);

        private static Type? FindMonsterType(string typeName)
        {
            if (MonsterTypes.Count == 0)
            {
                foreach (Type t in typeof(Monster).Assembly.GetTypes())
                {
                    if (t.IsClass && !t.IsAbstract && typeof(Monster).IsAssignableFrom(t) && t.Name != "Monster")
                        MonsterTypes[t.Name] = t;
                }
            }
            return MonsterTypes.TryGetValue(typeName, out Type? found) ? found : null;
        }

        /// <summary>列出游戏里所有可生成的怪物类型名。</summary>
        public List<string> ListMonsterTypes()
        {
            FindMonsterType("__init__");
            return MonsterTypes.Keys.OrderBy(x => x).ToList();
        }

        /// <summary>在玩家附近生成一只怪物。</summary>
        public bool SpawnMonster(string typeName, int offsetTiles = 2)
        {
            if (!ModEntry.WorldReady || Game1.player == null)
            {
                this.Monitor.Log("请先进入存档。", LogLevel.Warn);
                return false;
            }

            Type? type = FindMonsterType(typeName);
            if (type == null)
            {
                this.Monitor.Log($"找不到怪物类型 '{typeName}'。可用类型见 trainer_monsters。", LogLevel.Warn);
                return false;
            }

            try
            {
                Vector2 tile = Game1.player.Tile + new Vector2(offsetTiles, 0);
                Vector2 pixel = tile * 64f;

                // 不同怪物的构造函数不一样：Skeleton 只有 (Vector2, bool)，MetalHead 有 (Vector2, MineShaft)…
                // 按优先级逐个尝试，全部失败就报出可用签名，方便定位。
                object? instance = null;
                foreach (object?[] args in new[]
                         {
                             new object?[] { pixel },
                             new object?[] { pixel, 0 },
                             new object?[] { pixel, false },
                             new object?[] { pixel, Game1.currentLocation.Name },
                             new object?[] { Game1.currentLocation.Name, pixel },
                         })
                {
                    Type[] types = args.Select(a => a!.GetType()).ToArray();
                    // int 也能匹配 bool 之外的数值参数，这里显式只找精确匹配
                    ConstructorInfo? ctor = type.GetConstructor(types);
                    if (ctor == null)
                        continue;
                    instance = ctor.Invoke(args);
                    if (instance != null)
                        break;
                }

                if (instance is not Monster monster)
                {
                    string sigs = string.Join(", ", type.GetConstructors()
                        .Select(c => "(" + string.Join(",", c.GetParameters()
                            .Select(p => p.ParameterType.Name)) + ")"));
                    this.Monitor.Log($"生成 {typeName} 失败：找不到合适的构造函数。可用签名：{sigs}", LogLevel.Warn);
                    return false;
                }

                Game1.currentLocation.characters.Add(monster);
                this.Monitor.Log($"已生成怪物：{typeName} @ {tile.X:0},{tile.Y:0}", LogLevel.Info);
                Sound.Play(Sound.Wand);
                return true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"生成怪物 {typeName} 失败：{ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>
        /// 生成一个村民；如果这个 NPC 已经存在，就把他传送到玩家身边
        /// （而不是像以前那样直接跳过）。
        /// </summary>
        public bool SpawnVillager(string npcName)
        {
            if (!ModEntry.WorldReady || Game1.player == null)
            {
                this.Monitor.Log("请先进入存档。", LogLevel.Warn);
                return false;
            }

            try
            {
                Vector2 target = FindOpenTileNear(Game1.player.Tile);

                // 已经存在 -> 传送到玩家身边（而不是像以前那样直接跳过）
                NPC? existing = Game1.getCharacterFromName(npcName) ?? this.FindNpcAnywhere(npcName);
                if (existing != null)
                    return this.TeleportNpcToPlayer(existing, target);

                // 签名（来自游戏程序集）：NPC(AnimatedSprite, Vector2, int facingDir, string name, LocalizedContentManager)
                NPC npc = new(
                    new AnimatedSprite("Characters\\" + npcName, 0, 16, 32),
                    target,
                    2,
                    npcName,
                    Game1.content);

                Game1.currentLocation.characters.Add(npc);
                SpawnedVillagers.Add(npcName);
                this.Monitor.Log($"已生成角色：{npcName}（用 trainer_clearnpc 可以删掉）", LogLevel.Info);
                Sound.Play(Sound.Wand);
                return true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"生成角色 {npcName} 失败：{ex.GetType().Name}: {ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>在游戏已知的所有地图里按名字找一个 NPC。</summary>
        /// <summary>
        /// 在玩家周围找一个能站人的格子。原来直接用「玩家坐标 + 2」，
        /// 在室内（比如农舍左上角 9,9）会生成到墙里，NPC 就看不见了。
        /// </summary>
        private static Vector2 FindOpenTileNear(Vector2 origin)
        {
            GameLocation? loc = Game1.currentLocation;
            if (loc == null)
                return origin;

            (int dx, int dy)[] dirs =
            {
                (0, 1), (0, 2), (1, 0), (-1, 0), (1, 1), (-1, 1),
                (0, -1), (1, -1), (-1, -1), (2, 0), (-2, 0), (0, 3), (0, -2)
            };

            foreach ((int dx, int dy) in dirs)
            {
                Vector2 candidate = new(origin.X + dx, origin.Y + dy);
                if (IsFreeTile(loc, candidate))
                    return candidate;
            }
            return origin;
        }

        /// <summary>这个格子能不能站人。</summary>
        private static bool IsFreeTile(GameLocation loc, Vector2 tile)
        {
            try
            {
                if (!loc.isTileLocationOpen(new xTile.Dimensions.Location((int)tile.X, (int)tile.Y)))
                    return false;
                if (loc.isWaterTile((int)tile.X, (int)tile.Y))
                    return false;

                foreach (NPC npc in loc.characters)
                {
                    if (npc.Tile == tile)
                        return false;
                }
                return true;
            }
            catch
            {
                return false;
            }
        }

        private NPC? FindNpcAnywhere(string npcName)
        {
            try
            {
                foreach (GameLocation loc in Game1.locations)
                {
                    foreach (NPC npc in loc.characters)
                    {
                        if (npc.Name.Equals(npcName, StringComparison.OrdinalIgnoreCase))
                            return npc;
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("扫描 NPC 失败：" + ex.Message, LogLevel.Warn);
            }
            return null;
        }

        /// <summary>把指定 NPC 传送到玩家身边（跨地图也没问题）。</summary>
        public bool TeleportNpcToPlayer(NPC npc, Vector2? overrideTarget = null)
        {
            if (!ModEntry.WorldReady || Game1.player == null || Game1.currentLocation == null)
                return false;

            try
            {
                Vector2 target = overrideTarget ?? FindOpenTileNear(Game1.player.Tile);
                GameLocation here = Game1.currentLocation;
                string name = npc.Name;

                bool sameMap = npc.currentLocation != null
                               && npc.currentLocation.NameOrUniqueName == here.NameOrUniqueName;

                if (sameMap)
                {
                    npc.Position = target * 64f;
                    npc.Halt();
                }
                else
                {
                    // 跨地图交给游戏自己的 warp，它会正确搬迁字符列表
                    Game1.warpCharacter(npc, here.NameOrUniqueName, target);
                    SpawnedVillagers.Add(name);
                }

                this.Monitor.Log($"已把 {name} 传送到你身边（{target.X:0},{target.Y:0}）。", LogLevel.Info);
                Sound.Play(Sound.Wand);
                return true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"传送 {npc.Name} 失败：{ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>把当前地图上的某个 NPC 传送到玩家身边（按名字）。</summary>
        public bool TeleportNpcByName(string npcName)
        {
            NPC? npc = Game1.getCharacterFromName(npcName) ?? this.FindNpcAnywhere(npcName);
            if (npc == null)
            {
                this.Monitor.Log($"找不到角色「{npcName}」。", LogLevel.Warn);
                return false;
            }
            return this.TeleportNpcToPlayer(npc);
        }

        /// <summary>清空当前地图上的所有怪物。</summary>
        public int ClearMonsters()
        {
            if (!ModEntry.WorldReady || Game1.currentLocation == null)
                return 0;
            int removed = 0;
            foreach (NPC npc in Game1.currentLocation.characters.ToList())
            {
                if (npc is Monster)
                {
                    Game1.currentLocation.characters.Remove(npc);
                    removed++;
                }
            }
            if (removed > 0)
                this.Monitor.Log($"已移除 {removed} 只怪物。", LogLevel.Info);
            return removed;
        }
    }
}
