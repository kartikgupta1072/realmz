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

    public enum SurfaceType
    {
        None = 0,
        Water = 1,
        Oil = 2,
        Fire = 3,
        Ice = 4,
        Poison = 5,
        ElectrifiedWater = 6,
        SteamCloud = 7
    }

    public enum CombatIntentActionType
    {
        Hold = 0,
        MeleeAttack = 1,
        RangedAttack = 2,
        CastSpell = 3,
        MoveAndAttack = 4,
        MoveAndRanged = 5,
        MoveAndCast = 6,
        HealAlly = 7,
        Flee = 8,
        Guard = 9
    }

    public sealed class CombatGridCell
    {
        public CombatGridCell()
        {
            MoveCost = 1;
        }

        public CombatGridCell(
            int x,
            int y,
            bool isBlocked = false,
            bool blocksLos = false,
            bool providesCover = false,
            int moveCost = 1,
            short elevationTier = 0,
            SurfaceType surface = SurfaceType.None,
            int surfaceRoundsRemaining = 0)
        {
            X = x;
            Y = y;
            IsBlocked = isBlocked;
            BlocksLos = blocksLos;
            ProvidesCover = providesCover;
            MoveCost = moveCost < 1 ? 1 : moveCost;
            ElevationTier = elevationTier;
            Surface = surface;
            BaseSurface = surface == SurfaceType.Water || surface == SurfaceType.Oil ? surface : SurfaceType.None;
            SurfaceRoundsRemaining = surfaceRoundsRemaining;
        }

        public int X { get; }
        public int Y { get; }
        public GridCoord Coord => new GridCoord(X, Y);
        public bool IsBlocked { get; internal set; }
        public bool BlocksLos { get; internal set; }
        public bool ProvidesCover { get; internal set; }
        public int MoveCost { get; internal set; }
        public short ElevationTier { get; set; }
        public SurfaceType Surface { get; set; }
        public SurfaceType BaseSurface { get; internal set; }
        public int SurfaceRoundsRemaining { get; set; }
    }

    public sealed class SurfaceChangeEvent
    {
        public SurfaceChangeEvent(
            GridCoord coord,
            SurfaceType previousSurface,
            SurfaceType newSurface,
            int roundsRemaining,
            string cause)
        {
            Coord = coord;
            PreviousSurface = previousSurface;
            NewSurface = newSurface;
            RoundsRemaining = roundsRemaining;
            Cause = cause ?? string.Empty;
        }

        public GridCoord Coord { get; }
        public SurfaceType PreviousSurface { get; }
        public SurfaceType NewSurface { get; }
        public int RoundsRemaining { get; }
        public string Cause { get; }
    }

    public sealed class CombatGrid
    {
        private readonly CombatGridCell[] _cells;

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
            _cells = new CombatGridCell[count];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    _cells[(y * width) + x] = new CombatGridCell(x, y);
                }
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
            return FromTileMapSlice(map, originX, originY, width, height, hasBoat, null, null);
        }

        public static CombatGrid FromTileMapSlice(
            TileMapData map,
            int originX,
            int originY,
            int width,
            int height,
            bool hasBoat,
            Func<int, int, short>? elevationResolver,
            Func<int, int, SurfaceType>? surfaceResolver = null)
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

                    if (elevationResolver != null)
                    {
                        grid.SetElevation(x, y, elevationResolver(mx, my));
                    }

                    if (surfaceResolver != null)
                    {
                        SurfaceType surf = surfaceResolver(mx, my);
                        if (surf != SurfaceType.None)
                        {
                            grid.SetSurface(x, y, surf, -1);
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

        public CombatGridCell GetCell(int x, int y)
        {
            if (!InBounds(x, y))
            {
                throw new ArgumentOutOfRangeException($"({x}, {y}) is outside {Width}x{Height} combat grid.");
            }

            return _cells[(y * Width) + x];
        }

        public CombatGridCell GetCell(GridCoord coord)
        {
            return GetCell(coord.X, coord.Y);
        }

        public bool TryGetCell(int x, int y, out CombatGridCell? cell)
        {
            if (!InBounds(x, y))
            {
                cell = null;
                return false;
            }

            cell = _cells[(y * Width) + x];
            return true;
        }

        public void SetBlocked(int x, int y, bool blocksLos = true)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            CombatGridCell cell = _cells[(y * Width) + x];
            cell.IsBlocked = true;
            cell.BlocksLos = blocksLos;
        }

        public void SetCover(int x, int y, int moveCost = 2, bool blocksLos = false)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            CombatGridCell cell = _cells[(y * Width) + x];
            cell.ProvidesCover = true;
            cell.BlocksLos = blocksLos;
            cell.MoveCost = moveCost < 1 ? 1 : moveCost;
        }

        public void SetMoveCost(int x, int y, int moveCost)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            _cells[(y * Width) + x].MoveCost = moveCost < 1 ? 1 : moveCost;
        }

        public void SetElevation(int x, int y, short elevationTier)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            _cells[(y * Width) + x].ElevationTier = elevationTier;
        }

        public short GetElevation(int x, int y)
        {
            return InBounds(x, y) ? _cells[(y * Width) + x].ElevationTier : (short)0;
        }

        public short GetElevation(GridCoord coord)
        {
            return GetElevation(coord.X, coord.Y);
        }

        public void ApplyElevationTiers(IReadOnlyList<short> rowMajorTiers)
        {
            if (rowMajorTiers == null)
            {
                throw new ArgumentNullException(nameof(rowMajorTiers));
            }

            int count = Math.Min(_cells.Length, rowMajorTiers.Count);
            for (int i = 0; i < count; i++)
            {
                _cells[i].ElevationTier = rowMajorTiers[i];
            }
        }

        public void ApplyElevationTiers(short[,] tiersYx)
        {
            if (tiersYx == null)
            {
                throw new ArgumentNullException(nameof(tiersYx));
            }

            int rows = Math.Min(Height, tiersYx.GetLength(0));
            int cols = Math.Min(Width, tiersYx.GetLength(1));
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    _cells[(y * Width) + x].ElevationTier = tiersYx[y, x];
                }
            }
        }

        public void ApplyElevationTiers(int[,] tiersYx)
        {
            if (tiersYx == null)
            {
                throw new ArgumentNullException(nameof(tiersYx));
            }

            int rows = Math.Min(Height, tiersYx.GetLength(0));
            int cols = Math.Min(Width, tiersYx.GetLength(1));
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < cols; x++)
                {
                    _cells[(y * Width) + x].ElevationTier = (short)tiersYx[y, x];
                }
            }
        }

        public void SetSurface(int x, int y, SurfaceType surface, int roundsRemaining = -1)
        {
            if (!InBounds(x, y))
            {
                return;
            }

            CombatGridCell cell = _cells[(y * Width) + x];
            cell.Surface = surface;
            cell.SurfaceRoundsRemaining = surface == SurfaceType.None ? 0 : roundsRemaining;
            if (roundsRemaining < 0 && (surface == SurfaceType.Water || surface == SurfaceType.Oil))
            {
                cell.BaseSurface = surface;
            }
            else if (surface == SurfaceType.None)
            {
                cell.BaseSurface = SurfaceType.None;
            }
        }

        public SurfaceType GetSurface(int x, int y)
        {
            return InBounds(x, y) ? _cells[(y * Width) + x].Surface : SurfaceType.None;
        }

        public SurfaceType GetSurface(GridCoord coord)
        {
            return GetSurface(coord.X, coord.Y);
        }

        public int GetSurfaceRoundsRemaining(int x, int y)
        {
            return InBounds(x, y) ? _cells[(y * Width) + x].SurfaceRoundsRemaining : 0;
        }

        public IReadOnlyList<CombatGridCell> GetActiveSurfaceCells()
        {
            List<CombatGridCell> list = new List<CombatGridCell>();
            for (int i = 0; i < _cells.Length; i++)
            {
                if (_cells[i].Surface != SurfaceType.None)
                {
                    list.Add(_cells[i]);
                }
            }

            return list;
        }

        public bool IsWalkable(int x, int y)
        {
            return InBounds(x, y) && !_cells[(y * Width) + x].IsBlocked;
        }

        public bool BlocksLos(int x, int y)
        {
            return !InBounds(x, y) || _cells[(y * Width) + x].BlocksLos;
        }

        public bool ProvidesCover(int x, int y)
        {
            return InBounds(x, y) && _cells[(y * Width) + x].ProvidesCover;
        }

        public int GetMoveCost(int x, int y)
        {
            return InBounds(x, y) ? _cells[(y * Width) + x].MoveCost : int.MaxValue;
        }
    }

    public sealed class CombatantState
    {
        private readonly List<int> _spellIds;
        private readonly int[] _saves;
        private readonly bool[] _spellImmunities;
        private readonly int[] _lootItemIds;

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
            int portraitId = 0,
            int brawn = 10,
            IReadOnlyList<int>? lootItemIds = null,
            bool? prefersRanged = null,
            bool weaponBonusIncludedInToHit = false)
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

            if (lootItemIds != null && lootItemIds.Count > 0)
            {
                _lootItemIds = new int[lootItemIds.Count];
                for (int i = 0; i < lootItemIds.Count; i++)
                {
                    _lootItemIds[i] = lootItemIds[i];
                }
            }
            else
            {
                _lootItemIds = Array.Empty<int>();
            }

            IsEvil = isEvil;
            IsUndead = isUndead;
            XpReward = xpReward;
            GoldReward = goldReward;
            IconId = iconId;
            PortraitId = portraitId;
            Brawn = brawn;
            PrefersRanged = prefersRanged ?? (RangedRange > 1 && !string.IsNullOrWhiteSpace(RangedDice));
            WeaponBonusIncludedInToHit = weaponBonusIncludedInToHit;
            Conditions = new ConditionTracker();
            Economy = new CombatActionEconomy(MovementMax);
        }

        public static CombatantState FromHero(
            PartyMemberState hero,
            GridCoord position,
            IReadOnlyDictionary<int, ItemDefinition>? catalog = null,
            bool isEncumbered = false)
        {
            if (hero == null)
            {
                throw new ArgumentNullException(nameof(hero));
            }

            int penaltyAc = hero.ExhaustionStacks + hero.CurseStacks;
            int penaltyToHit = penaltyAc * 2;
            int effectiveAc = hero.GetEffectiveAc(catalog) + penaltyAc;
            int effectiveMeleeToHit = hero.GetEffectiveMeleeToHit(catalog) + penaltyToHit;
            int effectiveRangedToHit = hero.GetEffectiveRangedToHit(catalog) + penaltyToHit;
            string weaponDice = hero.GetEffectiveDamageDice(catalog);
            int weaponBonus = hero.GetEffectiveWeaponBonus(catalog);
            int rangedRange = hero.GetEffectiveRangedRange(catalog);
            string rangedDice = hero.GetEffectiveRangedDice(catalog);
            int moveMax = hero.GetEffectiveMovementMax(catalog, isEncumbered);
            int attacks = Math.Clamp((int)Math.Floor(hero.AttacksPerRound), 1, 5);
            int damageBonus = hero.GetMeleeDamageAttributeBonus();
            bool prefersRanged = rangedRange > 1 && !string.IsNullOrWhiteSpace(rangedDice);

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
                toHit: effectiveMeleeToHit,
                dodge: hero.Dodge,
                missileSkill: effectiveRangedToHit,
                movementMax: moveMax,
                dexterity: hero.Agility,
                numAttacks: attacks,
                weaponDice: weaponDice,
                weaponBonus: weaponBonus,
                damageBonus: damageBonus,
                rangedDice: rangedDice,
                rangedRange: rangedRange,
                spellIds: hero.KnownSpellIds,
                magicResistance: hero.GetMagicResistancePercent(),
                saves: null,
                spellImmunities: null,
                isEvil: false,
                isUndead: false,
                xpReward: 0,
                goldReward: 0,
                iconId: hero.IconId,
                portraitId: hero.PortraitId,
                brawn: hero.Brawn,
                lootItemIds: null,
                prefersRanged: prefersRanged,
                weaponBonusIncludedInToHit: true);
            state.Knowledge = hero.Knowledge;
            state.Judgment = hero.Judgment;
            state.Vitality = hero.Vitality;
            state.Luck = hero.Luck;
            state.ExhaustionStacks = hero.ExhaustionStacks;
            state.CurseStacks = hero.CurseStacks;

            if (hero.Conditions.Has(CharConditionId.Poisoned)) state.Conditions.Apply(CharConditionId.Poisoned);
            if (hero.Conditions.Has(CharConditionId.Diseased)) state.Conditions.Apply(CharConditionId.Diseased);
            if (hero.Conditions.Has(CharConditionId.Cursed)) state.Conditions.Apply(CharConditionId.Cursed);
            if (hero.Conditions.Has(CharConditionId.FireProtection))
            {
                state.Conditions.Apply(CharConditionId.FireProtection, magnitude: 1, turnsRemaining: 10, baseRecoveryChancePercent: 0, canRecoverBySave: false);
            }

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

            int brawn = Math.Max(8, 10 + (Math.Max(1, monster.HitDice) - 1) * 2 + monster.DamageBonus);
            bool prefersRanged = monster.MissileChancePct >= 50 ||
                                 monster.CastChancePct >= 50 ||
                                 (monster.RangedRange > 1 && !string.IsNullOrWhiteSpace(monster.RangedDice));

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
                portraitId: 0,
                brawn: brawn,
                lootItemIds: monster.LootItemIds,
                prefersRanged: prefersRanged,
                weaponBonusIncludedInToHit: false);
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
        public int Brawn { get; set; }
        public int Knowledge { get; set; } = 10;
        public int Judgment { get; set; } = 10;
        public int Vitality { get; set; } = 10;
        public int Luck { get; set; } = 10;
        public int CritBonusPct => Side == CombatSide.Hero ? Math.Max(0, (Luck - 10) / 2) : 0;
        public int ArcaneSpellPowerBonus => Side == CombatSide.Hero ? Math.Max(0, (Knowledge - 10) / 4) : 0;
        public int DivineHealingBonus => Side == CombatSide.Hero ? Math.Max(0, (Judgment - 10) / 4) : 0;
        public int SpellHitDcBonusPct => Side == CombatSide.Hero ? Knowledge - 10 : 0;
        public int ConditionSaveBonusPct => Side == CombatSide.Hero ? Math.Max(0, (Vitality - 10) * 2) + Math.Max(0, Luck - 10) : 0;
        public int CombatKills { get; internal set; }
        public int CombatDamageDealt { get; internal set; }
        public int CombatHealingDone { get; internal set; }
        public bool PrefersRanged { get; set; }
        public bool WeaponBonusIncludedInToHit { get; }
        public IReadOnlyList<int> SpellIds => _spellIds;
        public int MagicResistance { get; set; }
        public IReadOnlyList<int> Saves => _saves;
        public IReadOnlyList<bool> SpellImmunities => _spellImmunities;
        public IReadOnlyList<int> LootItemIds => _lootItemIds;
        public bool IsEvil { get; }
        public bool IsUndead { get; }
        public ConditionTracker Conditions { get; }
        public CombatActionEconomy Economy { get; }
        public int ExhaustionStacks { get; set; }
        public int CurseStacks { get; set; }
        public bool IsGuarding { get; set; }
        public bool IsProne { get; set; }
        public bool PronePendingMovePenalty { get; set; }
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

        public int MeleeToHit => EffectiveToHit;

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

        public int RangedToHit => EffectiveMissileSkill;

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

    public sealed class HeroVictorySummary
    {
        public HeroVictorySummary(
            string heroId,
            string name,
            string archetype,
            int iconId,
            int portraitId,
            int levelBefore,
            int levelAfter,
            int xpBefore,
            int xpAfter,
            int xpAwarded,
            int currentLevelFloorXp,
            int nextLevelXp,
            bool readyToLevelUp,
            int pendingLevelUps,
            bool wasFallenRevived,
            int kills,
            int damageDealt,
            int healingDone,
            bool isMvp)
        {
            HeroId = heroId ?? string.Empty;
            Name = name ?? string.Empty;
            Archetype = archetype ?? string.Empty;
            IconId = iconId;
            PortraitId = portraitId;
            LevelBefore = levelBefore;
            LevelAfter = levelAfter;
            XpBefore = xpBefore;
            XpAfter = xpAfter;
            XpAwarded = xpAwarded;
            CurrentLevelFloorXp = currentLevelFloorXp;
            NextLevelXp = nextLevelXp;
            ReadyToLevelUp = readyToLevelUp;
            PendingLevelUps = pendingLevelUps;
            WasFallenRevived = wasFallenRevived;
            Kills = kills;
            DamageDealt = damageDealt;
            HealingDone = healingDone;
            IsMvp = isMvp;
        }

        public string HeroId { get; }
        public string Name { get; }
        public string Archetype { get; }
        public int IconId { get; }
        public int PortraitId { get; }
        public int LevelBefore { get; }
        public int LevelAfter { get; }
        public int XpBefore { get; }
        public int XpAfter { get; }
        public int XpAwarded { get; }
        public int CurrentLevelFloorXp { get; }
        public int NextLevelXp { get; }
        public bool ReadyToLevelUp { get; }
        public int PendingLevelUps { get; }
        public bool WasFallenRevived { get; }
        public int Kills { get; }
        public int DamageDealt { get; }
        public int HealingDone { get; }
        public bool IsMvp { get; set; }
    }

    public sealed class LootStackSummary
    {
        public LootStackSummary(
            int itemId,
            string name,
            string category,
            string slot,
            int quantity,
            int remainingCharges,
            int basePrice,
            bool isJunk)
        {
            ItemId = itemId;
            Name = name ?? string.Empty;
            Category = category ?? string.Empty;
            Slot = slot ?? string.Empty;
            Quantity = quantity;
            RemainingCharges = remainingCharges;
            BasePrice = basePrice;
            IsJunk = isJunk;
        }

        public int ItemId { get; }
        public string Name { get; }
        public string Category { get; }
        public string Slot { get; }
        public int Quantity { get; }
        public int RemainingCharges { get; }
        public int BasePrice { get; }
        public bool IsJunk { get; }
    }

    public sealed class CombatVictorySummary
    {
        public CombatVictorySummary(
            int totalXpEarned,
            int baseGoldLoot,
            int luckBonusGold,
            int totalGoldLoot,
            int partyGoldAfter,
            int roundsTaken,
            IReadOnlyList<HeroVictorySummary> heroes,
            IReadOnlyList<LootStackSummary> lootStacks)
        {
            TotalXpEarned = totalXpEarned;
            BaseGoldLoot = baseGoldLoot;
            LuckBonusGold = luckBonusGold;
            TotalGoldLoot = totalGoldLoot;
            PartyGoldAfter = partyGoldAfter;
            RoundsTaken = roundsTaken;
            Heroes = heroes ?? Array.Empty<HeroVictorySummary>();
            LootStacks = lootStacks ?? Array.Empty<LootStackSummary>();
        }

        public int TotalXpEarned { get; }
        public int BaseGoldLoot { get; }
        public int LuckBonusGold { get; }
        public int TotalGoldLoot { get; }
        public int PartyGoldAfter { get; }
        public int RoundsTaken { get; }
        public IReadOnlyList<HeroVictorySummary> Heroes { get; }
        public IReadOnlyList<LootStackSummary> LootStacks { get; }

        public int ReadyToLevelUpCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < Heroes.Count; i++)
                {
                    if (Heroes[i].ReadyToLevelUp)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        public bool AnyReadyToLevelUp => ReadyToLevelUpCount > 0;
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
        public LosResult(
            LosKind kind,
            int coverPenaltyPct,
            GridCoord? blockedBy,
            IReadOnlyList<GridCoord> rayCells,
            bool isSteamObscured = false)
        {
            Kind = kind;
            CoverPenaltyPct = coverPenaltyPct;
            BlockedBy = blockedBy;
            RayCells = rayCells ?? Array.Empty<GridCoord>();
            IsSteamObscured = isSteamObscured;
        }

        public LosKind Kind { get; }
        public int CoverPenaltyPct { get; }
        public GridCoord? BlockedBy { get; }
        public IReadOnlyList<GridCoord> RayCells { get; }
        public bool IsSteamObscured { get; }
    }

    public sealed class SpellAoeFootprint
    {
        public SpellAoeFootprint(
            bool inRange,
            GridCoord center,
            IReadOnlyList<GridCoord> affectedTiles,
            IReadOnlyList<string> affectedAllyIds,
            IReadOnlyList<string> affectedEnemyIds,
            IReadOnlyList<GridCoord> blockedTiles,
            bool hasLineOfSight = true)
        {
            InRange = inRange;
            Center = center;
            AffectedTiles = affectedTiles ?? Array.Empty<GridCoord>();
            AffectedAllyIds = affectedAllyIds ?? Array.Empty<string>();
            AffectedEnemyIds = affectedEnemyIds ?? Array.Empty<string>();
            BlockedTiles = blockedTiles ?? Array.Empty<GridCoord>();
            HasLineOfSight = hasLineOfSight;
        }

        public bool InRange { get; }
        public GridCoord Center { get; }
        public IReadOnlyList<GridCoord> AffectedTiles { get; }
        public IReadOnlyList<string> AffectedAllyIds { get; }
        public IReadOnlyList<string> AffectedEnemyIds { get; }
        public IReadOnlyList<GridCoord> BlockedTiles { get; }
        public bool HasLineOfSight { get; }
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
            IReadOnlyList<string> conditionTags,
            int elevationModifierPct = 0)
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
            ElevationModifierPct = elevationModifierPct;
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
        public int ElevationModifierPct { get; }
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

    public sealed class TacticalShovePreview
    {
        public TacticalShovePreview(
            bool canExecute,
            string reason,
            int successChancePct,
            GridCoord pushDirection,
            GridCoord destination,
            bool hitsWallOrBounds,
            string? collidingUnitId,
            bool pushesOffLedge,
            int fallElevationDrop,
            SurfaceType destinationSurface,
            bool isAllyShove)
        {
            CanExecute = canExecute;
            Reason = reason ?? string.Empty;
            SuccessChancePct = successChancePct;
            PushDirection = pushDirection;
            Destination = destination;
            HitsWallOrBounds = hitsWallOrBounds;
            CollidingUnitId = collidingUnitId;
            PushesOffLedge = pushesOffLedge;
            FallElevationDrop = fallElevationDrop;
            DestinationSurface = destinationSurface;
            IsAllyShove = isAllyShove;
        }

        public bool CanExecute { get; }
        public string Reason { get; }
        public int SuccessChancePct { get; }
        public GridCoord PushDirection { get; }
        public GridCoord Destination { get; }
        public bool HitsWallOrBounds { get; }
        public string? CollidingUnitId { get; }
        public bool PushesOffLedge { get; }
        public int FallElevationDrop { get; }
        public SurfaceType DestinationSurface { get; }
        public bool IsAllyShove { get; }
    }

    public sealed class EnemyIntentPrediction
    {
        public EnemyIntentPrediction(
            string actorId,
            GridCoord targetCoord,
            string actionType,
            CombatIntentActionType actionKind,
            string targetId,
            GridCoord plannedMoveCoord,
            bool hasPlannedMove,
            string spellKey = "")
        {
            ActorId = actorId ?? string.Empty;
            TargetCoord = targetCoord;
            ActionType = actionType ?? "Hold";
            ActionKind = actionKind;
            TargetId = targetId ?? string.Empty;
            PlannedMoveCoord = plannedMoveCoord;
            HasPlannedMove = hasPlannedMove;
            SpellKey = spellKey ?? string.Empty;
        }

        public string ActorId { get; }
        public GridCoord TargetCoord { get; }
        public string ActionType { get; }
        public CombatIntentActionType ActionKind { get; }
        public string TargetId { get; }
        public GridCoord PlannedMoveCoord { get; }
        public bool HasPlannedMove { get; }
        public string SpellKey { get; }

        public void Deconstruct(out GridCoord targetCoord, out string actionType, out string targetId)
        {
            targetCoord = TargetCoord;
            actionType = ActionType;
            targetId = TargetId;
        }
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
