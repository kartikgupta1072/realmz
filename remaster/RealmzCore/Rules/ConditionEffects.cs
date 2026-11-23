using System;
using System.Collections.Generic;

namespace RealmzCore.Rules
{
    public sealed class ConditionDisplayEffect
    {
        public ConditionDisplayEffect(
            CharConditionId condition,
            string code,
            string displayName,
            string description,
            string statSummary,
            int magnitude,
            int turnsRemaining,
            bool isBeneficial)
        {
            Condition = condition;
            Code = code ?? string.Empty;
            DisplayName = displayName ?? string.Empty;
            Description = description ?? string.Empty;
            StatSummary = statSummary ?? string.Empty;
            Magnitude = magnitude;
            TurnsRemaining = turnsRemaining;
            IsBeneficial = isBeneficial;
        }

        public CharConditionId Condition { get; }
        public string Code { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string StatSummary { get; }
        public int Magnitude { get; }
        public int TurnsRemaining { get; }
        public bool IsBeneficial { get; }
    }

    public readonly struct ConditionDescriptor
    {
        public ConditionDescriptor(
            string code,
            string displayName,
            string description,
            string statSummary,
            bool isBeneficial,
            int defaultEscalationStepPercent)
        {
            Code = code;
            DisplayName = displayName;
            Description = description;
            StatSummary = statSummary;
            IsBeneficial = isBeneficial;
            DefaultEscalationStepPercent = defaultEscalationStepPercent;
        }

        public string Code { get; }
        public string DisplayName { get; }
        public string Description { get; }
        public string StatSummary { get; }
        public bool IsBeneficial { get; }
        public int DefaultEscalationStepPercent { get; }
    }

    public static class ConditionEffects
    {
        private static readonly Dictionary<CharConditionId, ConditionDescriptor> Descriptors =
            new Dictionary<CharConditionId, ConditionDescriptor>
            {
                [CharConditionId.RunsAway] = new ConditionDescriptor(
                    "COND_RUNS_AWAY", "Panicked", "Flees from combat and cannot threaten attacks of opportunity.", "Cannot take AoO; flees enemies", false, 10),
                [CharConditionId.Helpless] = new ConditionDescriptor(
                    "COND_HELPLESS", "Helpless", "Incapacitated (held, asleep, or stunned); cannot act and attackers hit automatically.", "No actions; melee hits auto-land", false, 15),
                [CharConditionId.Tangled] = new ConditionDescriptor(
                    "COND_TANGLED", "Tangled", "Restrained by webs or vines; movement is severely impaired.", "Movement halved; -10% Dodge", false, 15),
                [CharConditionId.Cursed] = new ConditionDescriptor(
                    "COND_CURSED", "Cursed", "Afflicted by ill fortune that weakens accuracy and defenses.", "-5% To-Hit; attackers gain +5% To-Hit", false, 10),
                [CharConditionId.MagicAura] = new ConditionDescriptor(
                    "COND_MAGIC_AURA", "Magic Aura", "Surrounded by a protective shimmer that aids strikes and deflection.", "+5% To-Hit; attackers suffer -5% To-Hit", true, 10),
                [CharConditionId.Stupid] = new ConditionDescriptor(
                    "COND_STUPID", "Dazed", "Mental faculties dulled, reducing spellcasting effectiveness.", "-3 Knowledge/Judgment", false, 10),
                [CharConditionId.Slow] = new ConditionDescriptor(
                    "COND_SLOW", "Slowed", "Actions and reflexes are sluggish.", "-4 Dexterity; -4 Move; -15% To-Hit; +15% hit against", false, 15),
                [CharConditionId.ShieldFromHits] = new ConditionDescriptor(
                    "COND_SHIELD_HITS", "Shielded", "Magical ward deflects incoming melee blows.", "+5 AC vs melee attacks", true, 10),
                [CharConditionId.ShieldFromProjectiles] = new ConditionDescriptor(
                    "COND_SHIELD_PROJECTILES", "Missile Ward", "Deflects incoming arrows and thrown projectiles.", "-15% enemy ranged To-Hit", true, 10),
                [CharConditionId.Poisoned] = new ConditionDescriptor(
                    "COND_POISONED", "Poisoned", "Venom courses through the veins, draining vitality each turn.", "Takes periodic poison damage each round", false, 10),
                [CharConditionId.Regenerating] = new ConditionDescriptor(
                    "COND_REGENERATING", "Regenerating", "Wounds knit shut over time.", "Restores HP each round", true, 10),
                [CharConditionId.Strong] = new ConditionDescriptor(
                    "COND_STRONG", "Empowered", "Surging physical strength increases hit rate and melee damage.", "+15% To-Hit; +3 melee damage", true, 10),
                [CharConditionId.ProtectionFromEvil] = new ConditionDescriptor(
                    "COND_PROT_EVIL", "Protection from Evil", "Warded against evil foes, improving strikes and resilience.", "+10% To-Hit vs evil creatures", true, 10),
                [CharConditionId.Speedy] = new ConditionDescriptor(
                    "COND_SPEEDY", "Hasted", "Supernatural swiftness speeds movement and initiative.", "+4 Dexterity; +4 Movement", true, 10),
                [CharConditionId.Invisible] = new ConditionDescriptor(
                    "COND_INVISIBLE", "Invisible", "Hidden from sight; avoids attacks of opportunity and evades strikes.", "-10% enemy To-Hit; immune to AoO", true, 10),
                [CharConditionId.TurnedToStone] = new ConditionDescriptor(
                    "COND_PETRIFIED", "Petrified", "Turned to solid stone; completely unable to act.", "Incapacitated until cured", false, 10),
                [CharConditionId.Blind] = new ConditionDescriptor(
                    "COND_BLIND", "Blinded", "Cannot see targets clearly.", "-15% To-Hit; attackers gain +15% To-Hit", false, 10),
                [CharConditionId.Diseased] = new ConditionDescriptor(
                    "COND_DISEASED", "Diseased", "Weakened by lingering contagion.", "Gradual stamina penalty", false, 10),
                [CharConditionId.Confused] = new ConditionDescriptor(
                    "COND_CONFUSED", "Confused", "Disoriented and erratic in battle.", "-10% To-Hit; attackers gain +10% To-Hit", false, 15),
                [CharConditionId.AttackBonus] = new ConditionDescriptor(
                    "COND_ATTACK_BONUS", "Battle Fervor", "Guided strikes land more reliably.", "+10% To-Hit", true, 10),
                [CharConditionId.HinderedAttacks] = new ConditionDescriptor(
                    "COND_HINDERED_ATTACK", "Hindered Attacks", "Weapons feel heavy and awkward.", "-10% To-Hit", false, 10),
                [CharConditionId.HinderedDefense] = new ConditionDescriptor(
                    "COND_HINDERED_DEFENSE", "Exposed", "Stance is compromised against incoming blows.", "-5 AC", false, 10),
                [CharConditionId.DefenseBonus] = new ConditionDescriptor(
                    "COND_DEFENSE_BONUS", "Fortified", "Defensive posture deflects blows.", "+5 AC", true, 10),
                [CharConditionId.Silenced] = new ConditionDescriptor(
                    "COND_SILENCED", "Silenced", "Cannot utter verbal components for spells.", "Cannot cast spells", false, 10)
            };

        public static ConditionDescriptor GetDescriptor(CharConditionId condition)
        {
            if (Descriptors.TryGetValue(condition, out ConditionDescriptor descriptor))
            {
                return descriptor;
            }

            string rawName = condition.ToString();
            string code = "COND_" + ToUpperSnake(rawName);
            bool beneficial = condition >= CharConditionId.FireProtection &&
                              condition <= CharConditionId.FifthLevelSpellProtection;
            return new ConditionDescriptor(
                code: code,
                displayName: rawName,
                description: $"{rawName} condition active.",
                statSummary: rawName,
                isBeneficial: beneficial,
                defaultEscalationStepPercent: 10);
        }

        public static string GetCode(CharConditionId condition)
        {
            return GetDescriptor(condition).Code;
        }

        public static string GetStatSummary(CharConditionId condition)
        {
            return GetDescriptor(condition).StatSummary;
        }

        public static bool IsCrowdControl(CharConditionId condition)
        {
            return condition == CharConditionId.Helpless ||
                   condition == CharConditionId.Confused ||
                   condition == CharConditionId.Slow ||
                   condition == CharConditionId.Tangled;
        }

        private static string ToUpperSnake(string pascal)
        {
            if (string.IsNullOrEmpty(pascal))
            {
                return "UNKNOWN";
            }

            System.Text.StringBuilder sb = new System.Text.StringBuilder(pascal.Length + 6);
            for (int i = 0; i < pascal.Length; i++)
            {
                char c = pascal[i];
                if (i > 0 && char.IsUpper(c))
                {
                    sb.Append('_');
                }

                sb.Append(char.ToUpperInvariant(c));
            }

            return sb.ToString();
        }
    }
}
