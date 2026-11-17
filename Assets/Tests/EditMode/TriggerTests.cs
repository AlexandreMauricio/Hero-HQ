using System;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P1-13 (03 Architecture 4.12): moment bonuses (4.6) and the fight-start and turn-start triggers (02 GDD 8).
    // Strix's first attack always crits (a counter counts; used up on a miss); Doc Nefer's Outlaw bonus on her
    // skills, not on the leader heal; fight-start order (10.6 q. 5); Ibis's Ward goes to the other ally in the
    // lowest slot; Skipjack's fill; a turn-start trigger fires on a stunned turn. Leader triggers run on each
    // matching ally (03 Architecture 3.2).
    //
    // The set-up is SkillPiecesTests': p0 (Speed 200) acts first, everyone else (Speed 50) after it. Test heroes:
    // Damage 200, Armor 100, Health 1,000,000, Accuracy and Dodge 0 (nothing misses unless a test gives a Dodge),
    // Crit Chance set to 0 here, traits hero · greek · human, no leader bonus and no triggers unless a test adds
    // them. The first hero of each team is its named leader. The test data has no Outlaw trait: "villain" stands in.
    public class TriggerTests
    {
        static Fight Duel(Action<string, JObject> edit = null, int seed = 1) => NewFight(
            new[] { ("p0", 200), ("p1", 50), ("p2", 50), ("p3", 50), ("p4", 50) },
            new[] { ("e0", 50), ("e1", 50), ("e2", 50), ("e3", 50), ("e4", 50) }, seed,
            edit: (id, h) => { h["stats"]["crit_chance"] = 0; edit?.Invoke(id, h); });

        static void Pieces(JObject hero, string button, params string[] pieces) =>
            hero[button]["pieces"] = JArray.Parse("[" + string.Join(",", pieces) + "]");

        static void Stat(JObject hero, string stat, double value) => hero["stats"][stat] = value;
        static void Traits(JObject hero, params string[] traits) => hero["traits"] = new JArray(traits);
        static void PassiveBonuses(JObject hero, string bonuses) => hero["passive"]["bonuses"] = JArray.Parse(bonuses);
        static void PassiveTriggers(JObject hero, string triggers) => hero["passive"]["triggers"] = JArray.Parse(triggers);
        static void LeaderTriggers(JObject hero, string triggers) => hero["leader"]["triggers"] = JArray.Parse(triggers);

        /// <summary>Strix's passive (02 GDD 10.4): Crit Chance +100 on her first attack this fight.</summary>
        const string FirstAttackCrits = "[{ stat: 'crit_chance', percent: 100, if: { test: 'first_attack' } }]";

        static List<FightEvent> Of(IEnumerable<FightEvent> events, FightEventKind kind, Fighter who = null) =>
            events.Where(e => e.Kind == kind && (who == null || e.Who == who)).ToList();

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

        // ---- moment bonuses (03 Architecture 4.6) ----

        [Test]
        public void FirstAttack_AlwaysCrits_ThenNeverAgain()
        {
            var fight = Duel((id, h) => { if (id == "p0") PassiveBonuses(h, FirstAttackCrits); });
            var p0 = fight.Players[0];
            var log = new List<FightEvent>();

            Until(fight, p0, log);
            var first = Of(fight.Act(new Choice(Button.Basic, fight.Enemies[0])), FightEventKind.Damaged).Single();
            Until(fight, p0, log);
            var second = Of(fight.Act(new Choice(Button.Basic, fight.Enemies[0])), FightEventKind.Damaged).Single();

            Assert.That(first.Crit, Is.True, "Crit Chance 0 + 100 on the first attack");
            Assert.That(second.Crit, Is.False, "Crit Chance 0 afterwards");
        }

        [Test]
        public void FirstAttack_IsUsedUp_EvenWhenItMisses()
        {
            // e0 has Dodge 30, so some seeds miss p0's first attack; then the second attack (on e1, no Dodge) hits without a crit.
            var seen = 0;
            for (var seed = 1; seed <= 40; seed++)
            {
                var fight = Duel((id, h) =>
                {
                    if (id == "p0") PassiveBonuses(h, FirstAttackCrits);
                    if (id == "e0") Stat(h, "dodge_chance", 30);
                }, seed);
                var p0 = fight.Players[0];
                var log = new List<FightEvent>();

                Until(fight, p0, log);
                if (Of(fight.Act(new Choice(Button.Basic, fight.Enemies[0])), FightEventKind.Missed).Count == 0) continue;
                seen++;
                Until(fight, p0, log);
                var hit = Of(fight.Act(new Choice(Button.Basic, fight.Enemies[1])), FightEventKind.Damaged).Single();
                Assert.That(hit.Crit, Is.False, $"seed {seed}: the missed first attack used the bonus up (reading R4)");
            }
            Assert.That(seen, Is.GreaterThan(0), "no seed missed the first attack");
        }

        [Test]
        public void FirstAttack_CanBeACounter()
        {
            // e0 holds a Counter and Strix's passive; p0 hits e0, e0's counter is its first attack and crits;
            // e0's own first turn afterwards does not.
            var fight = Duel((id, h) => { if (id == "e0") PassiveBonuses(h, FirstAttackCrits); });
            var p0 = fight.Players[0];
            var e0 = fight.Enemies[0];
            Effects.Apply(e0, "counter", 2);
            var log = new List<FightEvent>();

            Until(fight, p0, log);
            var events = fight.Act(new Choice(Button.Basic, e0));
            var counterHit = Of(events, FightEventKind.Damaged, p0).Single();
            Assert.That(counterHit.Crit, Is.True, "the counter is e0's first attack (10.6 q. 3)");

            Until(fight, e0, log);
            var ownTurn = Of(fight.Act(new Choice(Button.Basic, fight.Players[0])), FightEventKind.Damaged).Single();
            Assert.That(ownTurn.Crit, Is.False);
        }

        [Test]
        public void TargetHasBonus_OnHerSkills_NotOnTheLeaderHeal()
        {
            // Doc Nefer (p0, Outlaw, the leader): her heal +20 % on Outlaw allies; her leader heals every ally 3 %
            // at the start of their own turn, as their own heal, with no moment bonus (03 Architecture 4.6).
            var fight = Duel((id, h) =>
            {
                if (id == "p0" || id == "p1") Traits(h, "villain", "greek", "human");
                if (id != "p0") return;
                PassiveBonuses(h, "[{ stat: 'healing', percent: 20, if: { test: 'target_has', trait: 'villain' } }]");
                LeaderTriggers(h, "[{ when: 'turn_start', pieces: [{ do: 'heal', target: 'self', percent: 3 }] }]");
                Pieces(h, "basic", "{ do: 'heal', target: 'ally', percent: 20 }");
            });
            var p = fight.Players;
            foreach (var f in p.Concat(fight.Enemies)) f.Health = 500000;
            var log = new List<FightEvent>();

            Until(fight, p[0], log);
            Assert.That(Of(log, FightEventKind.Healed, p[0]).Single().Amount, Is.EqualTo(30000),
                        "3 % at her own turn start, not 3.6 %: she is an Outlaw, but her bonus waits for her skills");
            Assert.That(Of(fight.Act(new Choice(Button.Basic, p[1])), FightEventKind.Healed).Single().Amount, Is.EqualTo(240000),
                        "20 % + 20 % on an Outlaw ally");

            Until(fight, p[1], log);
            var turnStart = log.FindLastIndex(e => e.Kind == FightEventKind.TurnStarted && e.Who == p[1]);
            Assert.That(log[turnStart + 1].Kind, Is.EqualTo(FightEventKind.Healed));
            Assert.That(log[turnStart + 1].Amount, Is.EqualTo(30000), "the leader heal on another ally: 3 %, as that ally's own");
            fight.Act(Basic(fight));

            Until(fight, p[0], log);
            Assert.That(Of(fight.Act(new Choice(Button.Basic, p[2])), FightEventKind.Healed).Single().Amount, Is.EqualTo(200000),
                        "20 % on an ally who isn't an Outlaw");
            Assert.That(Of(log, FightEventKind.Healed).Where(e => e.Who.Side == Side.Enemy), Is.Empty,
                        "only the named leader's own side heals");
        }

        // ---- fight-start triggers ----

        [Test]
        public void LeaderFill_AtFightStart_OnEachMatchingAlly()
        {
            // Skipjack's leader: Greek allies fill 50 % of their turn meter at fight start (10.4). p2 and p4 are Norse.
            var fight = Duel((id, h) =>
            {
                if (id == "p2" || id == "p4") Traits(h, "hero", "norse", "human");
                if (id == "p0") LeaderTriggers(h, "[{ when: 'fight_start', who: { trait: 'greek' }, pieces: [{ do: 'meter', target: 'self', percent: 50 }] }]");
            });

            Assert.That(fight.Players.Select(f => f.Meter), Is.EqualTo(new[] { 500, 500, 0, 500, 0 }));
            Assert.That(fight.Enemies.Select(f => f.Meter), Is.All.EqualTo(0), "the enemy's leader has no such trigger");
            Assert.That(fight.StartEvents.Select(e => e.Kind), Is.All.EqualTo(FightEventKind.MeterChanged));
            Assert.That(fight.StartEvents.Select(e => e.Who), Is.EqualTo(new[] { 0, 1, 3 }.Select(s => fight.Players[s])));
            Assert.That(fight.StartEvents.Select(e => e.Amount), Is.All.EqualTo(500));
            Assert.That(fight.PredictTurns(1)[0].Who, Is.SameAs(fight.Players[0]), "the bar already shows the fill");
        }

        [TestCase(2, 0)]
        [TestCase(0, 1)]
        public void IbisWard_GoesToHerselfAndTheOtherAllyInTheLowestSlot(int ibis, int other)
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p" + ibis)
                    PassiveTriggers(h, @"[{ when: 'fight_start', pieces: [{ do: 'effect', target: 'self', effect: 'ward', turns: 2 },
                                                                          { do: 'effect', target: 'most_hurt_ally', effect: 'ward', turns: 2 }] }]");
            });

            var warded = fight.Players.Where(f => f.Effects.Any(e => e.Kind == "ward")).Select(f => f.Slot);
            Assert.That(warded, Is.EquivalentTo(new[] { ibis, other }));
            Assert.That(fight.Enemies.Where(f => f.Effects.Count > 0), Is.Empty);
        }

        [Test]
        public void FightStartOrder_LeadersYoursThenTheirs_ThenPassivesInSlotOrderYourSideFirst()
        {
            var fight = Duel((id, h) =>
            {
                if (id == "p0") LeaderTriggers(h, "[{ when: 'fight_start', pieces: [{ do: 'effect', target: 'self', effect: 'ward', turns: 2 }] }]");
                if (id == "e0") LeaderTriggers(h, "[{ when: 'fight_start', pieces: [{ do: 'effect', target: 'self', effect: 'offense_up', turns: 2 }] }]");
                if (id == "p1" || id == "p3" || id == "e1")
                    PassiveTriggers(h, "[{ when: 'fight_start', pieces: [{ do: 'effect', target: 'self', effect: 'defense_up', turns: 2 }] }]");
            });
            var p = fight.Players;
            var e = fight.Enemies;

            var expected = p.Select(f => (f, "ward"))
                .Concat(e.Select(f => (f, "offense_up")))
                .Concat(new[] { (p[1], "defense_up"), (p[3], "defense_up"), (e[1], "defense_up") })
                .Select(x => new FightEvent(FightEventKind.EffectApplied, x.Item1, x.Item2).ToString());
            Assert.That(fight.StartEvents.Select(ev => ev.ToString()), Is.EqualTo(expected));
        }

        [Test]
        public void ATriggerAimedAtAnEnemy_IsRefused()
        {
            // reading R13: a trigger has nobody tapped and never rolls, so it aims only at its own side
            Assert.Throws<NotSupportedException>(() => Duel((id, h) =>
            {
                if (id == "p0") PassiveTriggers(h, "[{ when: 'fight_start', pieces: [{ do: 'effect', target: 'all_enemies', effect: 'offense_down', turns: 2 }] }]");
            }));
        }

        // ---- turn-start triggers ----

        [Test]
        public void TurnStartTrigger_FiresEvenOnAStunnedTurn_BeforeTheSkip()
        {
            // Hori's passive (10.4): a Counter on himself at the start of his turn. Stunned, the turn is skipped, but
            // the trigger has fired first (02 GDD 2, step 2).
            var fight = Duel((id, h) =>
            {
                if (id == "p0") PassiveTriggers(h, "[{ when: 'turn_start', pieces: [{ do: 'effect', target: 'self', effect: 'counter', turns: 1 }] }]");
            });
            var p0 = fight.Players[0];
            Effects.Apply(p0, "stun", 0);

            var events = fight.AdvanceToChoice().Where(ev => ev.Who == p0).Select(ev => ev.Kind).ToList();

            Assert.That(events.Take(3), Is.EqualTo(new[] { FightEventKind.TurnStarted, FightEventKind.EffectApplied, FightEventKind.TurnSkipped }));
            Assert.That(p0.Effects.Any(ef => ef.Kind == "counter"), "put on at the start of the skipped turn, it lasts through his next turn");
        }
    }
}
