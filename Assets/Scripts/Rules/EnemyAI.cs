using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules.Data;

namespace HeroHQ.Rules
{
    /// <summary>
    /// The computer's choice (02 GDD 9, 03 Architecture 4.8). For whichever side is acting: "enemies" means the
    /// other side, so the same code plays the enemy crew and, for the fight report, your own target teams.
    ///
    /// On a turn the first rule that applies wins: 1. the Ultimate if it is charged and would do something,
    /// 2. the Special if it is ready and would do something, 3. the Basic. Targets (only among the heroes
    /// <see cref="Fight.Options"/> lets it pick: Taunt, then the front row) follow 02 GDD 9's table, and where one
    /// skill fits two rows the most specific wins (reading R1, OWNER 2026-11-16): drain the turn meter → the enemy
    /// who acts next; else a bad effect → the lowest-Health enemy without it; else damage → the lowest-Health
    /// enemy; a heal or a buff on an ally → the ally with the lowest Health %. Ties go to the lowest slot.
    ///
    /// No luck, no copy of the fight, no look ahead: it reads the fight and changes nothing.
    /// </summary>
    public static class EnemyAI
    {
        /// <summary>The acting hero's button and target. Throws if nobody is choosing (call <see cref="Fight.AdvanceToChoice"/> first).</summary>
        public static Choice Choose(Fight fight)
        {
            if (fight.Acting == null) throw new InvalidOperationException("Nobody is choosing: call AdvanceToChoice first.");

            var options = fight.Options;                  // Basic, Special, Ultimate
            var option = options.First(o => o.Button == Button.Basic);
            foreach (var button in new[] { Button.Ultimate, Button.Special })
            {
                var candidate = options.First(o => o.Button == button);
                if (candidate.Usable && WouldDoSomething(fight, candidate)) { option = candidate; break; }
            }
            return new Choice(option.Button, option.NeedsChoice ? Pick(fight, option) : null);
        }

        // ---- "would it do something" (02 GDD 9, rules 1 and 2) ----------------------------------------------------

        /// <summary>
        /// A skill would do something if any of its pieces would. The GDD names three that would not: a heal when
        /// every one it could reach is at full Health, a Clear when none of them has a bad effect, and a Stun when
        /// every one it could reach is already stunned. Every other piece counts as useful (a damage piece, a meter
        /// piece, and a buff or debuff, which at least resets the duration): reading R17, 03 Architecture 4.12.
        /// </summary>
        static bool WouldDoSomething(Fight fight, ButtonOption option) =>
            option.Skill.Pieces.Any(piece =>
            {
                var reach = Reach(fight, option, piece);
                switch (piece.Do)
                {
                    case "heal": return reach.Any(h => h.Health < h.MaxHealth);
                    case "clear": return reach.Any(h => h.Effects.Any(e => Effects.IsBad(e.Kind)));
                    case "effect" when piece.Effect == "stun": return reach.Any(h => !Effects.IsStunned(h));
                    default: return true;
                }
            });

        /// <summary>
        /// Who one piece could reach: the heroes the AI may pick for a chosen target, else whoever the piece's
        /// target names (the enemy who acts next, everyone on a side, the actor).
        /// </summary>
        static IReadOnlyList<Fighter> Reach(Fight fight, ButtonOption option, PieceDef piece) =>
            piece.Target == "enemy" || piece.Target == "enemy_and_neighbours" || piece.Target == "ally"
                ? option.Choosable
                : Targeting.Targets(fight, fight.Acting, piece.Target, null);

        // ---- the target table (02 GDD 9) ----------------------------------------------------------------------

        /// <summary>The hero the AI taps for a skill that needs a choice, by what the skill does to that hero (reading R1).</summary>
        static Fighter Pick(Fight fight, ButtonOption option)
        {
            var from = option.Choosable;                  // slot order, Taunt and front row already applied
            var pieces = option.Skill.Pieces.Where(p => p.Target == "enemy" || p.Target == "enemy_and_neighbours" || p.Target == "ally").ToList();

            if (pieces.Any(p => p.Target == "ally"))
                return LowestHealthPercent(from);

            if (pieces.Any(p => p.Do == "meter" && p.Percent < 0))
                return FirstOnTheBar(fight, from);

            var badEffect = pieces.FirstOrDefault(p => p.Do == "effect" && Effects.IsBad(p.Effect));
            if (badEffect != null)
            {
                var without = from.Where(h => !h.Effects.Any(e => e.Kind == badEffect.Effect)).ToList();
                return LowestHealth(without.Count > 0 ? without : from);
            }
            return LowestHealth(from);
        }

        /// <summary>The lowest current Health points; the first in slot order on a tie.</summary>
        static Fighter LowestHealth(IReadOnlyList<Fighter> heroes)
        {
            var best = heroes[0];
            foreach (var h in heroes)
                if (h.Health < best.Health) best = h;
            return best;
        }

        /// <summary>The lowest Health % (compared by cross-multiplying, so nothing is rounded); the first in slot order on a tie.</summary>
        static Fighter LowestHealthPercent(IReadOnlyList<Fighter> heroes)
        {
            var best = heroes[0];
            foreach (var h in heroes)
                if ((long)h.Health * best.MaxHealth < (long)best.Health * h.MaxHealth) best = h;
            return best;
        }

        /// <summary>
        /// The first of <paramref name="heroes"/> on the turn bar: "the enemy who acts next" (a skipped turn counts,
        /// 10.6 q. 10), or the first one the AI may pick when that enemy is out of reach (reading R17). Asks for a
        /// longer bar until one of them shows up, as <see cref="Targeting"/> does.
        /// </summary>
        static Fighter FirstOnTheBar(Fight fight, IReadOnlyList<Fighter> heroes)
        {
            for (var count = Math.Max(1, (int)fight.Data.Tuning["turn_bar_length"]); count <= 4096; count *= 2)
            {
                var first = fight.PredictTurns(count).Select(t => t.Who).FirstOrDefault(heroes.Contains);
                if (first != null) return first;
            }
            throw new InvalidOperationException("Nobody it may pick acts within 4096 turns: the Speeds must be wrong.");
        }
    }
}
