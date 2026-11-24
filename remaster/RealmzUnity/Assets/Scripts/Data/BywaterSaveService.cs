using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;
using RealmzCore.Serialization;
using UnityEngine;

namespace Realmz.Data
{
    [Serializable]
    public sealed class SavedConditionState
    {
        public int id;
        public int magnitude;
        public int turnsRemaining;
        public int baseRecoveryChancePercent;
        public bool canRecoverBySave;
        public int escalationStepPercent;
        public int elapsedRounds;
    }

    [Serializable]
    public sealed class SavedEquippedEntry
    {
        public string slot = string.Empty;
        public int itemId;
        public int remainingCharges;
    }

    [Serializable]
    public sealed class SavedMemberState
    {
        public string id = string.Empty;
        public int hp;
        public int sp;
        public int level = 1;
        public int xp;
        public int maxHp = 1;
        public int maxSp;
        public int baseToHit;
        public int baseMissile;
        public int pendingLevelUps;
        public int brawn;
        public int knowledge;
        public int judgment;
        public int agility;
        public int vitality;
        public int luck;
        public int[] knownSpellIds = Array.Empty<int>();
        public int exhaustionStacks;
        public int curseStacks;
        public SavedConditionState[] conditions = Array.Empty<SavedConditionState>();
        public SavedEquippedEntry[] equippedEntries = Array.Empty<SavedEquippedEntry>();
        public int[] equippedItemIds = Array.Empty<int>();
    }

    [Serializable]
    public sealed class SavedStashEntry
    {
        public int itemId;
        public int quantity;
        public bool isIdentified = true;
        public int remainingCharges;
    }

    [Serializable]
    public sealed class BywaterSaveData
    {
        public int version = BywaterSaveService.CurrentSaveVersion;
        public string mapId = "land_0";
        public int x;
        public int y;
        public string facing = "South";
        public int gold;
        public bool hasBoat;
        public int torchTurnsRemaining;
        public int quickBandageCharges = PartyState.MaxQuickBandageCharges;
        public int turnCount;
        public ulong prngState;
        public string prngStateText = "0";
        public string[] clearedEncounterIds = Array.Empty<string>();
        public SavedMemberState[] members = Array.Empty<SavedMemberState>();
        public SavedStashEntry[] stash = Array.Empty<SavedStashEntry>();
        public string[] combatLogLines = Array.Empty<string>();
    }

    /// <summary>
    /// Atomic JSON persistence service for City of Bywater exploration sessions (v3 schema with v1/v2 backward compatibility).
    /// </summary>
    public static class BywaterSaveService
    {
        public const int CurrentSaveVersion = 3;
        private const ulong DefaultFallbackSeed = 0x5245414C4D5AUL;

        public static BywaterSaveData CaptureSnapshot(
            ExplorationSession session,
            IReadOnlyList<string> combatLogLines = null)
        {
            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            ulong seedState = (session.Roller as DeterministicDiceRoller)?.State ?? 0UL;

            List<string> clearedIds = new List<string>(session.ClearedEncounterIds.Count);
            foreach (string id in session.ClearedEncounterIds)
            {
                if (!string.IsNullOrWhiteSpace(id))
                {
                    clearedIds.Add(id);
                }
            }
            clearedIds.Sort(StringComparer.OrdinalIgnoreCase);

            SavedStashEntry[] stashEntries = new SavedStashEntry[session.Party.Stash.Count];
            for (int i = 0; i < session.Party.Stash.Count; i++)
            {
                InventoryItem item = session.Party.Stash[i];
                stashEntries[i] = new SavedStashEntry
                {
                    itemId = item.ItemId,
                    quantity = item.Quantity,
                    isIdentified = item.IsIdentified,
                    remainingCharges = item.RemainingCharges
                };
            }

            SavedMemberState[] memberStates = new SavedMemberState[session.Party.Members.Count];
            for (int i = 0; i < session.Party.Members.Count; i++)
            {
                PartyMemberState member = session.Party.Members[i];
                IReadOnlyList<ConditionTracker.ConditionEntry> exportedConditions = member.Conditions.ExportEntries();
                SavedConditionState[] savedConditions = new SavedConditionState[exportedConditions.Count];
                for (int c = 0; c < exportedConditions.Count; c++)
                {
                    ConditionTracker.ConditionEntry entry = exportedConditions[c];
                    savedConditions[c] = new SavedConditionState
                    {
                        id = (int)entry.Id,
                        magnitude = entry.Magnitude,
                        turnsRemaining = entry.TurnsRemaining,
                        baseRecoveryChancePercent = entry.BaseRecoveryChancePercent,
                        canRecoverBySave = entry.CanRecoverBySave,
                        escalationStepPercent = entry.EscalationStepPercent,
                        elapsedRounds = entry.ElapsedRounds
                    };
                }

                List<KeyValuePair<EquipmentSlot, InventoryItem>> sortedEquipped =
                    new List<KeyValuePair<EquipmentSlot, InventoryItem>>(member.Equipped);
                sortedEquipped.Sort((a, b) => ((int)a.Key).CompareTo((int)b.Key));

                SavedEquippedEntry[] equippedEntries = new SavedEquippedEntry[sortedEquipped.Count];
                int[] equippedItemIds = new int[sortedEquipped.Count];
                for (int e = 0; e < sortedEquipped.Count; e++)
                {
                    KeyValuePair<EquipmentSlot, InventoryItem> kvp = sortedEquipped[e];
                    equippedEntries[e] = new SavedEquippedEntry
                    {
                        slot = kvp.Key.ToString(),
                        itemId = kvp.Value.ItemId,
                        remainingCharges = kvp.Value.RemainingCharges
                    };
                    equippedItemIds[e] = kvp.Value.ItemId;
                }

                int[] knownCopy = new int[member.KnownSpellIds.Count];
                for (int ks = 0; ks < member.KnownSpellIds.Count; ks++)
                {
                    knownCopy[ks] = member.KnownSpellIds[ks];
                }

                memberStates[i] = new SavedMemberState
                {
                    id = member.Id,
                    hp = member.Hp,
                    sp = member.Sp,
                    level = member.Level,
                    xp = member.Xp,
                    maxHp = member.MaxHp,
                    maxSp = member.MaxSp,
                    baseToHit = member.BaseToHit,
                    baseMissile = member.BaseMissile,
                    pendingLevelUps = member.PendingLevelUps,
                    brawn = member.Brawn,
                    knowledge = member.Knowledge,
                    judgment = member.Judgment,
                    agility = member.Agility,
                    vitality = member.Vitality,
                    luck = member.Luck,
                    knownSpellIds = knownCopy,
                    exhaustionStacks = member.ExhaustionStacks,
                    curseStacks = member.CurseStacks,
                    conditions = savedConditions,
                    equippedEntries = equippedEntries,
                    equippedItemIds = equippedItemIds
                };
            }

            string[] logCopy = Array.Empty<string>();
            if (combatLogLines != null && combatLogLines.Count > 0)
            {
                logCopy = new string[combatLogLines.Count];
                for (int i = 0; i < combatLogLines.Count; i++)
                {
                    logCopy[i] = combatLogLines[i] ?? string.Empty;
                }
            }

            return new BywaterSaveData
            {
                version = CurrentSaveVersion,
                mapId = session.CurrentMap.MapId,
                x = session.Position.X,
                y = session.Position.Y,
                facing = string.IsNullOrWhiteSpace(session.Facing) ? "South" : session.Facing,
                gold = session.Party.Gold,
                hasBoat = session.Party.HasBoat,
                torchTurnsRemaining = session.Party.TorchTurnsRemaining,
                quickBandageCharges = session.Party.QuickBandageCharges,
                turnCount = session.TurnCount,
                prngState = seedState,
                prngStateText = seedState.ToString(CultureInfo.InvariantCulture),
                clearedEncounterIds = clearedIds.ToArray(),
                members = memberStates,
                stash = stashEntries,
                combatLogLines = logCopy
            };
        }

        public static void ApplySnapshot(
            BywaterSaveData data,
            ExplorationSession session,
            IReadOnlyDictionary<int, ItemDefinition> items,
            out ulong restoredSeed)
        {
            if (data == null)
            {
                throw new ArgumentNullException(nameof(data));
            }

            if (session == null)
            {
                throw new ArgumentNullException(nameof(session));
            }

            restoredSeed = ResolveRestoredSeed(data);
            session.RestorePersistenceState(data.turnCount, data.clearedEncounterIds);

            PartyState party = session.Party;
            party.Gold = Math.Max(0, data.gold);
            party.HasBoat = data.hasBoat;
            party.TorchTurnsRemaining = Math.Max(0, data.torchTurnsRemaining);
            party.QuickBandageCharges = data.version >= 3
                ? Clamp(data.quickBandageCharges, 0, PartyState.MaxQuickBandageCharges)
                : PartyState.MaxQuickBandageCharges;

            ClearStash(party);
            if (items != null)
            {
                EquipmentSlot[] allSlots = (EquipmentSlot[])Enum.GetValues(typeof(EquipmentSlot));
                for (int i = 0; i < party.Members.Count; i++)
                {
                    for (int s = 0; s < allSlots.Length; s++)
                    {
                        party.UnequipToStash(i, allSlots[s], items);
                    }
                }
                ClearStash(party);
            }

            if (data.members != null)
            {
                for (int i = 0; i < data.members.Length; i++)
                {
                    SavedMemberState saved = data.members[i];
                    if (saved == null)
                    {
                        continue;
                    }

                    int memberIndex = FindMemberIndex(party.Members, saved.id, i);
                    if (memberIndex < 0)
                    {
                        continue;
                    }

                    PartyMemberState member = party.Members[memberIndex];
                    member.Level = Math.Max(1, saved.level);
                    if (data.version >= 2 || saved.xp > 0)
                    {
                        member.Xp = Math.Max(0, saved.xp);
                    }

                    if (saved.maxHp > 0)
                    {
                        member.MaxHp = Math.Max(1, saved.maxHp);
                    }

                    if (data.version >= 2 || saved.maxSp > 0)
                    {
                        member.MaxSp = Math.Max(0, saved.maxSp);
                    }

                    if (data.version >= 2)
                    {
                        member.BaseToHit = saved.baseToHit;
                        member.BaseMissile = saved.baseMissile;
                    }

                    if (data.version >= 3)
                    {
                        member.PendingLevelUps = Math.Max(0, saved.pendingLevelUps);
                        if (saved.brawn > 0) member.Brawn = saved.brawn;
                        if (saved.knowledge > 0) member.Knowledge = saved.knowledge;
                        if (saved.judgment > 0) member.Judgment = saved.judgment;
                        if (saved.agility > 0) member.Agility = saved.agility;
                        if (saved.vitality > 0) member.Vitality = saved.vitality;
                        if (saved.luck > 0) member.Luck = saved.luck;
                        if (saved.knownSpellIds != null)
                        {
                            for (int ks = 0; ks < saved.knownSpellIds.Length; ks++)
                            {
                                member.AddKnownSpell(saved.knownSpellIds[ks]);
                            }
                        }
                    }
                    else
                    {
                        member.PendingLevelUps = 0;
                    }

                    member.Hp = Clamp(saved.hp, 0, member.MaxHp);
                    member.Sp = Clamp(saved.sp, 0, member.MaxSp);
                    member.ExhaustionStacks = Math.Max(0, saved.exhaustionStacks);
                    member.CurseStacks = Math.Max(0, saved.curseStacks);

                    member.Conditions.Clear();
                    if (saved.conditions != null)
                    {
                        for (int c = 0; c < saved.conditions.Length; c++)
                        {
                            SavedConditionState cond = saved.conditions[c];
                            if (cond == null || !Enum.IsDefined(typeof(CharConditionId), cond.id))
                            {
                                continue;
                            }

                            member.Conditions.RestoreEntry(
                                (CharConditionId)cond.id,
                                cond.magnitude,
                                cond.turnsRemaining,
                                Clamp(cond.baseRecoveryChancePercent, 0, 100),
                                cond.canRecoverBySave,
                                Clamp(cond.escalationStepPercent, 0, 100),
                                Math.Max(0, cond.elapsedRounds));
                        }
                    }

                    if (items != null)
                    {
                        RestoreMemberEquipment(party, memberIndex, member, saved, data.version, items);
                    }
                }
            }

            if (data.stash != null)
            {
                for (int i = 0; i < data.stash.Length; i++)
                {
                    SavedStashEntry entry = data.stash[i];
                    if (entry == null || entry.itemId < 0 || entry.quantity <= 0)
                    {
                        continue;
                    }

                    party.AddItemToStash(
                        entry.itemId,
                        entry.quantity,
                        entry.isIdentified,
                        entry.remainingCharges);
                }
            }
        }

        public static bool TryWriteSave(string path, BywaterSaveData data)
        {
            if (string.IsNullOrWhiteSpace(path) || data == null)
            {
                Debug.LogWarning("[BywaterSaveService] Cannot write save: path or snapshot data is null.");
                return false;
            }

            if (data.version <= 0)
            {
                data.version = CurrentSaveVersion;
            }

            if (string.IsNullOrWhiteSpace(data.prngStateText))
            {
                data.prngStateText = data.prngState.ToString(CultureInfo.InvariantCulture);
            }

            string fullPath;
            string tempPath = string.Empty;
            string backupPath = string.Empty;

            try
            {
                fullPath = Path.GetFullPath(path);
                string dir = Path.GetDirectoryName(fullPath);
                if (!string.IsNullOrEmpty(dir))
                {
                    Directory.CreateDirectory(dir);
                }

                string json = JsonUtility.ToJson(data, true);
                if (string.IsNullOrWhiteSpace(json))
                {
                    Debug.LogWarning("[BywaterSaveService] Serialized save JSON was empty.");
                    return false;
                }

                tempPath = fullPath + ".tmp";
                backupPath = fullPath + ".bak";

                File.WriteAllText(tempPath, json);
                FileInfo tmpInfo = new FileInfo(tempPath);
                if (!tmpInfo.Exists || tmpInfo.Length <= 0)
                {
                    Debug.LogWarning("[BywaterSaveService] Temporary save validation failed: " + tempPath);
                    return false;
                }

                if (File.Exists(fullPath))
                {
                    File.Replace(tempPath, fullPath, backupPath, ignoreMetadataErrors: true);
                    if (File.Exists(backupPath))
                    {
                        try
                        {
                            File.Delete(backupPath);
                        }
                        catch (Exception)
                        {
                            // Best-effort cleanup of backup file.
                        }
                    }
                }
                else
                {
                    File.Move(tempPath, fullPath);
                }

                return true;
            }
            catch (Exception ex)
            {
                Debug.LogWarning("[BywaterSaveService] Atomic save write failed: " + ex.Message);
                if (!string.IsNullOrEmpty(tempPath) && File.Exists(tempPath))
                {
                    try
                    {
                        File.Delete(tempPath);
                    }
                    catch (Exception)
                    {
                        // Ignore cleanup errors.
                    }
                }
                return false;
            }
        }

        public static bool TryLoadSave(string path, out BywaterSaveData data)
        {
            return TryLoadSave(path, out data, out _);
        }

        public static bool TryLoadSave(string path, out BywaterSaveData data, out string status)
        {
            data = null;
            status = string.Empty;

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                status = "Save file not found.";
                Debug.LogWarning("[BywaterSaveService] " + status + " Path: " + (path ?? "<null>"));
                return false;
            }

            try
            {
                string json = File.ReadAllText(path);
                if (string.IsNullOrWhiteSpace(json))
                {
                    status = "Save file is empty.";
                    Debug.LogWarning("[BywaterSaveService] " + status);
                    return false;
                }

                object parsed = MiniJson.Parse(json);
                IReadOnlyDictionary<string, object> root = MiniJson.AsObject(parsed);

                int version = MiniJson.GetInt(root, "version", 0);
                if (version <= 0)
                {
                    status = "Save file is missing a valid version field.";
                    Debug.LogWarning("[BywaterSaveService] " + status);
                    return false;
                }

                string mapId = MiniJson.GetString(root, "mapId", string.Empty);
                if (string.IsNullOrWhiteSpace(mapId))
                {
                    status = "Save file is missing required mapId field.";
                    Debug.LogWarning("[BywaterSaveService] " + status);
                    return false;
                }

                bool hasMembers = MiniJson.TryGetValue(root, "members", out object rawMembers) && rawMembers != null;
                bool hasHeroes = MiniJson.TryGetValue(root, "heroes", out object rawHeroes) && rawHeroes != null;

                if (version == 1 || (!hasMembers && hasHeroes))
                {
                    data = ParseV1SaveData(root, mapId, version);
                }
                else
                {
                    if (!hasMembers)
                    {
                        status = "Save file is missing required members field.";
                        Debug.LogWarning("[BywaterSaveService] " + status);
                        return false;
                    }

                    data = JsonUtility.FromJson<BywaterSaveData>(json);
                    if (data == null || string.IsNullOrWhiteSpace(data.mapId))
                    {
                        data = null;
                        status = "Save file deserialization produced invalid snapshot data.";
                        Debug.LogWarning("[BywaterSaveService] " + status);
                        return false;
                    }

                    NormalizeLoadedSaveData(data);
                }

                status = "Loaded save snapshot.";
                return true;
            }
            catch (Exception ex)
            {
                data = null;
                status = "Failed to load save: " + ex.Message;
                Debug.LogWarning("[BywaterSaveService] " + status);
                return false;
            }
        }

        public static bool Save(string path, ExplorationSession session)
        {
            if (string.IsNullOrWhiteSpace(path) || session == null)
            {
                return false;
            }

            BywaterSaveData data = CaptureSnapshot(session);
            return TryWriteSave(path, data);
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

            if (!TryLoadSave(path, out BywaterSaveData data, out status))
            {
                return false;
            }

            string mapId = string.IsNullOrWhiteSpace(data.mapId) ? "land_0" : data.mapId;
            if (!bundle.Maps.TryGetValue(mapId, out TileMapData targetMap))
            {
                status = "Saved map not found in bundle: " + mapId;
                Debug.LogWarning("[BywaterSaveService] " + status);
                return false;
            }

            GridCoord startPos = targetMap.InBounds(data.x, data.y)
                ? new GridCoord(data.x, data.y)
                : targetMap.StartSpawn;

            ulong restoredSeed = ResolveRestoredSeed(data);
            PartyState party = PartyState.FromBundle(bundle);
            session = new ExplorationSession(
                bundle.Maps,
                targetMap.MapId,
                party,
                new DeterministicDiceRoller(restoredSeed),
                startPosition: startPos,
                initialFacing: data.facing,
                initialTurnCount: data.turnCount,
                clearedEncounterIds: data.clearedEncounterIds);

            ApplySnapshot(data, session, bundle.Items, out _);

            status = string.Format(
                CultureInfo.InvariantCulture,
                "Loaded save at {0} ({1}, {2}).",
                targetMap.MapId,
                startPos.X,
                startPos.Y);
            return true;
        }

        private static BywaterSaveData ParseV1SaveData(
            IReadOnlyDictionary<string, object> root,
            string mapId,
            int version)
        {
            List<SavedMemberState> members = new List<SavedMemberState>();
            if (MiniJson.TryGetValue(root, "heroes", out object rawHeroes) && rawHeroes != null)
            {
                IReadOnlyList<object> heroArray = MiniJson.AsArray(rawHeroes);
                for (int i = 0; i < heroArray.Count; i++)
                {
                    if (heroArray[i] == null)
                    {
                        continue;
                    }

                    IReadOnlyDictionary<string, object> heroObj = MiniJson.AsObject(heroArray[i]);
                    List<SavedEquippedEntry> equippedEntries = new List<SavedEquippedEntry>();
                    List<int> equippedIds = new List<int>();

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
                            equippedEntries.Add(new SavedEquippedEntry
                            {
                                slot = kvp.Key,
                                itemId = itemId,
                                remainingCharges = -1
                            });
                            equippedIds.Add(itemId);
                        }
                    }

                    members.Add(new SavedMemberState
                    {
                        id = MiniJson.GetString(heroObj, "id", string.Empty),
                        hp = MiniJson.GetInt(heroObj, "hp", 1),
                        sp = MiniJson.GetInt(heroObj, "sp", 0),
                        level = Math.Max(1, MiniJson.GetInt(heroObj, "level", 1)),
                        xp = Math.Max(0, MiniJson.GetInt(heroObj, "xp", 0)),
                        maxHp = MiniJson.GetInt(heroObj, "maxHp", 0),
                        maxSp = MiniJson.GetInt(heroObj, "maxSp", 0),
                        exhaustionStacks = Math.Max(0, MiniJson.GetInt(heroObj, "exhaustionStacks", 0)),
                        curseStacks = Math.Max(0, MiniJson.GetInt(heroObj, "curseStacks", 0)),
                        conditions = Array.Empty<SavedConditionState>(),
                        equippedEntries = equippedEntries.ToArray(),
                        equippedItemIds = equippedIds.ToArray()
                    });
                }
            }

            List<SavedStashEntry> stash = new List<SavedStashEntry>();
            if (MiniJson.TryGetValue(root, "stash", out object rawStash) && rawStash != null)
            {
                IReadOnlyList<object> stashArray = MiniJson.AsArray(rawStash);
                for (int i = 0; i < stashArray.Count; i++)
                {
                    if (stashArray[i] == null)
                    {
                        continue;
                    }

                    IReadOnlyDictionary<string, object> itemObj = MiniJson.AsObject(stashArray[i]);
                    int itemId = MiniJson.GetInt(itemObj, "itemId", -1);
                    int quantity = MiniJson.GetInt(itemObj, "quantity", 0);
                    if (itemId < 0 || quantity <= 0)
                    {
                        continue;
                    }

                    stash.Add(new SavedStashEntry
                    {
                        itemId = itemId,
                        quantity = quantity,
                        isIdentified = MiniJson.GetBool(itemObj, "isIdentified", true),
                        remainingCharges = MiniJson.GetInt(itemObj, "remainingCharges", 0)
                    });
                }
            }

            return new BywaterSaveData
            {
                version = version,
                mapId = mapId,
                x = MiniJson.GetInt(root, "x", 0),
                y = MiniJson.GetInt(root, "y", 0),
                facing = MiniJson.GetString(root, "facing", "South"),
                gold = Math.Max(0, MiniJson.GetInt(root, "gold", 0)),
                hasBoat = MiniJson.GetBool(root, "hasBoat", false),
                torchTurnsRemaining = Math.Max(0, MiniJson.GetInt(root, "torchTurnsRemaining", 0)),
                turnCount = Math.Max(0, MiniJson.GetInt(root, "turnCount", 0)),
                prngState = DefaultFallbackSeed,
                prngStateText = DefaultFallbackSeed.ToString(CultureInfo.InvariantCulture),
                clearedEncounterIds = Array.Empty<string>(),
                members = members.ToArray(),
                stash = stash.ToArray(),
                combatLogLines = Array.Empty<string>()
            };
        }

        private static void NormalizeLoadedSaveData(BywaterSaveData data)
        {
            data.facing = string.IsNullOrWhiteSpace(data.facing) ? "South" : data.facing;
            data.clearedEncounterIds ??= Array.Empty<string>();
            data.members ??= Array.Empty<SavedMemberState>();
            data.stash ??= Array.Empty<SavedStashEntry>();
            data.combatLogLines ??= Array.Empty<string>();

            if (string.IsNullOrWhiteSpace(data.prngStateText))
            {
                data.prngStateText = data.prngState.ToString(CultureInfo.InvariantCulture);
            }
            else if (ulong.TryParse(data.prngStateText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed))
            {
                data.prngState = parsed;
            }
        }

        private static ulong ResolveRestoredSeed(BywaterSaveData data)
        {
            if (data == null)
            {
                return DefaultFallbackSeed;
            }

            if (!string.IsNullOrWhiteSpace(data.prngStateText) &&
                ulong.TryParse(data.prngStateText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out ulong parsed))
            {
                if (data.version < 2 && parsed == 0UL && data.prngState == 0UL)
                {
                    return DefaultFallbackSeed;
                }

                return parsed;
            }

            if (data.version < 2 && data.prngState == 0UL)
            {
                return DefaultFallbackSeed;
            }

            return data.prngState;
        }

        private static void RestoreMemberEquipment(
            PartyState party,
            int memberIndex,
            PartyMemberState member,
            SavedMemberState saved,
            int saveVersion,
            IReadOnlyDictionary<int, ItemDefinition> items)
        {
            if (saved.equippedEntries != null && saved.equippedEntries.Length > 0)
            {
                for (int e = 0; e < saved.equippedEntries.Length; e++)
                {
                    SavedEquippedEntry eq = saved.equippedEntries[e];
                    if (eq == null || !items.TryGetValue(eq.itemId, out ItemDefinition itemDef))
                    {
                        continue;
                    }

                    int charges = ResolveEquippedCharges(saveVersion, eq, itemDef);
                    party.AddItemToStash(eq.itemId, 1, isIdentified: true, charges: charges);
                    int tempIdx = party.Stash.Count - 1;
                    if (party.EquipFromStash(memberIndex, tempIdx, items))
                    {
                        if (PartyMemberState.TryParseSlot(itemDef.Slot, out EquipmentSlot slot))
                        {
                            InventoryItem equipped = member.GetEquipped(slot);
                            if (equipped != null)
                            {
                                equipped.RemainingCharges = charges;
                            }
                        }
                    }
                    else
                    {
                        ClearStash(party);
                    }
                }

                ClearStash(party);
                return;
            }

            if (saved.equippedItemIds != null && saved.equippedItemIds.Length > 0)
            {
                for (int e = 0; e < saved.equippedItemIds.Length; e++)
                {
                    int itemId = saved.equippedItemIds[e];
                    if (!items.TryGetValue(itemId, out ItemDefinition itemDef))
                    {
                        continue;
                    }

                    party.AddItemToStash(itemId, 1, isIdentified: true, charges: itemDef.Charges);
                    int tempIdx = party.Stash.Count - 1;
                    if (!party.EquipFromStash(memberIndex, tempIdx, items))
                    {
                        ClearStash(party);
                    }
                }

                ClearStash(party);
            }
        }

        private static int ResolveEquippedCharges(int saveVersion, SavedEquippedEntry eq, ItemDefinition itemDef)
        {
            if (saveVersion < 2 && eq.remainingCharges < 0)
            {
                return itemDef.Charges;
            }

            return Math.Max(0, eq.remainingCharges);
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
