using System;
using System.Linq;
using HeroHQ.Rules;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using static HeroHQ.Tests.TestFights;

namespace HeroHQ.Tests
{
    // P1-12 (03 Architecture 4.12): leader and passive bonuses, fixed part, and the team-select preview
    // (02 GDD 8; 10.2 and 10.6 questions 1, 2 and 11; 03 Architecture 4.6).
    // 02 GDD 11 test 13: only the named leader counts; it stays after the leader's knockout; counts don't change
    // after a knockout; the preview is what the fight uses. Also: chance stats add points; Health, Armor and Speed
    // grow by a % and are rounded once, halves up; Damage and healing bonuses are summed in the formulas.
    //
    // Test heroes (TestData, TestFights): Damage 200, Armor 100, Crit Chance 10 (set to 0 here), traits hero ·
    // greek · human, class vanguard, no leader bonus unless a test gives one. Both teams stand 3–2 and the
    // first hero of each is the named leader.
    public class BonusesTests
    {
        static Fight NewTeams(Action<string, JObject> edit, int p0Speed = 100, int e0Speed = 100) => NewFight(
            new[] { ("p0", p0Speed), ("p1", 100), ("p2", 100), ("p3", 100), ("p4", 100) },
            new[] { ("e0", e0Speed), ("e1", 100), ("e2", 100), ("e3", 100), ("e4", 100) },
            edit: (id, h) => { h["stats"]["crit_chance"] = 0; edit?.Invoke(id, h); });

        static void LeaderBonuses(JObject hero, string bonuses) => hero["leader"]["bonuses"] = JArray.Parse(bonuses);
        static void PassiveBonuses(JObject hero, string bonuses) => hero["passive"]["bonuses"] = JArray.Parse(bonuses);
        static void Traits(JObject hero, string pantheon) => hero["traits"] = new JArray("hero", pantheon, "human");

        static HeroBonuses[] PreviewOf(Fight fight, string team)
        {
            var def = fight.Data.Teams[team];
            return Bonuses.Preview(fight.Data, def.Slots, def.Leader).ToArray();
        }

        [Test]
        public void OnlyTheNamedLeadersLeaderSkillCounts_EachSideItsOwn()
        {
            var fight = NewTeams((id, h) =>
            {
                if (id == "p0") LeaderBonuses(h, "[{ stat: 'armor', percent: 10 }]");   // the named leader
                if (id == "p1") LeaderBonuses(h, "[{ stat: 'armor', percent: 50 }]");   // not the leader: never counts
                if (id == "e0") LeaderBonuses(h, "[{ stat: 'armor', percent: 30 }]");   // the enemy's leader
            });

            Assert.That(fight.Players.Select(f => f.Armor), Is.All.EqualTo(110));
            Assert.That(fight.Enemies.Select(f => f.Armor), Is.All.EqualTo(130));
        }

        [Test]
        public void LeaderBonus_GoesToTheAlliesItsWhoNames_TheLeaderIncluded()
        {
            var fight = NewTeams((id, h) =>
            {
                if (id == "p0" || id == "p2") Traits(h, "norse");
                if (id == "p0") LeaderBonuses(h, "[{ stat: 'crit_chance', percent: 20, who: { trait: 'norse' } }]");
            });

            Assert.That(fight.Players.Select(f => f.CritChance), Is.EqualTo(new[] { 20, 0, 20, 0, 0 }));
        }

        [Test]
        public void ChanceStatsAddPoints_HealthArmorSpeedGrowByAPercent_RoundedOnceHalvesUp()
        {
            var fight = NewTeams((id, h) =>
            {
                if (id != "p0") return;
                h["stats"]["health"] = 1005;      // +10 % = 1,105.5 → 1,106
                h["stats"]["armor"] = 150;        // +15 % = 172.5  → 173
                h["stats"]["speed"] = 105;        // +10 % = 115.5  → 116
                h["stats"]["accuracy"] = 10;
                h["stats"]["dodge_chance"] = 5;
                h["stats"]["crit_chance"] = 25;
                PassiveBonuses(h, @"[{ stat: 'health', percent: 10 }, { stat: 'armor', percent: 15 }, { stat: 'speed', percent: 10 },
                                     { stat: 'accuracy', percent: 5 }, { stat: 'dodge_chance', percent: 5 }, { stat: 'crit_chance', percent: 20 }]");
            });
            var p0 = fight.Players[0];

            Assert.That(p0.Health, Is.EqualTo(1106));
            Assert.That(p0.MaxHealth, Is.EqualTo(1106));
            Assert.That(p0.Armor, Is.EqualTo(173));
            Assert.That(p0.Speed, Is.EqualTo(116));
            Assert.That(p0.Accuracy, Is.EqualTo(15), "points: 10 + 5");
            Assert.That(p0.DodgeChance, Is.EqualTo(10), "points: 5 + 5");
            Assert.That(p0.CritChance, Is.EqualTo(45), "points: 25 + 20, not 25 × 1.2 (02 GDD 10.2)");
            Assert.That(p0.Damage, Is.EqualTo(200), "a Damage bonus stays a % for the formula, the stat is unchanged");
        }

        [Test]
        public void DamageBonuses_LeaderPassiveAndOffenseUp_AddUpAndApplyOnce()
        {
            var fight = NewTeams((id, h) =>
            {
                if (id == "p0")
                {
                    LeaderBonuses(h, "[{ stat: 'damage', percent: 20 }]");
                    PassiveBonuses(h, "[{ stat: 'damage', percent: 10 }]");
                }
            }, p0Speed: 200);
            fight.AdvanceToChoice();
            var p0 = fight.Acting;
            Assert.That(p0, Is.SameAs(fight.Players[0]));
            Assert.That(p0.DamageBonus, Is.EqualTo(30));
            Effects.Apply(p0, "offense_up", 2);

            var hit = fight.Act(new Choice(Button.Basic, fight.Enemies[0])).First(e => e.Kind == FightEventKind.Damaged);

            // 200 × 100 % × (1 + 0.20 + 0.10 + 0.25) = 310; Armor 100 of armor_k 1,000 cuts 1/11 → 281.8 → 282.
            // (Multiplied instead, 200 × 1.3 × 1.25 = 325 → 295.)
            Assert.That(hit.Amount, Is.EqualTo(282));
        }

        [Test]
        public void HealingBonus_AddsToTheHeal()
        {
            var fight = NewTeams((id, h) =>
            {
                if (id != "p0") return;
                PassiveBonuses(h, "[{ stat: 'healing', percent: 20 }]");
                h["basic"]["pieces"] = JArray.Parse("[{ do: 'heal', target: 'self', percent: 20 }]");
            }, p0Speed: 200);
            fight.AdvanceToChoice();
            var p0 = fight.Acting;
            p0.Health = p0.MaxHealth / 2;

            var healed = fight.Act(new Choice(Button.Basic, null)).First(e => e.Kind == FightEventKind.Healed);

            // 20 % of 1,000,000 × (1 + 0.20) = 24 % (02 GDD 10.2: Patch Job's 20 % with +20 % heals 24 %)
            Assert.That(healed.Amount, Is.EqualTo(240000));
        }

        [TestCase(2, 120, true)]
        [TestCase(1, 100, false)]
        public void AlliesWith_CountsTheOtherFour_OnOrOff(int norseAllies, int armor, bool on)
        {
            var fight = NewTeams((id, h) =>
            {
                // p0 is Norse too, and never counts itself (02 GDD 7.3)
                if (id == "p0" || (id == "p1" && norseAllies >= 1) || (id == "p2" && norseAllies >= 2)) Traits(h, "norse");
                if (id == "p0") PassiveBonuses(h, "[{ stat: 'armor', percent: 20, if: { test: 'allies_with', count: 2, trait: 'norse' } }]");
            });

            Assert.That(fight.Players[0].Armor, Is.EqualTo(armor));
            var line = PreviewOf(fight, "players")[0].Lines.Single();
            Assert.That(line.On, Is.EqualTo(on));
            Assert.That(line.Allies, Is.EqualTo(norseAllies), "the 'have' count team select shows (02 GDD 17.10)");
        }

        [TestCase(4, 25)]
        [TestCase(3, 20)]
        [TestCase(0, 5)]
        public void PerAllyWith_MultipliesByTheMatchingOtherFour(int greekAllies, int dodge)
        {
            var fight = NewTeams((id, h) =>
            {
                h["stats"]["dodge_chance"] = 5;
                var slot = int.Parse(id.Substring(1));
                if (id.StartsWith("p") && slot > greekAllies) Traits(h, "norse");   // p1..p{greekAllies} stay Greek
                if (id == "p0") PassiveBonuses(h, "[{ stat: 'dodge_chance', percent: 5, if: { test: 'per_ally_with', trait: 'greek' } }]");
            });

            Assert.That(fight.Players[0].DodgeChance, Is.EqualTo(dodge));
            var line = PreviewOf(fight, "players")[0].Lines.Single();
            Assert.That(line.Allies, Is.EqualTo(greekAllies));
            Assert.That(line.On, Is.EqualTo(greekAllies > 0));
            Assert.That(line.Percent, Is.EqualTo(5 * greekAllies));
        }

        [Test]
        public void LeaderBonusStays_AndCountsDontChange_AfterKnockouts()
        {
            // e0 acts first with a hit on every player; the leader p0 and the Norse ally p2 have 1 Health and fall.
            // p1 acts next: its +20 % from the fallen leader and +10 % for one Norse ally (fallen) still count.
            var fight = NewTeams((id, h) =>
            {
                if (id == "p0") LeaderBonuses(h, "[{ stat: 'damage', percent: 20 }]");
                if (id == "p0" || id == "p2") h["stats"]["health"] = 1;
                if (id == "p2") Traits(h, "norse");
                if (id == "p1") { h["stats"]["speed"] = 150; PassiveBonuses(h, "[{ stat: 'damage', percent: 10, if: { test: 'per_ally_with', trait: 'norse' } }]"); }
                if (id == "e0") h["basic"]["pieces"] = JArray.Parse("[{ do: 'damage', target: 'all_enemies', power: 100 }]");
            }, e0Speed: 200);

            fight.AdvanceToChoice();
            Assert.That(fight.Acting, Is.SameAs(fight.Enemies[0]));
            fight.Act(new Choice(Button.Basic, null));
            Assert.That(fight.Players[0].KnockedOut && fight.Players[2].KnockedOut, "the leader and the Norse ally are down");

            fight.AdvanceToChoice();
            var p1 = fight.Acting;
            Assert.That(p1, Is.SameAs(fight.Players[1]));
            Assert.That(p1.DamageBonus, Is.EqualTo(30));
            var hit = fight.Act(new Choice(Button.Basic, fight.Enemies[0])).First(e => e.Kind == FightEventKind.Damaged);

            // 200 × (1 + 0.30) = 260; × 1,000/1,100 = 236.4 → 236
            Assert.That(hit.Amount, Is.EqualTo(236));
        }

        [Test]
        public void Preview_IsWhatTheFightUses_BothSides()
        {
            var fight = NewTeams((id, h) =>
            {
                if (id == "p1" || id == "p3" || id == "e2") Traits(h, "norse");
                if (id == "p0") LeaderBonuses(h, "[{ stat: 'health', percent: 15, who: { trait: 'greek' } }, { stat: 'damage', percent: 20 }]");
                if (id == "p1") PassiveBonuses(h, "[{ stat: 'speed', percent: 10, if: { test: 'allies_with', count: 1, trait: 'norse' } }]");
                if (id == "p2") PassiveBonuses(h, "[{ stat: 'healing', percent: 20 }, { stat: 'armor', percent: 15 }]");
                if (id == "e0") LeaderBonuses(h, "[{ stat: 'accuracy', percent: 10, who: { class: 'vanguard' } }]");
                if (id == "e2") PassiveBonuses(h, "[{ stat: 'crit_chance', percent: 5, if: { test: 'per_ally_with', trait: 'greek' } }]");
            });

            foreach (var (team, fighters) in new[] { ("players", fight.Players), ("enemies", fight.Enemies) })
            {
                var preview = PreviewOf(fight, team);
                for (var slot = 0; slot < 5; slot++)
                {
                    var f = fighters[slot];
                    var p = preview[slot];
                    Assert.That(p.Hero, Is.SameAs(f.Hero));
                    Assert.That(new[] { f.MaxHealth, f.Health, f.Armor, f.Speed, f.Accuracy, f.DodgeChance, f.CritChance, f.Damage, f.CritDamage },
                        Is.EqualTo(new[] { p.Stat("health"), p.Stat("health"), p.Stat("armor"), p.Stat("speed"), p.Stat("accuracy"),
                                           p.Stat("dodge_chance"), p.Stat("crit_chance"), p.Stat("damage"), p.Stat("crit_damage") }), f.ToString());
                    Assert.That(f.DamageBonus, Is.EqualTo(p.DamageBonus), f.ToString());
                    Assert.That(f.HealingBonus, Is.EqualTo(p.HealingBonus), f.ToString());
                }
            }
            // and a few spot checks that something really changed
            Assert.That(fight.Players[1].Speed, Is.EqualTo(110), "one other Norse ally (p3): ON");
            Assert.That(fight.Players[2].MaxHealth, Is.EqualTo(1150000));
            Assert.That(fight.Players[2].HealingBonus, Is.EqualTo(20));
            Assert.That(fight.Enemies[2].CritChance, Is.EqualTo(20), "four other Greek allies × 5");
            Assert.That(fight.Enemies.Select(f => f.Accuracy), Is.All.EqualTo(10));
        }

        [Test]
        public void Preview_LeaderLinesFirst_ThenThePassive_AndAMomentBonusIsListedNotApplied()
        {
            var fight = NewTeams((id, h) =>
            {
                if (id == "p0") LeaderBonuses(h, "[{ stat: 'damage', percent: 20 }]");
                if (id == "p1") PassiveBonuses(h, @"[{ stat: 'crit_chance', percent: 100, if: { test: 'first_attack' } },
                                                      { stat: 'armor', percent: 10, if: { test: 'allies_with', count: 1, trait: 'norse' } }]");
            });

            var lines = PreviewOf(fight, "players")[1].Lines;
            Assert.That(lines.Select(l => (l.Source, l.From.Id, l.Stat)),
                Is.EqualTo(new[] { (BonusSource.Leader, "p0", "damage"), (BonusSource.Passive, "p1", "crit_chance"), (BonusSource.Passive, "p1", "armor") }));
            Assert.That(lines.Select(l => (l.AtTheMoment, l.On)), Is.EqualTo(new[] { (false, true), (true, false), (false, false) }));
            Assert.That(lines[1].Percent, Is.EqualTo(100), "the 'when …' line keeps its number");
            Assert.That(fight.Players[1].CritChance, Is.EqualTo(0), "Strix-style first-attack crit waits for P1-13");
            Assert.That(fight.Players[1].Armor, Is.EqualTo(100), "no Norse ally: OFF");
        }
    }
}
