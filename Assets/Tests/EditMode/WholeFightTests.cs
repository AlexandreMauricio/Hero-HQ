using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace HeroHQ.Tests
{
    // P1-16 (03 Architecture 4.12): the whole fight, 02 GDD 11 test 16. Two fixed squads of five test heroes fight
    // with EnemyAI choosing for BOTH sides (the same loop the fight report, P1-18, will use), until somebody wins.
    // The squads are small test heroes with round numbers written here (so they keep passing while the real numbers
    // are tuned, 4.12), but their kits are mixed on purpose: a Stun, a heal, a Barrier, a Counter, a turn-meter drain,
    // Momentum, a Ward, a skill that can't miss and one that always crits, plus a little Dodge so there are misses.
    // That way a whole fight touches most rules, and the "does it cover them" test below keeps it that way.
    //
    //   Squad A (players, 3–2, leader ares):  ares 130 · bea 100 · cyr 90 | dara 110 · eli 80   (the number is Speed)
    //   Squad B (enemies, 2–3, leader fenn):  fenn 120 · gia 95 | hal 105 · ira 85 · jax 100
    public class WholeFightTests
    {
        const int MaxTurns = 300;                 // 02 GDD 11 test 16 / 03 Architecture 4.10: the safety stop

        static string Hero(string id, int speed, string side, string pantheon, System.Action<JObject> edit = null)
        {
            var hero = JObject.Parse(TestData.Hero(id, side: side, pantheon: pantheon, speed: speed));
            edit?.Invoke(hero);
            return hero.ToString(Formatting.None);
        }

        static JArray Pieces(string json) => JArray.Parse(json);

        static GameData Data()
        {
            const string hero = "hero", villain = "villain", greek = "greek", norse = "norse";
            var heroes = TestData.HeroesFile(
                Hero("ares", 130, hero, greek),
                // Stun off the Special: the stunned enemy loses a turn (02 GDD 7.4)
                Hero("bea", 100, hero, greek, h => h["special"]["pieces"] =
                    Pieces("[{ do: 'damage', target: 'enemy', power: 120 }, { do: 'effect', target: 'enemy', effect: 'stun' }]")),
                // a Counter at the start of every one of her turns (Hori's shape)
                Hero("cyr", 90, hero, greek, h =>
                {
                    h["stats"]["dodge_chance"] = 15;
                    h["passive"]["triggers"] = Pieces("[{ when: 'turn_start', pieces: [{ do: 'effect', target: 'self', effect: 'counter', turns: 2 }] }]");
                }),
                // the healer: a heal Special; the Ultimate heals everyone and gives each a Barrier
                Hero("dara", 110, hero, greek, h =>
                {
                    h["special"]["pieces"] = Pieces("[{ do: 'heal', target: 'most_hurt_ally', percent: 30 }]");
                    h["ultimate"]["pieces"] = Pieces("[{ do: 'heal', target: 'all_allies', percent: 20 }, " +
                                                     "{ do: 'effect', target: 'all_allies', effect: 'barrier', turns: 2, percent: 10 }]");
                }),
                // Momentum from every attack that hits; the Special drains the next enemy's turn meter
                Hero("eli", 80, hero, greek, h =>
                {
                    h["stats"]["dodge_chance"] = 15;
                    h["passive"]["triggers"] = Pieces("[{ when: 'attack_hits', pieces: [{ do: 'effect', target: 'self', effect: 'momentum' }] }]");
                    h["special"]["pieces"] = Pieces("[{ do: 'damage', target: 'enemy', power: 100 }, { do: 'meter', target: 'next_enemy', percent: -50 }]");
                }),

                Hero("fenn", 120, villain, greek),
                // a bad effect off the Special: Defense Down for two turns
                Hero("gia", 95, villain, norse, h =>
                {
                    h["stats"]["dodge_chance"] = 15;
                    h["special"]["pieces"] = Pieces("[{ do: 'damage', target: 'enemy', power: 100 }, " +
                                                    "{ do: 'effect', target: 'enemy', effect: 'defense_down', turns: 2 }]");
                }),
                // starts the fight with a Ward on herself (a fight-start trigger)
                Hero("hal", 105, villain, greek, h =>
                {
                    h["stats"]["dodge_chance"] = 15;
                    h["passive"]["triggers"] = Pieces("[{ when: 'fight_start', pieces: [{ do: 'effect', target: 'self', effect: 'ward', turns: 3 }] }]");
                }),
                // an Ultimate that always crits
                Hero("ira", 85, villain, norse, h =>
                    h["ultimate"]["pieces"] = Pieces("[{ do: 'damage', target: 'all_enemies', power: 250, always_crits: true }]")),
                // a Special that can't miss and reaches the back row
                Hero("jax", 100, villain, greek, h =>
                    h["special"]["pieces"] = Pieces("[{ do: 'damage', target: 'enemy', power: 80, cant_miss: true, reaches_back_row: true }]")));

            string Team(string id, string kind, string formation, params string[] slots) =>
                "{ \"id\": \"" + id + "\", \"kind\": \"" + kind + "\", \"formation\": \"" + formation + "\", \"leader\": \"" + slots[0] +
                "\", \"slots\": [" + string.Join(",", slots.Select(s => "\"" + s + "\"")) + "] }";
            var teams = "[" + Team("squad_a", "target", "3-2", "ares", "bea", "cyr", "dara", "eli") + "," +
                              Team("squad_b", "enemy", "2-3", "fenn", "gia", "hal", "ira", "jax") + "]";
            return TestData.Load(heroes: heroes, teams: teams);
        }

        /// <summary>One played fight: the fight, every event in order (the fight-start ones first) and how many turns were started.</summary>
        sealed class Played
        {
            public Fight Fight;
            public List<FightEvent> Events = new List<FightEvent>();
            public int Turns => Events.Count(e => e.Kind == FightEventKind.TurnStarted);
            public List<string> Text => Events.Select(e => e.ToString()).ToList();
        }

        /// <summary>The loop of 03 Architecture 4.3 with the AI choosing for whichever side is acting, until the fight is decided or the turn limit.</summary>
        static Played Play(int seed)
        {
            var data = Data();
            var fight = Fight.Create(data, data.Teams["squad_a"], data.Teams["squad_b"], seed);
            var played = new Played { Fight = fight };
            played.Events.AddRange(fight.StartEvents);

            while (fight.Outcome == Outcome.Going && played.Turns < MaxTurns)
            {
                played.Events.AddRange(fight.AdvanceToChoice());
                if (fight.Outcome != Outcome.Going) break;
                Assert.IsFalse(fight.Acting.KnockedOut, "a knocked-out hero never gets a turn");
                played.Events.AddRange(fight.Act(EnemyAI.Choose(fight)));
            }
            return played;
        }

        // ---- test 16: to a winner ----------------------------------------------------------------------------------

        [Test]
        public void TwoSquads_AIOnBothSides_PlayToAWinner_WithinTheTurnLimit([Range(1, 12)] int seed)
        {
            var played = Play(seed);
            var fight = played.Fight;

            Assert.AreNotEqual(Outcome.Going, fight.Outcome, $"seed {seed}: no winner after {MaxTurns} turns");
            Assert.LessOrEqual(played.Turns, MaxTurns);

            // exactly one ending, and it is the last event
            var endings = played.Events.Where(e => e.Kind == FightEventKind.FightWon || e.Kind == FightEventKind.FightLost).ToList();
            Assert.AreEqual(1, endings.Count, "one FightWon or FightLost");
            Assert.AreSame(played.Events.Last(), endings[0], "and nothing happens after it");
            Assert.AreEqual(fight.Outcome == Outcome.Won ? FightEventKind.FightWon : FightEventKind.FightLost, endings[0].Kind);

            // the winner has someone standing, the loser has nobody
            var (winners, losers) = fight.Outcome == Outcome.Won ? (fight.Players, fight.Enemies) : (fight.Enemies, fight.Players);
            Assert.IsTrue(losers.All(f => f.KnockedOut), "every loser is knocked out");
            Assert.IsTrue(winners.Any(f => !f.KnockedOut), "somebody on the winning side still stands");

            // Health stays in range; knocked out means exactly Health 0
            foreach (var f in fight.Players.Concat(fight.Enemies))
            {
                Assert.That(f.Health, Is.InRange(0, f.MaxHealth), f.ToString());
                Assert.AreEqual(f.Health == 0, f.KnockedOut, $"{f}: knocked out exactly when Health is 0");
            }
        }

        [Test]
        public void TheAIPlaysBothSides()
        {
            var played = Play(1);
            var used = played.Events.Where(e => e.Kind == FightEventKind.SkillUsed).Select(e => e.Who.Side).ToList();
            Assert.IsTrue(used.Contains(Side.Player), "squad A used skills");
            Assert.IsTrue(used.Contains(Side.Enemy), "squad B used skills");
        }

        // ---- test 16: the same seed, the same fight ----------------------------------------------------------------

        [Test]
        public void TheSameSeed_GivesTheSameEventListAndEndStateTwice([Range(1, 6)] int seed)
        {
            var first = Play(seed);
            var second = Play(seed);

            Assert.AreEqual(first.Text, second.Text, "the whole event list, text for text");
            Assert.AreEqual(first.Fight.Outcome, second.Fight.Outcome);
            Assert.AreEqual(first.Fight.Players.Concat(first.Fight.Enemies).Select(f => f.Health),
                            second.Fight.Players.Concat(second.Fight.Enemies).Select(f => f.Health), "the same Health everywhere at the end");
        }

        [Test]
        public void DifferentSeeds_GiveDifferentFights_SoTheSeedReallyDecidesTheLuck()
        {
            // without this, "the same seed twice" would also pass for a fight that ignored the seed
            var fights = Enumerable.Range(1, 12).Select(s => string.Join("\n", Play(s).Text)).Distinct().Count();
            Assert.Greater(fights, 1, "twelve seeds gave the very same fight: the seed is not used");
        }

        // ---- the squads really use the rules -----------------------------------------------------------------------

        [Test]
        public void AcrossTheSeeds_TheFightsTouchTheRulesTheSquadsWereBuiltFor()
        {
            var events = Enumerable.Range(1, 12).SelectMany(s => Play(s).Events).ToList();
            bool Has(FightEventKind kind, string effect = null, Button? button = null) =>
                events.Any(e => e.Kind == kind && (effect == null || e.Effect == effect) && (button == null || e.Button == button));

            Assert.IsTrue(Has(FightEventKind.SkillUsed, button: Button.Basic), "a Basic");
            Assert.IsTrue(Has(FightEventKind.SkillUsed, button: Button.Special), "a Special");
            Assert.IsTrue(Has(FightEventKind.SkillUsed, button: Button.Ultimate), "an Ultimate");
            Assert.IsTrue(Has(FightEventKind.Missed), "a miss (Dodge)");
            Assert.IsTrue(events.Any(e => e.Kind == FightEventKind.Damaged && e.Crit), "a critical hit");
            Assert.IsTrue(Has(FightEventKind.KnockedOut), "a knockout");
            Assert.IsTrue(Has(FightEventKind.ChargeChanged), "ultimate charge");
            Assert.IsTrue(Has(FightEventKind.Healed), "a heal (dara)");
            Assert.IsTrue(Has(FightEventKind.EffectApplied, "barrier"), "a Barrier (dara's Ultimate)");
            Assert.IsTrue(Has(FightEventKind.EffectApplied, "stun"), "a Stun (bea)");
            Assert.IsTrue(Has(FightEventKind.TurnSkipped), "a skipped, stunned turn");
            Assert.IsTrue(Has(FightEventKind.EffectApplied, "defense_down"), "Defense Down (gia)");
            Assert.IsTrue(Has(FightEventKind.EffectApplied, "counter"), "a Counter put on (cyr)");
            Assert.IsTrue(Has(FightEventKind.CounterAttack), "a Counter fires");
            Assert.IsTrue(Has(FightEventKind.MeterChanged), "a turn-meter drain (eli)");
            Assert.IsTrue(Has(FightEventKind.EffectApplied, "momentum"), "Momentum (eli)");
            Assert.IsTrue(Has(FightEventKind.EffectApplied, "ward"), "a Ward at fight start (hal)");
        }
    }
}
