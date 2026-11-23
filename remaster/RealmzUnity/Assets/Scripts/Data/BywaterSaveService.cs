using System;
using System.Collections.Generic;
using System.IO;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Serialization;

namespace Realmz.Data
{
    /// <summary>
    /// JSON persistence service for City of Bywater exploration sessions using MiniJson.
    /// </summary>
    public static class BywaterSaveService
    {
        private const int SaveVersion = 1;

        public static bool Save(string path, ExplorationSession session)
        {
            if (string.IsNullOrWhiteSpace(path) || session == null)
            {
                return false;
            }

            try
            {
                Dictionary<string, object> root = new Dictionary<string, object>
                {
                    ["version"] = SaveVersion,
                    ["mapId"] = session.CurrentMap.MapId,
                    ["x"] = session.Position.X,
                    ["y"] = session.Position.Y,
                    ["facing"] = session.Facing ?? "South",
                    ["gold"] = session.Party.Gold,
                    ["hasBoat"] = session.Party.HasBoat,
                    ["torchTurnsRemaining"] = session.Party.TorchTurnsRemaining
                };

                List<object> stashList = new List<object>(session.Party.Stash.Count);
                for (int i = 0; i < session.Party.Stash.Count; i++)
                {
                    InventoryItem item = session.Party.Stash[i];
                    stashList.Add(new Dictionary<string, object>
                    {
                        ["itemId"] = item.ItemId,
                        ["quantity"] = item.Quantity,
                        ["isIdentified"] = item.IsIdentified,
                        ["remainingCharges"] = item.RemainingCharges
                    });
                }
                root["stash"] = stashList;

                List<object> heroesList = new List<object>(session.Party.Members.Count);
                for (int i = 0; i < session.Party.Members.Count; i++)
                {
                    PartyMemberState member = session.Party.Members[i];
                    Dictionary<string, object> equippedDict = new Dictionary<string, object>();
                    foreach (KeyValuePair<EquipmentSlot, InventoryItem> kvp in member.Equipped)
                    {
                        equippedDict[kvp.Key.ToString()] = kvp.Value.ItemId;
                    }

                    heroesList.Add(new Dictionary<string, object>
                    {
                        ["id"] = member.Id,
                        ["hp"] = member.Hp,
                        ["sp"] = member.Sp,
                        ["level"] = member.Level,
                        ["exhaustionStacks"] = member.ExhaustionStacks,
                        ["curseStacks"] = member.CurseStacks,
                        ["equipped"] = equippedDict
                    });
                }
                root["heroes"] = heroesList;

                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = MiniJsonWriter.Serialize(root, indented: true);
                File.WriteAllText(path, json);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static bool Load(
            string path,
            BywaterM1Bundle bundle,
            out ExplorationSession session,
            out string status)
        {
            session = null;
            status = string.Empty;

            if (bundle == null)
            {
                status = "Cannot load save: scenario bundle is null.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                status = "Save file not found.";
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                IReadOnlyDictionary<string, object> root = MiniJson.AsObject(MiniJson.Parse(json));

                string mapId = MiniJson.GetString(root, "mapId", "land_0");
                if (string.IsNullOrWhiteSpace(mapId))
                {
                    mapId = "land_0";
                }

                if (!bundle.Maps.TryGetValue(mapId, out TileMapData targetMap))
                {
                    status = "Saved map not found in bundle: " + mapId;
                    return false;
                }

                int x = MiniJson.GetInt(root, "x", targetMap.StartSpawn.X);
                int y = MiniJson.GetInt(root, "y", targetMap.StartSpawn.Y);
                GridCoord startPos = targetMap.InBounds(x, y) ? new GridCoord(x, y) : targetMap.StartSpawn;

                PartyState party = PartyState.FromBundle(bundle);
                party.Gold = Math.Max(0, MiniJson.GetInt(root, "gold", party.Gold));
                party.HasBoat = MiniJson.GetBool(root, "hasBoat", party.HasBoat);
                party.TorchTurnsRemaining = Math.Max(0, MiniJson.GetInt(root, "torchTurnsRemaining", party.TorchTurnsRemaining));

                // Clear preset stash and unequip default gear so saved state can be applied cleanly.
                ClearStash(party);
                EquipmentSlot[] allSlots = (EquipmentSlot[])Enum.GetValues(typeof(EquipmentSlot));
                for (int i = 0; i < party.Members.Count; i++)
                {
                    for (int s = 0; s < allSlots.Length; s++)
                    {
                        party.UnequipToStash(i, allSlots[s], bundle.Items);
                    }
                }
                ClearStash(party);

                if (MiniJson.TryGetValue(root, "heroes", out object rawHeroes) && rawHeroes != null)
                {
                    IReadOnlyList<object> savedHeroes = MiniJson.AsArray(rawHeroes);
                    for (int i = 0; i < savedHeroes.Count; i++)
                    {
                        IReadOnlyDictionary<string, object> heroObj = MiniJson.AsObject(savedHeroes[i]);
                        string heroId = MiniJson.GetString(heroObj, "id", string.Empty);
                        int memberIndex = FindMemberIndex(party.Members, heroId, i);
                        if (memberIndex < 0)
                        {
                            continue;
                        }

                        PartyMemberState member = party.Members[memberIndex];
                        member.Level = Math.Max(1, MiniJson.GetInt(heroObj, "level", member.Level));
                        member.Hp = Clamp(MiniJson.GetInt(heroObj, "hp", member.Hp), 0, member.MaxHp);
                        member.Sp = Clamp(MiniJson.GetInt(heroObj, "sp", member.Sp), 0, member.MaxSp);
                        member.ExhaustionStacks = Math.Max(0, MiniJson.GetInt(heroObj, "exhaustionStacks", 0));
                        member.CurseStacks = Math.Max(0, MiniJson.GetInt(heroObj, "curseStacks", 0));

                        if (MiniJson.TryGetValue(heroObj, "equipped", out object rawEquipped) && rawEquipped != null)
                        {
                            IReadOnlyDictionary<string, object> equippedObj = MiniJson.AsObject(rawEquipped);
                            foreach (KeyValuePair<string, object> kvp in equippedObj)
                            {
                                if (kvp.Value == null)
                                {
                                    continue;
                                }

                                int itemId = MiniJson.ToInt32(kvp.Value, "equipped." + kvp.Key);
                                if (!bundle.Items.TryGetValue(itemId, out ItemDefinition itemDef))
                                {
                                    continue;
                                }

                                party.AddItemToStash(itemId, 1, isIdentified: true, charges: itemDef.Charges);
                                int tempIdx = party.Stash.Count - 1;
                                if (!party.EquipFromStash(memberIndex, tempIdx, bundle.Items))
                                {
                                    ClearStash(party);
                                }
                            }
                            ClearStash(party);
                        }
                    }
                }

                if (MiniJson.TryGetValue(root, "stash", out object rawStash) && rawStash != null)
                {
                    IReadOnlyList<object> savedStash = MiniJson.AsArray(rawStash);
                    for (int i = 0; i < savedStash.Count; i++)
                    {
                        IReadOnlyDictionary<string, object> itemObj = MiniJson.AsObject(savedStash[i]);
                        int itemId = MiniJson.GetInt(itemObj, "itemId", -1);
                        int quantity = MiniJson.GetInt(itemObj, "quantity", 0);
                        if (itemId < 0 || quantity <= 0)
                        {
                            continue;
                        }

                        bool isIdentified = MiniJson.GetBool(itemObj, "isIdentified", true);
                        int remainingCharges = MiniJson.GetInt(itemObj, "remainingCharges", 0);
                        party.AddItemToStash(itemId, quantity, isIdentified, remainingCharges);
                    }
                }

                session = new ExplorationSession(bundle.Maps, targetMap.MapId, party, startPosition: startPos);
                status = string.Format("Loaded save at {0} ({1}, {2}).", targetMap.MapId, startPos.X, startPos.Y);
                return true;
            }
            catch (Exception ex)
            {
                session = null;
                status = "Failed to load save: " + ex.Message;
                return false;
            }
        }

        private static void ClearStash(PartyState party)
        {
            while (party.Stash.Count > 0)
            {
                int lastIndex = party.Stash.Count - 1;
                party.RemoveItemFromStash(lastIndex, party.Stash[lastIndex].Quantity);
            }
        }

        private static int FindMemberIndex(IReadOnlyList<PartyMemberState> members, string heroId, int fallbackIndex)
        {
            if (!string.IsNullOrWhiteSpace(heroId))
            {
                for (int i = 0; i < members.Count; i++)
                {
                    if (string.Equals(members[i].Id, heroId, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }

            if (fallbackIndex >= 0 && fallbackIndex < members.Count)
            {
                return fallbackIndex;
            }

            return -1;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min)
            {
                return min;
            }
            if (value > max)
            {
                return max;
            }
            return value;
        }
    }
}
