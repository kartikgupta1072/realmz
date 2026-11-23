using System;
using System.Collections.Generic;
using System.Globalization;

namespace RealmzCore.Rules
{
    public interface IDiceRoller
    {
        int NextInt(int minInclusive, int maxExclusive);
    }

    public readonly struct DiceSpec : IEquatable<DiceSpec>
    {
        public DiceSpec(int count, int sides, int modifier = 0)
        {
            if (count < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(count));
            }

            if (count > 0 && sides <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sides));
            }

            if (count == 0 && sides != 0)
            {
                throw new ArgumentOutOfRangeException(nameof(sides));
            }

            Count = count;
            Sides = sides;
            Modifier = modifier;
        }

        public int Count { get; }
        public int Sides { get; }
        public int Modifier { get; }

        public int Min => Count == 0 ? Modifier : Count + Modifier;
        public int Max => Count == 0 ? Modifier : (Count * Sides) + Modifier;
        public double ExpectedAverage => Count == 0 ? Modifier : (Count * (Sides + 1) / 2.0) + Modifier;

        public int Roll(IDiceRoller roller)
        {
            if (roller == null)
            {
                throw new ArgumentNullException(nameof(roller));
            }

            int total = Modifier;
            for (int i = 0; i < Count; i++)
            {
                total += roller.NextInt(1, Sides + 1);
            }

            return total;
        }

        public static DiceSpec Parse(string expression)
        {
            if (!TryParse(expression, out DiceSpec spec))
            {
                throw new FormatException($"Invalid dice expression '{expression}'.");
            }

            return spec;
        }

        public static bool TryParse(string? expression, out DiceSpec spec)
        {
            spec = default;
            if (string.IsNullOrWhiteSpace(expression))
            {
                return false;
            }

            string compact = StripWhitespace(expression!);
            if (compact.Length == 0)
            {
                return false;
            }

            int dIndex = IndexOfD(compact);
            if (dIndex < 0)
            {
                if (int.TryParse(compact, NumberStyles.Integer, CultureInfo.InvariantCulture, out int flatValue))
                {
                    spec = new DiceSpec(0, 0, flatValue);
                    return true;
                }

                return false;
            }

            // Ensure there is only a single 'd'/'D'.
            if (IndexOfD(compact, dIndex + 1) >= 0)
            {
                return false;
            }

            int count;
            if (dIndex == 0)
            {
                count = 1;
            }
            else
            {
                string countPart = compact.Substring(0, dIndex);
                if (!int.TryParse(countPart, NumberStyles.None, CultureInfo.InvariantCulture, out count) || count <= 0)
                {
                    return false;
                }
            }

            int afterD = dIndex + 1;
            if (afterD >= compact.Length)
            {
                return false;
            }

            int modSignIndex = -1;
            for (int i = afterD; i < compact.Length; i++)
            {
                char c = compact[i];
                if (c == '+' || c == '-')
                {
                    modSignIndex = i;
                    break;
                }
            }

            string sidesPart = modSignIndex < 0
                ? compact.Substring(afterD)
                : compact.Substring(afterD, modSignIndex - afterD);

            if (!int.TryParse(sidesPart, NumberStyles.None, CultureInfo.InvariantCulture, out int sides) || sides <= 0)
            {
                return false;
            }

            int modifier = 0;
            if (modSignIndex >= 0)
            {
                string modPart = compact.Substring(modSignIndex);
                if (modPart.Length < 2)
                {
                    return false;
                }

                // Disallow chained signs like "2d6+1+2".
                for (int i = 1; i < modPart.Length; i++)
                {
                    if (!char.IsDigit(modPart[i]))
                    {
                        return false;
                    }
                }

                if (!int.TryParse(modPart, NumberStyles.Integer, CultureInfo.InvariantCulture, out modifier))
                {
                    return false;
                }
            }

            spec = new DiceSpec(count, sides, modifier);
            return true;
        }

        public override string ToString()
        {
            if (Count == 0)
            {
                return Modifier.ToString(CultureInfo.InvariantCulture);
            }

            if (Modifier == 0)
            {
                return $"{Count}d{Sides}";
            }

            string sign = Modifier > 0 ? "+" : string.Empty;
            return $"{Count}d{Sides}{sign}{Modifier}";
        }

        public bool Equals(DiceSpec other)
        {
            return Count == other.Count && Sides == other.Sides && Modifier == other.Modifier;
        }

        public override bool Equals(object? obj)
        {
            return obj is DiceSpec other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = Count;
                hash = (hash * 397) ^ Sides;
                hash = (hash * 397) ^ Modifier;
                return hash;
            }
        }

        private static int IndexOfD(string text, int startIndex = 0)
        {
            for (int i = startIndex; i < text.Length; i++)
            {
                char c = text[i];
                if (c == 'd' || c == 'D')
                {
                    return i;
                }
            }

            return -1;
        }

        private static string StripWhitespace(string text)
        {
            char[] buffer = new char[text.Length];
            int count = 0;
            for (int i = 0; i < text.Length; i++)
            {
                char c = text[i];
                if (!char.IsWhiteSpace(c))
                {
                    buffer[count++] = c;
                }
            }

            return new string(buffer, 0, count);
        }
    }

    /// <summary>
    /// Deterministic SplitMix64 PRNG for reproducible combat, loot, and AI simulation.
    /// </summary>
    public sealed class DeterministicDiceRoller : IDiceRoller
    {
        private ulong _state;

        public DeterministicDiceRoller(ulong seed)
        {
            _state = seed;
        }

        public ulong NextUInt64()
        {
            _state += 0x9E3779B97F4A7C15UL;
            ulong z = _state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxExclusive),
                    $"maxExclusive ({maxExclusive}) must be greater than minInclusive ({minInclusive}).");
            }

            ulong span = (ulong)((long)maxExclusive - minInclusive);
            ulong threshold = (0UL - span) % span;
            while (true)
            {
                ulong r = NextUInt64();
                if (r >= threshold)
                {
                    return (int)((long)minInclusive + (long)(r % span));
                }
            }
        }
    }

    /// <summary>
    /// Explicit roll queue for deterministic unit tests.
    /// </summary>
    public sealed class QueuedDiceRoller : IDiceRoller
    {
        private readonly Queue<int> _rolls = new Queue<int>();

        public QueuedDiceRoller(params int[] rolls)
        {
            Enqueue(rolls);
        }

        public QueuedDiceRoller(IEnumerable<int> rolls)
        {
            if (rolls == null)
            {
                throw new ArgumentNullException(nameof(rolls));
            }

            foreach (int roll in rolls)
            {
                _rolls.Enqueue(roll);
            }
        }

        public int RemainingCount => _rolls.Count;

        public void Enqueue(params int[] rolls)
        {
            if (rolls == null)
            {
                throw new ArgumentNullException(nameof(rolls));
            }

            for (int i = 0; i < rolls.Length; i++)
            {
                _rolls.Enqueue(rolls[i]);
            }
        }

        public int NextInt(int minInclusive, int maxExclusive)
        {
            if (minInclusive >= maxExclusive)
            {
                throw new ArgumentOutOfRangeException(nameof(maxExclusive));
            }

            if (_rolls.Count == 0)
            {
                throw new InvalidOperationException("QueuedDiceRoller has no remaining scripted rolls.");
            }

            int next = _rolls.Dequeue();
            if (next < minInclusive || next >= maxExclusive)
            {
                throw new InvalidOperationException(
                    $"Queued roll {next} is outside requested range [{minInclusive}, {maxExclusive}).");
            }

            return next;
        }
    }
}
