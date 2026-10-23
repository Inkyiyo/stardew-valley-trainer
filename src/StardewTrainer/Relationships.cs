using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;

namespace StardewTrainer
{
    /// <summary>把某个村民设成配偶（结婚/同居）。</summary>
    public class Relationships
    {
        private readonly IMonitor Monitor;

        public Relationships(IMonitor monitor)
        {
            this.Monitor = monitor;
        }

        /// <summary>好感度上限（也就是满心）。</summary>
        private const int MaxFriendshipPoints = 2500;

        /// <summary>
        /// 把 name 对应的 NPC 设成配偶，并自动解除上一任。
        ///
        /// 光写 Farmer.spouse 是不够的：游戏的 Farmer.isMarriedOrRoommates() 会同时检查
        /// friendshipData[spouse].IsMarried()，两个条件都满足才算结婚。
        /// 所以这里必须一起把关系状态写成 Married，否则会变成"有配偶但不算结婚"的坏档。
        /// </summary>
        public bool Marry(string name)
        {
            if (!ModEntry.WorldReady || Game1.player == null)
            {
                this.Monitor.Log("请先进入存档。", LogLevel.Warn);
                return false;
            }

            NPC? npc = this.FindAnyNpc(name);
            if (npc == null)
            {
                this.Monitor.Log($"找不到 NPC「{name}」。用 trainer_npcs 看可用的名字。", LogLevel.Warn);
                return false;
            }

            try
            {
                Farmer player = Game1.player;
                string who = npc.Name;

                if (!npc.datable.Value)
                    this.Monitor.Log($"{who} 本来不是可结婚对象，还是照做了（可能会有点怪）。", LogLevel.Warn);

                this.ClearPreviousSpouse(who);

                // 建立（或取出）好感度记录，拉满并标为已婚
                if (!player.friendshipData.TryGetValue(who, out Friendship? friendship) || friendship == null)
                {
                    friendship = new Friendship();
                    player.friendshipData[who] = friendship;
                }
                friendship.Points = MaxFriendshipPoints;
                friendship.Status = FriendshipStatus.Married;
                friendship.ProposalRejected = false;
                friendship.RoommateMarriage = false;

                player.spouse = who;

                // 4) 房子至少要升级过一次
                // FarmHouse.updateMap() 在「有配偶」时会去找 Maps\FarmHouse{upgradeLevel}_marriage，
                // 而升级等级 0 对应的 FarmHouse_marriage.xnb 根本不存在，
                // 结果每次进农舍都会抛 ContentLoadException。升级到 1 级即可对上 FarmHouse1_marriage。
                this.EnsureMarriageHouse(player);

                this.Monitor.Log(
                    $"已把 {who} 设成你的配偶（好感度拉满，关系状态 = 已婚）。" +
                    $"getSpouse() 现在返回：{player.getSpouse()?.Name ?? "null"}", LogLevel.Info);
                Sound.Play(Sound.LevelUp);
                return true;
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"设置配偶失败：{ex.Message}", LogLevel.Warn);
                return false;
            }
        }

        /// <summary>
        /// 解除上一任配偶。把关系状态改成 Divorced 而不是删掉记录，
        /// 这样游戏自己的对话/日程逻辑不会因为"配偶名字还在但记录没了"而出错。
        /// </summary>
        private void ClearPreviousSpouse(string newSpouse)
        {
            Farmer player = Game1.player;
            string? previous = player.spouse;

            if (string.IsNullOrEmpty(previous) || previous == newSpouse)
                return;

            try
            {
                if (player.friendshipData.TryGetValue(previous, out Friendship? old) && old != null)
                    old.Status = FriendshipStatus.Divorced;
                this.Monitor.Log($"已解除与 {previous} 的婚姻关系（状态改为离异）。", LogLevel.Info);
            }
            catch (Exception ex)
            {
                this.Monitor.Log($"解除上一任配偶时出错：{ex.Message}", LogLevel.Warn);
            }
        }

        /// <summary>按名字找 NPC：先问游戏，再退回自己扫地图（含非村民角色）。</summary>
        private NPC? FindAnyNpc(string name)
        {
            NPC? npc = Game1.getCharacterFromName(name, false, true);
            if (npc != null)
                return npc;

            foreach (NPC candidate in this.AllNpcs())
            {
                if (candidate.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return candidate;
            }
            return null;
        }

        /// <summary>
        /// 列出世界里所有"真人" NPC 的英文名（村民、可结婚对象、矮人、科罗布斯……）。
        /// 排除怪物、宠物、农场动物和马，这些设成配偶没有意义。
        /// </summary>
        public List<string> AllNpcNames()
        {
            var names = new List<string>();
            try
            {
                foreach (NPC npc in this.AllNpcs())
                {
                    if (!npc.IsVillager)
                        continue;
                    if (!names.Contains(npc.Name))
                        names.Add(npc.Name);
                }
                names.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("列出 NPC 失败：" + ex.Message, LogLevel.Warn);
            }
            return names;
        }

        private IEnumerable<NPC> AllNpcs()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (GameLocation loc in Game1.locations)
            {
                if (loc?.characters == null)
                    continue;
                foreach (NPC npc in loc.characters)
                {
                    if (npc != null && seen.Add(npc.Name))
                        yield return npc;
                }
            }
        }

        /// <summary>好感度记录里的状态，用来给菜单显示。</summary>
        public string DescribeRelationship(string name)
        {
            try
            {
                Farmer? p = Game1.player;
                if (p == null)
                    return "-";
                if (p.friendshipData.TryGetValue(name, out Friendship? f) && f != null)
                    return $"{f.Status}（{f.Points} 点）";
                return "还没有交情";
            }
            catch
            {
                return "-";
            }
        }

        /// <summary>按序号取 NPC 名（自动绕回），给菜单左右选择用。</summary>
        public string NpcNameAt(int index)
        {
            List<string> names = this.AllNpcNames();
            if (names.Count == 0)
                return "";
            int i = index % names.Count;
            if (i < 0)
                i += names.Count;
            return names[i];
        }

        /// <summary>世界里的 NPC 总数。</summary>
        public int NpcCount()
        {
            return this.AllNpcNames().Count;
        }

        /// <summary>把指定序号的 NPC 设成配偶。</summary>
        public bool MarryByIndex(int index)
        {
            string name = this.NpcNameAt(index);
            if (string.IsNullOrEmpty(name))
            {
                this.Monitor.Log("现在找不到任何 NPC。", LogLevel.Warn);
                return false;
            }
            return this.Marry(name);
        }

        /// <summary>
        /// 保证房子至少升级过一次，否则结婚后游戏会去找不存在的 Maps\FarmHouse_marriage。
        /// 升级完顺手刷新一下农舍地图，配偶的小屋才会立刻出现。
        /// </summary>
        private void EnsureMarriageHouse(Farmer player)
        {
            try
            {
                if (player.HouseUpgradeLevel >= 1)
                    return;

                player.HouseUpgradeLevel = 1;
                this.Monitor.Log("把房子升级到 1 级（结婚需要，否则游戏会找不到婚姻版农舍地图）。", LogLevel.Info);

                // 立刻刷新农舍地图，免得要等下次进门才生效
                if (Game1.getLocationFromName("FarmHouse") is StardewValley.Locations.FarmHouse house)
                    house.updateMap();
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("升级房子失败（不影响配偶设置）：" + ex.Message, LogLevel.Warn);
            }
        }

        /// <summary>当前配偶，没有就返回 null。</summary>
        public string? CurrentSpouse()
        {
            try
            {
                Farmer? p = Game1.player;
                if (p == null || string.IsNullOrEmpty(p.spouse))
                    return null;
                return p.spouse;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>列出所有可以结婚的村民英文名（好感度 8 心以上才能结婚的那批）。</summary>
        public List<string> ListDatable()
        {
            var result = new List<string>();
            try
            {
                foreach (GameLocation loc in Game1.locations)
                {
                    foreach (NPC npc in loc.characters)
                    {
                        if (npc.datable.Value && !result.Contains(npc.Name))
                            result.Add(npc.Name);
                    }
                }
                result.Sort(StringComparer.OrdinalIgnoreCase);
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("列可结婚对象失败：" + ex.Message, LogLevel.Warn);
            }
            return result;
        }
    }
}
