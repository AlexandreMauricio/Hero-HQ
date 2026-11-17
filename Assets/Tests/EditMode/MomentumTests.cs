using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P1-14 (03 Architecture 4.12): Momentum (02 GDD 7.4) and the two triggers that were still missing, "attack hits"
    // and "ally below half" (02 GDD 8). Momentum: +1 stack for each skill that hits at least one enemy, up to
    // momentum_max, +momentum_step % damage per stack, all lost when a skill misses every target, a heal or a
    // self-buff leaves it (10.6 q. 7), a counter counts (q. 3). Halvard's Taunt: once per ally, the other allies'
    // drops only, not for a knocked-out ally, not at exactly half.
    //
    // The set-up is TriggerTests': p0 (Speed 200) acts first, everyone else (Speed 50) after it. Test heroes:
    // Damage 200, Armor 100, Health 1,000,000, Accuracy and Dodge 0, Crit Chance 0, no leader bonus and no
    // triggers unless a test adds them. A Basic that hits for 100 % does 182 with no Momentum (the test tuning:
    // momentum_step 8, momentum_max 5, armor_k 1000), and 196, 211, 225, 240, 255 with 1 to 5 stacks.
    public class MomentumTests
    {
        const string ThordisPassive = "[{ when: 'attack_hits', pieces: [{ do: 'effect', target: 'self', effect: 'momentum' }] }]";
        const string HalvardPassive = "[{ when: 'ally_below_half', pieces: [{ do: 'effect', target: 'self', effect: 'taunt', turns: 1 }] }]";

        static Fight Duel(Action<string, JObject> edit = null, int seed = 1) => NewFight(
            new[] { ("p0", 200), ("p1", 50), ("p2", 50), ("p3", 50), ("p4", 50) },
            new[] { ("e0", 50), ("e1", 50), ("e2", 50), ("e3", 50), ("e4", 50) }, seed,
            edit: (id, h) => { h["stats"]["crit_chance"] = 0; edit?.Invoke(id, h); });

        static void Pieces(JObject hero, string button, params string[] pieces) =>
            hero[button]["pieces"] = JArray.Parse("[" + string.Join(",", pieces) + "]");

        static void PassiveTriggers(JObject hero, string triggers) => hero["passive"]["triggers"] = JArray.Parse(triggers);

        static List<FightEvent> Of(IEnumerable<FightEvent> events, FightEventKind kind, Fighter who = null) =>
            events.Where(e => e.Kind == kind && (who == null || e.Who == who)).ToList();

        static int Stacks(Fighter hero) => hero.Effects.FirstOrDefault(e => e.Kind == "momentum")?.Amount ?? 0;

        /// <summary>Plays turns (everyone else uses Basic) until <paramref name="hero"/> must choose; every event goes into <paramref name="log"/>.</summary>
        static void Until(Fight fight, Fighter hero, List<FightEvent> log)
        {
            for (var guard = 0; guard < 100; guard++)
            {
                log.AddRange(fight.AdvanceToChoice());
                if (fight.Acting == hero) return;
                log.AddRange(fight.Act(Basic(fight)));
            }
            Assert.Fail($"{hero} never got a turn");
        }

        static void GiveMomentum(Fighter hero, int stacks)
        {
            for (var i = 0; i < stacks; i++) Effects.Apply(hero, "momentum", 0, 5);
        }

        // ---- Momentum ----

        [Test]
        public void Momentum_GrowsOneStackPerHittingSkill_UpToTheMost_AndRaisesDamage()
        {
            var fight = Duel((id, h) => { if (id == "p0") PassiveTriggers(h, ThordisPassive); });
            var p0 = fight.Players[0];
            var log = new List<FightEvent>();
            var damage = new List<int>();
            var stacks = new List<int>();

            for (var i = 0; i < 7; i++)
            {
                Until(fight, p0, log);
                var events = fight.Act(new Choice(Button.Basic, fight.Enemies[0]));
                damage.Add(Of(events, FightEventKind.Damaged).Single().Amount);
                stacks.Add(Stacks(p0));
                // the stack is added after the skill's own hit (it doesn't raise the hit that earns it)
                Assert.That(events.Select(e => e.Kind).ToList().IndexOf(FightEventKind.EffectApplied),
                            Is.GreaterThan(events.Select(e => e.Kind).ToList().IndexOf(FightEventKind.Damaged)));
            }

            Assert.That(damage, Is.EqualTo(new[] { 182, 196, 211, 225, 240, 255, 255 }));
            Assert.That(stacks, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 5, 5 }), "capped at momentum_max");
        }

        [Test]
        public void ASkillThatHitsSeveralEnemies_GivesOneStack()
        {
            var fight = Duel((id, h) => { if (id == "p0") PassiveTriggers(h, ThordisPassive); });
            var p0 = fight.Players[0];
            var log = new List<FightEvent>();

            Until(fight, p0, log);
            var events = fight.Act(new Choice(Button.Special, fight.Enemies[1]));   // e1 and its neighbours e0, e2

            Assert.That(Of(events, FightEventKind.Damaged), Has.Count.EqualTo(3));
            Assert.That(Stacks(p0), Is.EqualTo(1));
        }

        [Test]
        public void ASkillThatMissesEveryTarget_LosesAllStacks_AndOneThatHitsKeepsGrowing()
        {
            var lost = 0;
            var kept = 0;
            for (var seed = 1; seed <= 60; seed++)
            {
                var fight = Duel((id, h) =>
                {
                    if (id == "p0") PassiveTriggers(h, ThordisPassive);
                    if (id == "e0") h["stats"]["dodge_chance"] = 30;
                }, seed);
                var p0 = fight.Players[0];
                GiveMomentum(p0, 3);
                var log = new List<FightEvent>();

                Until(fight, p0, log);
                var events = fight.Act(new Choice(Button.Basic, fight.Enemies[0]));
                if (Of(events, FightEventKind.Missed).Count > 0)
                {
                    lost++;
                    Assert.That(Stacks(p0), Is.EqualTo(0), $"seed {seed}: a full miss loses every stack");
                    Assert.That(Of(events, FightEventKind.EffectLost, p0).Single().Effect, Is.EqualTo("momentum"));
                }
                else
                {
                    kept++;
                    Assert.That(Stacks(p0), Is.EqualTo(4), $"seed {seed}");
                }
            }
            Assert.That(lost, Is.GreaterThan(0), "no seed missed");
            Assert.That(kept, Is.GreaterThan(0), "no seed hit");
        }

        [Test]
        public void ASkillThatMissesOneEnemyButHitsAnother_StillGrows()
        {
            // the ultimate hits all five enemies; only e0 can dodge, so it always hits somebody
            var missedSomewhere = 0;
            for (var seed = 1; seed <= 40; seed++)
            {
                var fight = Duel((id, h) =>
                {
                    if (id == "p0") PassiveTriggers(h, ThordisPassive);
                    if (id == "e0") h["stats"]["dodge_chance"] = 30;
                }, seed);
                var p0 = fight.Players[0];
                p0.Charge = 100;
                GiveMomentum(p0, 2);

                fight.AdvanceToChoice();
                var events = fight.Act(new Choice(Button.Ultimate, null));

                if (Of(events, FightEventKind.Missed).Count > 0) missedSomewhere++;
                Assert.That(Stacks(p0), Is.EqualTo(3), $"seed {seed}");
            }
            Assert.That(missedSomewhere, Is.GreaterThan(0), "no seed missed e0");
        }

        [Test]
        public void AHealOrASelfBuff_LeavesMomentumAsItIs()
        {
            var fight = Duel((id, h) =>
            {
                if (id != "p0") return;
                PassiveTriggers(h, ThordisPassive);
                Pieces(h, "basic", "{ do: 'effect', target: 'self', effect: 'offense_up', turns: 1 }");
                Pieces(h, "special", "{ do: 'heal', target: 'self', percent: 10 }");
            });
            var p0 = fight.Players[0];
            GiveMomentum(p0, 2);
            var log = new List<FightEvent>();

            Until(fight, p0, log);
            fight.Act(new Choice(Button.Basic, null));
            Assert.That(Stacks(p0), Is.EqualTo(2), "a self-buff: no stack gained, none lost (10.6 q. 7)");

            Until(fight, p0, log);
            fight.Act(new Choice(Button.Special, null));
            Assert.That(Stacks(p0), Is.EqualTo(2), "a heal: the same");
        }

        [Test]
        public void ASkillThatTouchesNobody_LeavesMomentumAsItIs()
        {
            // reading R15: every piece's condition is false, so nobody was touched: not a hit and not a miss
            var fight = Duel((id, h) =>
            {
                if (id != "p0") return;
                PassiveTriggers(h, ThordisPassive);
                Pieces(h, "basic", "{ do: 'damage', target: 'enemy', power: 100, if: { test: 'target_has_effect', effect: 'ward' } }");
            });
            var p0 = fight.Players[0];
            GiveMomentum(p0, 2);
            var log = new List<FightEvent>();

            Until(fight, p0, log);
            var events = fight.Act(new Choice(Button.Basic, fight.Enemies[0]));

            Assert.That(Of(events, FightEventKind.Damaged), Is.Empty);
            Assert.That(Stacks(p0), Is.EqualTo(2));
        }

        [Test]
        public void ACounterAttackCounts()
        {
            // e0 holds a Counter and Thordis's passive: p0 hits e0, e0 hits back (it hit p0) and gains a stack (10.6 q. 3)
            var fight = Duel((id, h) => { if (id == "e0") PassiveTriggers(h, ThordisPassive); });
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "counter", 2);
            var log = new List<FightEvent>();

            Until(fight, fight.Players[0], log);
            var events = fight.Act(new Choice(Button.Basic, e0));

            Assert.That(Of(events, FightEventKind.CounterAttack, e0), Has.Count.EqualTo(1));
            Assert.That(Stacks(e0), Is.EqualTo(1));
        }

        // ---- ally below half ----

        /// <summary>
        /// Plays until e0 acts, then e0 uses its Basic on <paramref name="target"/> (a 182 hit). Returns everything that
        /// happened on the way, e0's hit last: the others' turns hit p0 (the first choosable hero), never the ally.
        /// </summary>
        static IReadOnlyList<FightEvent> EnemyHits(Fight fight, Fighter target)
        {
            var log = new List<FightEvent>();
            Until(fight, fight.Enemies[0], log);
            log.AddRange(fight.Act(new Choice(Button.Basic, target)));
            return log;
        }

        /// <summary>A fight where the hero in slot <paramref name="halvard"/> has Halvard's passive (the others have none).</summary>
        static Fight HalvardFight(int halvard = 1) => Duel((id, h) => { if (id == "p" + halvard) PassiveTriggers(h, HalvardPassive); });

        static bool HasTaunt(Fighter hero) => hero.Effects.Any(e => e.Kind == "taunt");

        [Test]
        public void AllyDropsBelowHalf_HalvardTauntsAtOnce_AfterThatHit()
        {
            var fight = HalvardFight();
            var halvard = fight.Players[1];
            var ally = fight.Players[2];
            ally.Health = 500100;                       // the hit does 182: 499,918, below half of 1,000,000

            var events = EnemyHits(fight, ally);

            var taunt = events.ToList().FindIndex(e => e.Kind == FightEventKind.EffectApplied && e.Who == halvard && e.Effect == "taunt");
            var hit = events.ToList().FindIndex(e => e.Kind == FightEventKind.Damaged && e.Who == ally);
            Assert.That(hit, Is.GreaterThanOrEqualTo(0));
            Assert.That(taunt, Is.GreaterThan(hit), "the Taunt comes right after the hit that dropped the ally");
            Assert.That(HasTaunt(halvard));
        }

        [Test]
        public void ItFiresOncePerAlly_EvenWhenTheAllyIsHealedAndDropsAgain()
        {
            var fight = HalvardFight();
            var halvard = fight.Players[1];
            var ally = fight.Players[2];
            ally.Health = 500100;
            EnemyHits(fight, ally);
            Assert.That(HasTaunt(halvard));

            Effects.UseUp(halvard, "taunt");
            ally.Health = 500100;                       // healed back to just above half, and about to drop again
            var events = EnemyHits(fight, ally);

            Assert.That(Of(events, FightEventKind.Damaged, ally), Has.Count.EqualTo(1));
            Assert.That(HasTaunt(halvard), Is.False, "once per ally per fight (02 GDD 8)");
        }

        [Test]
        public void ADifferentAllyDropping_FiresItAgain()
        {
            var fight = HalvardFight();
            var halvard = fight.Players[1];
            fight.Players[2].Health = 500100;
            EnemyHits(fight, fight.Players[2]);
            Effects.UseUp(halvard, "taunt");

            fight.Players[0].Health = 500100;
            var events = EnemyHits(fight, fight.Players[0]);

            // (the Taunt may already have run out by the time e0 acts: the others' hits on p0 drop him first)
            Assert.That(Of(events, FightEventKind.EffectApplied, halvard).Select(e => e.Effect), Has.Member("taunt"));
        }

        [Test]
        public void ExactlyHalf_IsNotBelowHalf_ButTheNextHitIs()
        {
            var fight = HalvardFight();
            var halvard = fight.Players[1];
            var ally = fight.Players[2];
            ally.Health = 500182;                        // the hit leaves exactly 500,000

            EnemyHits(fight, ally);
            Assert.That(ally.Health, Is.EqualTo(500000));
            Assert.That(HasTaunt(halvard), Is.False);

            EnemyHits(fight, ally);                      // 499,818
            Assert.That(HasTaunt(halvard));
        }

        [Test]
        public void HalvardHimselfDropping_DoesNotFire()
        {
            var fight = HalvardFight();
            var halvard = fight.Players[1];
            halvard.Health = 500100;

            EnemyHits(fight, halvard);

            Assert.That(halvard.Health, Is.LessThan(500000));
            Assert.That(HasTaunt(halvard), Is.False, "the passive is for another ally (10.4)");
        }

        [Test]
        public void AnAllyKnockedOutByTheHit_DoesNotFire()
        {
            var fight = HalvardFight();
            var ally = fight.Players[2];
            ally.Health = 100;

            var events = EnemyHits(fight, ally);

            Assert.That(Of(events, FightEventKind.KnockedOut, ally), Has.Count.EqualTo(1));
            Assert.That(HasTaunt(fight.Players[1]), Is.False, "the ally must still be standing");
        }

        [Test]
        public void AKnockedOutHalvard_DoesNotFire()
        {
            var fight = HalvardFight();
            fight.Players[1].KnockedOut = true;
            var ally = fight.Players[2];
            ally.Health = 500100;

            var events = EnemyHits(fight, ally);

            Assert.That(Of(events, FightEventKind.EffectApplied), Is.Empty);
        }

        [Test]
        public void TheSkillThatDroppedTheAlly_CarriesOnWithTheTargetsItHad()
        {
            // Halvard (slot 3) stands after the ally (slot 2) in the order of an all-enemies skill: his Taunt lands
            // between their two hits, and the two heroes after the ally are still hit.
            var fight = HalvardFight(halvard: 3);
            var halvard = fight.Players[3];
            var ally = fight.Players[2];
            ally.Health = 500100;
            var e0 = fight.Enemies[0];
            e0.Charge = 100;

            Until(fight, e0, new List<FightEvent>());
            var events = fight.Act(new Choice(Button.Ultimate, null)).ToList();

            var kinds = events.Where(e => e.Kind == FightEventKind.Damaged || e.Kind == FightEventKind.EffectApplied)
                              .Select(e => (e.Kind, e.Who.Slot)).ToList();
            Assert.That(kinds, Is.EqualTo(new[]
            {
                (FightEventKind.Damaged, 0), (FightEventKind.Damaged, 1), (FightEventKind.Damaged, 2),
                (FightEventKind.EffectApplied, 3), (FightEventKind.Damaged, 3), (FightEventKind.Damaged, 4),
            }));
            Assert.That(HasTaunt(halvard));
        }
    }
}
