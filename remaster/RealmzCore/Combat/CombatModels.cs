using System;
using System.Collections.Generic;
using RealmzCore.Data;
using RealmzCore.Exploration;
using RealmzCore.Rules;

namespace RealmzCore.Combat
{
    public enum CombatSide
    {
        Hero = 0,
        Monster = 1
    }

    public enum CombatOutcome
    {
        InProgress = 0,
        Victory = 1,
        Defeat = 2
    }

    public enum LosKind
    {
        Clear = 0,
        PartialCover = 1,
        Blocked = 2
    }

    public sealed class CombatGrid
    {
        private readonly bool[] _blocked;
        private readonly bool[] _blocksLos;
        private readonly bool[] _providesCover;
        private readonly int[] _moveCost;

        public CombatGrid(int width, int height)
        {
            if (width <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(width));
            }

            if (height <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(height));
            }

            Width = width;
            Height = height;
            int count = width * height;
            _blocked = new bool[count];
            _blocksLos = new bool[count];
            _providesCover = new bool[count];
            _moveCost = new int[count];
            for (int i = 0; i < count; i++)
            {
                _moveCost[i] = 1;
            }
        }

        public int Width { get; }
        public int Height { get; }

        public static CombatGrid FromTileMapSlice(
            TileMapData map,
            int originX,
            int originY,
            int width,
            int height,
            bool hasBoat = false)
        {
            if (map == null)
            {
                throw new ArgumentNullException(nameof(map));
            }

            CombatGrid grid = new CombatGrid(width, height);
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    int mx = originX + x;
                    int my = originY + y;
                    if (!map.InBounds(mx, my))
                    {
                        grid.SetBlocked(x, y, blocksLos: true);
                        continue;
                    }

                    TileRule rule = map.GetTileRule(map.GetTile(mx, my));
                    bool passable = rule.NeedsBoat ? hasBoat : rule.Passable;
                    if (!passable)
                    {
                        grid.SetBlocked(x, y, blocksLos: rule.BlocksLos);
                    }
                    else
                    {
                        grid.SetMoveCost(x, y, rule.MoveCost);
                        if (rule.IsForest || rule.BlocksLos)
                        {
                            grid.SetCover(x, y, rule.MoveCost, rule.BlocksLos);
                        }
                    }
                }
            }

            return grid;
        }

        public bool InBounds(int x, int y)
        {
            return x >= 0 && x < Width && y >= 0 && y < Height;
        }

        public bool InBounds(GridCoord coord)
        {
            return InBounds(coord.X, coord.Y);
        }

        public void SetBlocked(int x, int y, bool blocksLos = true)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            int idx = (y * Width) + x;
            _blocked[idx] = true;
            _blocksLos[idx] = blocksLos;
        }

        public void SetCover(int x, int y, int moveCost = 2, bool blocksLos = false)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            int idx = (y * Width) + x;
            _providesCover[idx] = true;
            _blocksLos[idx] = blocksLos;
            _moveCost[idx] = moveCost < 1 ? 1 : moveCost;
        }

        public void SetMoveCost(int x, int y, int moveCost)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            _moveCost[(y * Width) + x] = moveCost < 1 ? 1 : moveCost;
        }

        public bool IsWalkable(int x, int y)
        {
            return InBounds(x, y) && !_blocked[(y * Width) + x];
        }

        public bool BlocksLos(int x, int y)
        {
            return !InBounds(x, y) || _blocksLos[(y * Width) + x];
        }

        public bool ProvidesCover(int x, int y)
        {
            return InBounds(x, y) && _providesCover[(y * Width) + x];
        }

        public int GetMoveCost(int x, int y)
        {
            return InBounds(x, y) ? _moveCost[(y * Width) + x] : int.MaxValue;
        }
    }

    public sealed class CombatantState
    {
        private readonly List<int> _spellIds;
        private readonly int[] _saves;
        private readonly bool[] _spellImmunities;

        public CombatantState(
            string id,
            string name,
            CombatSide side,
            GridCoord position,
            int hp,
            int maxHp,
            int sp = 0,
            int maxSp = 0,
            int ac = 10,
            int toHit = 10,
            int dodge = 10,
            int missileSkill = 0,
            int movementMax = 6,
            int dexterity = 10,
            int numAttacks = 1,
            string weaponDice = "1d4",
            int weaponBonus = 0,
            int damageBonus = 0,
            string rangedDice = "",
            int rangedRange = 0,
            IReadOnlyList<int>? spellIds = null,
            int magicResistance = 0,
            IReadOnlyList<int>? saves = null,
            IReadOnlyList<bool>? spellImmunities = null,
            bool isEvil = false,
            bool isUndead = false,
            int xpReward = 0,
            int goldReward = 0,
            int iconId = 0,
            int portraitId = 0)
        {
            Id = id ?? throw new ArgumentNullException(nameof(id));
            Name = name ?? string.Empty;
            Side = side;
            Position = position;
            TurnStartPosition = position;
            MaxHp = Math.Max(1, maxHp);
            Hp = Math.Clamp(hp, 0, MaxHp);
            MaxSp = Math.Max(0, maxSp);
            Sp = Math.Clamp(sp, 0, MaxSp);
            Ac = ac;
            ToHit = toHit;
            Dodge = dodge;
            MissileSkill = missileSkill;
            MovementMax = Math.Max(1, movementMax);
            Dexterity = dexterity;
            NumAttacks = Math.Max(1, numAttacks);
            WeaponDice = string.IsNullOrWhiteSpace(weaponDice) ? "1d4" : weaponDice;
            WeaponBonus = weaponBonus;
            DamageBonus = damageBonus;
            RangedDice = rangedDice ?? string.Empty;
            RangedRange = Math.Max(0, rangedRange);
            _spellIds = spellIds != null ? new List<int>(spellIds) : new List<int>();
            MagicResistance = magicResistance;

            _saves = new int[6];
            if (saves != null)
            {
                int count = Math.Min(6, saves.Count);
                for (int i = 0; i < count; i++)
                {
                    _saves[i] = saves[i];
                }
            }

            _spellImmunities = new bool[6];
            if (spellImmunities != null)
            {
                int count = Math.Min(6, spellImmunities.Count);
                for (int i = 0; i < count; i++)
                {
                    _spellImmunities[i] = spellImmunities[i];
                }
            }

            IsEvil = isEvil;
            IsUndead = isUndead;
            XpReward = xpReward;
            GoldReward = goldReward;
            IconId = iconId;
            PortraitId = portraitId;
            Conditions = new ConditionTracker();
            Economy = new CombatActionEconomy(MovementMax);
        }

        public static CombatantState FromHero(
            PartyMemberState hero,
            GridCoord position,
            IReadOnlyDictionary<int, ItemDefinition>? catalog = null)
        {
            if (hero == null)
            {
                throw new ArgumentNullException(nameof(hero));
            }

            int effectiveAc = hero.GetEffectiveAc(catalog);
            int effectiveToHit = hero.GetEffectiveToHit(catalog);
            string weaponDice = hero.GetEffectiveDamageDice(catalog);
            int weaponBonus = hero.GetEffectiveWeaponBonus(catalog);
            int rangedRange = hero.GetEffectiveRangedRange(catalog);
            string rangedDice = hero.GetEffectiveRangedDice(catalog);
            int moveMax = hero.GetEffectiveMovementMax(catalog);
            int attacks = Math.Clamp((int)Math.Floor(hero.AttacksPerRound), 1, 5);
            int damageBonus = Math.Max(0, (hero.Brawn - 12) / 3);

            CombatantState state = new CombatantState(
                id: hero.Id,
                name: hero.Name,
                side: CombatSide.Hero,
                position: position,
                hp: hero.Hp,
                maxHp: hero.MaxHp,
                sp: hero.Sp,
                maxSp: hero.MaxSp,
                ac: effectiveAc,
                toHit: effectiveToHit,
                dodge: hero.Dodge,
                missileSkill: hero.BaseMissile > 0 ? hero.BaseMissile : effectiveToHit,
                movementMax: moveMax,
                dexterity: hero.Agility,
                numAttacks: attacks,
                weaponDice: weaponDice,
                weaponBonus: weaponBonus,
                damageBonus: damageBonus,
                rangedDice: rangedDice,
                rangedRange: rangedRange,
                spellIds: hero.KnownSpellIds,
                magicResistance: 0,
                saves: null,
                spellImmunities: null,
                isEvil: false,
                isUndead: false,
                xpReward: 0,
                goldReward: 0,
                iconId: hero.IconId,
                portraitId: hero.PortraitId);
            state.ExhaustionStacks = hero.ExhaustionStacks;
            state.CurseStacks = hero.CurseStacks;
            return state;
        }

        public static CombatantState FromMonster(
            MonsterDefinition monster,
            string instanceId,
            GridCoord position,
            IDiceRoller? roller = null)
        {
            if (monster == null)
            {
                throw new ArgumentNullException(nameof(monster));
            }

            int hp = monster.HpDefault > 0 ? monster.HpDefault : Math.Max(1, monster.HpMax);
            if (roller != null && monster.HpMax > monster.HpMin && monster.HpMin > 0)
            {
                hp = roller.NextInt(monster.HpMin, monster.HpMax + 1);
            }

            int gold = monster.GoldMin;
            if (roller != null && monster.GoldMax > monster.GoldMin)
            {
                gold = roller.NextInt(monster.GoldMin, monster.GoldMax + 1);
            }

            return new CombatantState(
                id: instanceId,
                name: monster.Name,
                side: CombatSide.Monster,
                position: position,
                hp: hp,
                maxHp: hp,
                sp: monster.SpellIds.Count > 0 ? 40 : 0,
                maxSp: monster.SpellIds.Count > 0 ? 40 : 0,
                ac: monster.Ac,
                toHit: monster.ToHitBonus,
                dodge: monster.Ac,
                missileSkill: monster.ToHitBonus,
                movementMax: Math.Max(1, monster.MovementMax),
                dexterity: monster.Dexterity,
                numAttacks: Math.Max(1, monster.NumAttacks),
                weaponDice: monster.AttackDice,
                weaponBonus: 0,
                damageBonus: monster.DamageBonus,
                rangedDice: monster.RangedDice,
                rangedRange: monster.RangedRange,
                spellIds: monster.SpellIds,
                magicResistance: monster.MagicResistance,
                saves: monster.Saves,
                spellImmunities: monster.SpellImmunities,
                isEvil: monster.IsEvil,
                isUndead: monster.IsUndead,
                xpReward: monster.XpReward,
                goldReward: gold,
                iconId: monster.IconId,
                portraitId: 0);
        }

        public string Id { get; }
        public string Name { get; }
        public CombatSide Side { get; }
        public GridCoord Position { get; set; }
        public int Hp { get; set; }
        public int MaxHp { get; set; }
        public int Sp { get; set; }
        public int MaxSp { get; set; }
        public int Ac { get; set; }
        public int ToHit { get; set; }
        public int Dodge { get; set; }
        public int MissileSkill { get; set; }
        public int MovementMax { get; set; }
        public int Dexterity { get; set; }
        public int NumAttacks { get; set; }
        public string WeaponDice { get; set; }
        public int WeaponBonus { get; set; }
        public int DamageBonus { get; set; }
        public string RangedDice { get; set; }
        public int RangedRange { get; set; }
        public IReadOnlyList<int> SpellIds => _spellIds;
        public int MagicResistance { get; set; }
        public IReadOnlyList<int> Saves => _saves;
        public IReadOnlyList<bool> SpellImmunities => _spellImmunities;
        public bool IsEvil { get; }
        public bool IsUndead { get; }
        public ConditionTracker Conditions { get; }
        public CombatActionEconomy Economy { get; }
        public int ExhaustionStacks { get; set; }
        public int CurseStacks { get; set; }
        public bool IsGuarding { get; set; }
        public GridCoord TurnStartPosition { get; set; }
        public bool HasTriggeredIrreversibleEvent { get; set; }
        public int XpReward { get; }
        public int GoldReward { get; }
        public int IconId { get; }
        public int PortraitId { get; }

        public bool IsAlive => Hp > 0;
        public bool IsConscious => Hp > 0 &&
                                   !Conditions.Has(CharConditionId.Helpless) &&
                                   !Conditions.Has(CharConditionId.TurnedToStone);

        public int EffectiveAc
        {
            get
            {
                int ac = Ac;
                if (Conditions.Has(CharConditionId.DefenseBonus)) ac += 5;
                if (Conditions.Has(CharConditionId.ShieldFromHits)) ac += 5;
                if (IsGuarding) ac += 4;
                if (Conditions.Has(CharConditionId.HinderedDefense)) ac -= 5;
                ac -= ExhaustionStacks + CurseStacks;
                return ac;
            }
        }

        public int EffectiveToHit
        {
            get
            {
                int th = ToHit;
                if (Conditions.Has(CharConditionId.AttackBonus)) th += 10;
                if (Conditions.Has(CharConditionId.HinderedAttacks)) th -= 10;
                th -= (ExhaustionStacks + CurseStacks) * 2;
                return th;
            }
        }

        public int EffectiveMissileSkill
        {
            get
            {
                int baseSkill = MissileSkill > 0 ? MissileSkill : ToHit;
                if (Conditions.Has(CharConditionId.AttackBonus)) baseSkill += 10;
                if (Conditions.Has(CharConditionId.HinderedAttacks)) baseSkill -= 10;
                baseSkill -= (ExhaustionStacks + CurseStacks) * 2;
                return baseSkill;
            }
        }

        public int EffectiveDexterity
        {
            get
            {
                int dex = Dexterity;
                if (Conditions.Has(CharConditionId.Speedy)) dex += 4;
                if (Conditions.Has(CharConditionId.Slow)) dex -= 4;
                return dex;
            }
        }

        public int EffectiveMovement
        {
            get
            {
                int move = MovementMax;
                if (Conditions.Has(CharConditionId.Speedy)) move += 4;
                if (Conditions.Has(CharConditionId.Slow)) move -= 4;
                if (Conditions.Has(CharConditionId.Tangled)) move /= 2;
                return Math.Max(1, move);
            }
        }
    }

    public sealed class InitiativeEntry
    {
        public InitiativeEntry(
            string combatantId,
            string name,
            CombatSide side,
            int roundNumber,
            int effectiveDexterity,
            bool isCurrentTurn)
        {
            CombatantId = combatantId;
            Name = name;
            Side = side;
            RoundNumber = roundNumber;
            EffectiveDexterity = effectiveDexterity;
            IsCurrentTurn = isCurrentTurn;
        }

        public string CombatantId { get; }
        public string Name { get; }
        public CombatSide Side { get; }
        public int RoundNumber { get; }
        public int EffectiveDexterity { get; }
        public bool IsCurrentTurn { get; }
    }

    public sealed class ReachableTileInfo
    {
        public ReachableTileInfo(
            GridCoord coord,
            int pathCost,
            IReadOnlyList<GridCoord> path,
            bool provokesAoO,
            IReadOnlyList<string> threateningEnemyIds)
        {
            Coord = coord;
            PathCost = pathCost;
            Path = path ?? Array.Empty<GridCoord>();
            ProvokesAoO = provokesAoO;
            ThreateningEnemyIds = threateningEnemyIds ?? Array.Empty<string>();
        }

        public GridCoord Coord { get; }
        public int PathCost { get; }
        public IReadOnlyList<GridCoord> Path { get; }
        public bool ProvokesAoO { get; }
        public IReadOnlyList<string> ThreateningEnemyIds { get; }
    }

    public sealed class LosResult
    {
        public LosResult(LosKind kind, int coverPenaltyPct, GridCoord? blockedBy, IReadOnlyList<GridCoord> rayCells)
        {
            Kind = kind;
            CoverPenaltyPct = coverPenaltyPct;
            BlockedBy = blockedBy;
            RayCells = rayCells ?? Array.Empty<GridCoord>();
        }

        public LosKind Kind { get; }
        public int CoverPenaltyPct { get; }
        public GridCoord? BlockedBy { get; }
        public IReadOnlyList<GridCoord> RayCells { get; }
    }

    public sealed class SpellAoeFootprint
    {
        public SpellAoeFootprint(
            bool inRange,
            GridCoord center,
            IReadOnlyList<GridCoord> affectedTiles,
            IReadOnlyList<string> affectedAllyIds,
            IReadOnlyList<string> affectedEnemyIds,
            IReadOnlyList<GridCoord> blockedTiles)
        {
            InRange = inRange;
            Center = center;
            AffectedTiles = affectedTiles ?? Array.Empty<GridCoord>();
            AffectedAllyIds = affectedAllyIds ?? Array.Empty<string>();
            AffectedEnemyIds = affectedEnemyIds ?? Array.Empty<string>();
            BlockedTiles = blockedTiles ?? Array.Empty<GridCoord>();
        }

        public bool InRange { get; }
        public GridCoord Center { get; }
        public IReadOnlyList<GridCoord> AffectedTiles { get; }
        public IReadOnlyList<string> AffectedAllyIds { get; }
        public IReadOnlyList<string> AffectedEnemyIds { get; }
        public IReadOnlyList<GridCoord> BlockedTiles { get; }
    }

    public sealed class TacticalAttackPreview
    {
        public TacticalAttackPreview(
            bool canExecute,
            string reason,
            int hitChancePct,
            int uncappedHitScore,
            int critChancePct,
            int grazeChancePct,
            int minDamage,
            int maxDamage,
            bool isFlanking,
            bool provokesAoO,
            LosResult? losResult,
            int defenderAc,
            IReadOnlyList<string> conditionTags)
        {
            CanExecute = canExecute;
            Reason = reason ?? string.Empty;
            HitChancePct = hitChancePct;
            UncappedHitScore = uncappedHitScore;
            CritChancePct = critChancePct;
            GrazeChancePct = grazeChancePct;
            MinDamage = minDamage;
            MaxDamage = maxDamage;
            IsFlanking = isFlanking;
            ProvokesAoO = provokesAoO;
            LosResult = losResult;
            DefenderAc = defenderAc;
            ConditionTags = conditionTags ?? Array.Empty<string>();
        }

        public bool CanExecute { get; }
        public string Reason { get; }
        public int HitChancePct { get; }
        public int UncappedHitScore { get; }
        public int CritChancePct { get; }
        public int GrazeChancePct { get; }
        public int MinDamage { get; }
        public int MaxDamage { get; }
        public bool IsFlanking { get; }
        public bool ProvokesAoO { get; }
        public LosResult? LosResult { get; }
        public int DefenderAc { get; }
        public IReadOnlyList<string> ConditionTags { get; }
    }

    public sealed class TacticalSpellPreview
    {
        public TacticalSpellPreview(
            bool canExecute,
            string reason,
            int spCost,
            int minAmount,
            int maxAmount,
            int saveChancePct,
            SpellAoeFootprint aoeFootprint,
            IReadOnlyList<string> conditionTags)
        {
            CanExecute = canExecute;
            Reason = reason ?? string.Empty;
            SpCost = spCost;
            MinAmount = minAmount;
            MaxAmount = maxAmount;
            SaveChancePct = saveChancePct;
            AoeFootprint = aoeFootprint;
            ConditionTags = conditionTags ?? Array.Empty<string>();
        }

        public bool CanExecute { get; }
        public string Reason { get; }
        public int SpCost { get; }
        public int MinAmount { get; }
        public int MaxAmount { get; }
        public int SaveChancePct { get; }
        public SpellAoeFootprint AoeFootprint { get; }
        public IReadOnlyList<string> ConditionTags { get; }
    }

    public sealed class CombatLogEntry
    {
        public CombatLogEntry(
            int turnNumber,
            int roundNumber,
            string actorId,
            string actorName,
            string actionType,
            string message,
            IReadOnlyList<string>? targetIds = null,
            int damageDealt = 0,
            int healingDone = 0)
        {
            TurnNumber = turnNumber;
            RoundNumber = roundNumber;
            ActorId = actorId ?? string.Empty;
            ActorName = actorName ?? string.Empty;
            ActionType = actionType ?? string.Empty;
            Message = message ?? string.Empty;
            TargetIds = targetIds ?? Array.Empty<string>();
            DamageDealt = damageDealt;
            HealingDone = healingDone;
        }

        public int TurnNumber { get; }
        public int RoundNumber { get; }
        public string ActorId { get; }
        public string ActorName { get; }
        public string ActionType { get; }
        public string Message { get; }
        public IReadOnlyList<string> TargetIds { get; }
        public int DamageDealt { get; }
        public int HealingDone { get; }
    }

    public sealed class CombatActionResult
    {
        public CombatActionResult(
            bool success,
            string message,
            IReadOnlyList<AttackOutcome>? attackOutcomes = null,
            int damageDealt = 0,
            int healingDone = 0,
            bool aooTriggered = false)
        {
            Success = success;
            Message = message ?? string.Empty;
            AttackOutcomes = attackOutcomes ?? Array.Empty<AttackOutcome>();
            DamageDealt = damageDealt;
            HealingDone = healingDone;
            AoOTriggered = aooTriggered;
        }

        public bool Success { get; }
        public string Message { get; }
        public IReadOnlyList<AttackOutcome> AttackOutcomes { get; }
        public int DamageDealt { get; }
        public int HealingDone { get; }
        public bool AoOTriggered { get; }
    }
}
