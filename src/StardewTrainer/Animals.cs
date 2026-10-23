using System;
using System.Collections.Generic;
using System.Linq;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Characters;

namespace StardewTrainer
{
    /// <summary>宠物好感度、农场动物快乐度 / 饱足度。</summary>
    public class Animals
    {
        private readonly IMonitor Monitor;

        public Animals(IMonitor monitor)
        {
            this.Monitor = monitor;
        }

        /// <summary>宠物好感度上限 1000（取自 Pet 自己的最大值）。</summary>
        private static int PetMaxFriendship => Math.Max(1, Pet.maxFriendship);

        /// <summary>农场动物快乐度上限 255（游戏里这个 NetInt 就是 0-255）。</summary>
        private const int AnimalMaxHappiness = 255;

        /// <summary>农场动物饱足度上限 255。</summary>
        private const int AnimalMaxFullness = 255;

        // ==================================================================
        // 宠物
        // ==================================================================
        /// <summary>世界上所有的宠物（含猫狗，也含 1.6 新增的宠物）。</summary>
        public List<Pet> AllPets()
        {
            var pets = new List<Pet>();
            try
            {
                foreach (GameLocation loc in Game1.locations)
                {
                    if (loc?.characters == null)
                        continue;
                    foreach (NPC npc in loc.characters)
                    {
                        if (npc is Pet pet && !pets.Contains(pet))
                            pets.Add(pet);
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("扫描宠物失败：" + ex.Message, LogLevel.Warn);
            }
            return pets;
        }

        /// <summary>把所有宠物的好感度拉满。返回处理了几只。</summary>
        public int MaxPetFriendship()
        {
            int changed = 0;
            foreach (Pet pet in this.AllPets())
            {
                try
                {
                    if (pet.friendshipTowardFarmer.Value < PetMaxFriendship)
                    {
                        pet.friendshipTowardFarmer.Value = PetMaxFriendship;
                        changed++;
                    }
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce("设置宠物好感度失败：" + ex.Message, LogLevel.Warn);
                }
            }
            return changed;
        }

        // ==================================================================
        // 农场动物
        // ==================================================================
        /// <summary>所有农场动物（游戏把动物挂在各个 GameLocation 的 animals 字典上）。</summary>
        public List<FarmAnimal> AllFarmAnimals()
        {
            var animals = new List<FarmAnimal>();
            try
            {
                foreach (GameLocation loc in Game1.locations)
                {
                    if (loc?.animals == null)
                        continue;
                    foreach (FarmAnimal animal in loc.animals.Values)
                    {
                        if (animal != null && !animals.Contains(animal))
                            animals.Add(animal);
                    }
                }
            }
            catch (Exception ex)
            {
                this.Monitor.LogOnce("扫描农场动物失败：" + ex.Message, LogLevel.Warn);
            }
            return animals;
        }

        /// <summary>把所有农场动物的快乐度拉满。</summary>
        public int MaxAnimalHappiness()
        {
            int changed = 0;
            foreach (FarmAnimal a in this.AllFarmAnimals())
            {
                try
                {
                    if (a.happiness.Value < AnimalMaxHappiness)
                    {
                        a.happiness.Value = AnimalMaxHappiness;
                        changed++;
                    }
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce("设置动物快乐度失败：" + ex.Message, LogLevel.Warn);
                }
            }
            return changed;
        }

        /// <summary>把所有农场动物的饱足度拉满（也就是喂饱）。</summary>
        public int MaxAnimalFullness()
        {
            int changed = 0;
            foreach (FarmAnimal a in this.AllFarmAnimals())
            {
                try
                {
                    if (a.fullness.Value < AnimalMaxFullness)
                    {
                        a.fullness.Value = AnimalMaxFullness;
                        changed++;
                    }
                }
                catch (Exception ex)
                {
                    this.Monitor.LogOnce("设置动物饱足度失败：" + ex.Message, LogLevel.Warn);
                }
            }
            return changed;
        }
    }
}
