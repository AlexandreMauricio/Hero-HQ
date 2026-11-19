using System;
using System.IO;
using System.Linq;
using HeroHQ.Rules.Data;
using HeroHQ.Screens;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace HeroHQ.Tests
{
    // P2a-06 (03 Architecture 7.7, 02 GDD 17.5 and 17.10 item 3): one sentence per skill, built from its pieces, so the
    // text on the info strip can't disagree with what the rules do. Most cases build a small skill from JSON (single
    // quotes are fine for Newtonsoft) so each piece kind, target kind and condition is checked on its own; the last
    // group reads the REAL heroes.json, like RealDataTests, and fails on purpose when the owner changes a kit.
    public class SkillTextTests
    {
        static SkillDef Skill(params string[] pieces) =>
            JsonConvert.DeserializeObject<SkillDef>("{ 'name': 'Test', 'pieces': [" + string.Join(",", pieces) + "] }");

        static string Say(params string[] pieces) => SkillText.Sentence(Skill(pieces));

        static GameData data;
        [OneTimeSetUp]
        public void LoadOnce()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            data = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"), Read("tuning.json"), Read("teams.json"), Read("formations.json"));
        }

        // ---- the two sentences the task names ---------------------------------------------------------------------

        [Test]
        public void ReturnToSender_IsTheSentenceOf17_5()
        {
            Assert.AreEqual("Return to Sender: 150 % damage to one enemy, then 150 % more if it hasn't acted yet this fight",
                            SkillText.Sentence(data.Heroes["skipjack"].Special));
        }

        [Test]
        public void Mark_ADamagePieceThenAnEffectPiece_NamesTheSameEnemyWithThat()
        {
            Assert.AreEqual("Mark: 80 % damage to one enemy, then Defense Down on that enemy for 2 turns",
                            SkillText.Sentence(data.Heroes["strix"].Special));
        }

        // ---- every target kind has words (02 GDD 7.2) -------------------------------------------------------------

        [TestCase("enemy", "one enemy")]
        [TestCase("enemy_and_neighbours", "one enemy and their neighbours")]
        [TestCase("all_enemies", "all enemies")]
        [TestCase("self", "self")]
        [TestCase("ally", "one ally")]
        [TestCase("all_allies", "all allies")]
        [TestCase("most_hurt_ally", "the most hurt ally")]
        [TestCase("next_enemy", "the enemy who acts next")]
        public void EveryTargetKind_HasWords(string target, string words)
        {
            Assert.AreEqual($"Test: Taunt on {words} for 1 turn",
                            Say($"{{ 'do': 'effect', 'target': '{target}', 'effect': 'taunt', 'turns': 1 }}"));
        }

        [Test]
        public void AnUnknownTarget_IsRefused_NotShownAsRawData()
        {
            Assert.Throws<ArgumentException>(() => Say("{ 'do': 'damage', 'target': 'everyone', 'power': 100 }"));
        }

        [Test]
        public void AnUnknownPieceKind_IsRefused()
        {
            Assert.Throws<ArgumentException>(() => Say("{ 'do': 'teleport', 'target': 'self' }"));
        }

        // ---- every piece kind has words (02 GDD 7.1) --------------------------------------------------------------

        [Test]
        public void Damage_SaysThePowerAndWhoGetsIt()
        {
            Assert.AreEqual("Test: 100 % damage to one enemy", Say("{ 'do': 'damage', 'target': 'enemy', 'power': 100 }"));
            Assert.AreEqual("Test: 12.5 % damage to all enemies", Say("{ 'do': 'damage', 'target': 'all_enemies', 'power': 12.5 }"),
                            "a fraction is kept, a whole number has no decimals");
        }

        [Test]
        public void Damage_WithSeveralHits_SaysTheHitsAndTheirPower()
        {
            Assert.AreEqual("Test: 2 hits of 55 % damage to one enemy", Say("{ 'do': 'damage', 'target': 'enemy', 'power': 55, 'hits': 2 }"));
            Assert.AreEqual("Test: 100 % damage to one enemy", Say("{ 'do': 'damage', 'target': 'enemy', 'power': 100, 'hits': 1 }"),
                            "one hit is the plain form");
        }

        [Test]
        public void Damage_FlagsAreListedInBrackets_InAFixedOrder()
        {
            Assert.AreEqual("Test: 70 % damage to one enemy (can't miss)",
                            Say("{ 'do': 'damage', 'target': 'enemy', 'power': 70, 'cant_miss': true }"));
            Assert.AreEqual("Test: 70 % damage to one enemy (can't miss, always crits, reaches the back row)",
                            Say("{ 'do': 'damage', 'target': 'enemy', 'power': 70, 'reaches_back_row': true, 'always_crits': true, 'cant_miss': true }"));
        }

        [Test]
        public void Heal_SaysThePercentOfMaxHealth()
        {
            Assert.AreEqual("Test: heal all allies for 30 % of max Health", Say("{ 'do': 'heal', 'target': 'all_allies', 'percent': 30 }"));
            Assert.AreEqual("Test: heal self for 3 % of max Health", Say("{ 'do': 'heal', 'target': 'self', 'percent': 3 }"));
        }

        [Test]
        public void Effect_NamesTheEffectInWordsAndHowLong()
        {
            Assert.AreEqual("Test: Offense Down on all enemies for 2 turns", Say("{ 'do': 'effect', 'target': 'all_enemies', 'effect': 'offense_down', 'turns': 2 }"));
            Assert.AreEqual("Test: Offense Up on self for 1 turn", Say("{ 'do': 'effect', 'target': 'self', 'effect': 'offense_up', 'turns': 1 }"));
        }

        [Test]
        public void Effect_StunAndMomentumHaveNoTurnCount()
        {
            Assert.AreEqual("Test: Stun on one enemy", Say("{ 'do': 'effect', 'target': 'enemy', 'effect': 'stun' }"));
            Assert.AreEqual("Test: Momentum on self", Say("{ 'do': 'effect', 'target': 'self', 'effect': 'momentum' }"));
        }

        [Test]
        public void Effect_ABarrierSaysItsSize()
        {
            Assert.AreEqual("Test: Barrier (25 % of max Health) on all allies for 2 turns",
                            Say("{ 'do': 'effect', 'target': 'all_allies', 'effect': 'barrier', 'turns': 2, 'percent': 25 }"));
        }

        [Test]
        public void Clear_SaysHowManyBadEffects_OrAll()
        {
            Assert.AreEqual("Test: clear 1 bad effect from one ally", Say("{ 'do': 'clear', 'target': 'ally', 'count': 1 }"));
            Assert.AreEqual("Test: clear 2 bad effects from one ally", Say("{ 'do': 'clear', 'target': 'ally', 'count': 2 }"));
            Assert.AreEqual("Test: clear all bad effects from all allies", Say("{ 'do': 'clear', 'target': 'all_allies' }"));
        }

        [Test]
        public void Meter_FillsOrDrainsTheTurnMeter()
        {
            Assert.AreEqual("Test: fill the turn meter of self by 50 %", Say("{ 'do': 'meter', 'target': 'self', 'percent': 50 }"));
            Assert.AreEqual("Test: drain the turn meter of all enemies by 30 %", Say("{ 'do': 'meter', 'target': 'all_enemies', 'percent': -30 }"));
        }

        // ---- every condition has words (02 GDD 7.3) ---------------------------------------------------------------

        [TestCase("{ 'test': 'allies_with', 'count': 2, 'trait': 'egyptian' }", "if 2 or more other allies are Egyptian")]
        [TestCase("{ 'test': 'allies_with', 'count': 1, 'class': 'hunter' }", "if 1 or more other allies are Hunter")]
        [TestCase("{ 'test': 'per_ally_with', 'trait': 'greek' }", "for each other ally that is Greek")]
        [TestCase("{ 'test': 'target_has_effect', 'effect': 'defense_down' }", "if it has Defense Down")]
        [TestCase("{ 'test': 'target_has', 'trait': 'outlaw' }", "if it is Outlaw")]
        [TestCase("{ 'test': 'target_has', 'class': 'mender' }", "if it is Mender")]
        [TestCase("{ 'test': 'target_not_acted' }", "if it hasn't acted yet this fight")]
        [TestCase("{ 'test': 'first_attack' }", "if it is this hero's first attack")]
        public void EveryCondition_HasWords(string condition, string words)
        {
            Assert.AreEqual($"Test: 100 % damage to one enemy {words}",
                            Say($"{{ 'do': 'damage', 'target': 'enemy', 'power': 100, 'if': {condition} }}"));
        }

        // ---- how pieces are joined ---------------------------------------------------------------------------------

        [Test]
        public void ADamagePieceWithAConditionRightAfterDamageToTheSameTarget_SaysMore()
        {
            Assert.AreEqual("Test: 100 % damage to one enemy, then 50 % more if it has Defense Down",
                            Say("{ 'do': 'damage', 'target': 'enemy', 'power': 100 }",
                                "{ 'do': 'damage', 'target': 'enemy', 'power': 50, 'if': { 'test': 'target_has_effect', 'effect': 'defense_down' } }"));
        }

        [Test]
        public void ADamagePieceWithAConditionAfterDamageToAnotherTarget_SaysItInFull()
        {
            Assert.AreEqual("Test: 100 % damage to one enemy, then 50 % damage to all enemies if it has Defense Down",
                            Say("{ 'do': 'damage', 'target': 'enemy', 'power': 100 }",
                                "{ 'do': 'damage', 'target': 'all_enemies', 'power': 50, 'if': { 'test': 'target_has_effect', 'effect': 'defense_down' } }"));
        }

        [Test]
        public void ADamagePieceWithoutACondition_IsNeverShortenedToMore()
        {
            Assert.AreEqual("Test: 120 % damage to one enemy and their neighbours, then 230 % damage to that enemy",
                            Say("{ 'do': 'damage', 'target': 'enemy_and_neighbours', 'power': 120 }",
                                "{ 'do': 'damage', 'target': 'enemy', 'power': 230 }"));
        }

        [Test]
        public void ThatEnemy_OnlyWhenAnEarlierPieceNamedTheChosenEnemy()
        {
            Assert.AreEqual("Test: 100 % damage to all enemies, then Stun on one enemy",
                            Say("{ 'do': 'damage', 'target': 'all_enemies', 'power': 100 }", "{ 'do': 'effect', 'target': 'enemy', 'effect': 'stun' }"),
                            "all enemies are not a chosen enemy");
        }

        [Test]
        public void ThatAlly_WhenAnEarlierPieceNamedTheChosenAlly()
        {
            Assert.AreEqual("Patch Job: heal one ally for 20 % of max Health, then clear 1 bad effect from that ally",
                            SkillText.Sentence(data.Heroes["doc_nefer"].Special));
        }

        // ---- the real heroes ---------------------------------------------------------------------------------------

        [TestCase("hori", "special", "Falcon's Sight: 150 % damage to the enemy who acts next (can't miss)")]
        [TestCase("hori", "ultimate", "EYE OF HORUS!: 6 hits of 70 % damage to one enemy (can't miss)")]
        [TestCase("skipjack", "ultimate", "SPECIAL DELIVERY!: 450 % damage to one enemy (always crits, reaches the back row)")]
        [TestCase("strix", "ultimate", "FROM ABOVE.: 380 % damage to one enemy (reaches the back row), then 200 % more if it has Defense Down")]
        [TestCase("kal", "special", "Overheat: 70 % damage to one enemy and their neighbours, then Offense Up on self for 1 turn")]
        [TestCase("halvard", "ultimate", "I GAVE MY WORD!: Barrier (25 % of max Health) on all allies for 2 turns, then Taunt on self for 1 turn")]
        [TestCase("thordis", "special", "Haul In: 130 % damage to one enemy (reaches the back row), then Stun on that enemy")]
        [TestCase("ibis", "special", "Threshold: 60 % damage to one enemy (reaches the back row), then drain the turn meter of that enemy by 50 %")]
        [TestCase("doc_nefer", "ultimate", "NOT ON MY TABLE!: heal all allies for 30 % of max Health, then clear all bad effects from all allies")]
        public void SomeRealSkills_ReadAsTheirKitIdeaDoes(string hero, string button, string expected)
        {
            var h = data.Heroes[hero];
            Assert.AreEqual(expected, SkillText.Sentence(button == "special" ? h.Special : h.Ultimate));
        }

        [Test]
        public void All24RealSkills_GiveASentenceWithNoRawDataName()
        {
            var skills = data.Heroes.Values.SelectMany(h => new[] { h.Basic, h.Special, h.Ultimate }).ToList();
            Assert.AreEqual(24, skills.Count, "set-up: eight heroes with three skills each");

            foreach (var skill in skills)
            {
                var sentence = SkillText.Sentence(skill);
                StringAssert.StartsWith(skill.Name + ": ", sentence);
                Assert.IsFalse(sentence.Contains("_"), $"'{sentence}' shows a raw data name (underscore)");

                // the data names themselves (an effect id, a trait id) never appear as written in the file
                foreach (var piece in skill.Pieces)
                {
                    foreach (var raw in new[] { piece.Effect, piece.If?.Effect, piece.If?.Trait, piece.If?.Class }.Where(r => r != null))
                        StringAssert.DoesNotContain(raw, sentence, $"'{sentence}' shows the data name '{raw}'");
                    StringAssert.DoesNotContain("null", sentence);
                }
            }
        }

        // ---- leader skills and passives (P2b-03; 02 GDD 17.10 item 3) -----------------------------------------------------

        static BonusSkillDef Bonus(string json) => JsonConvert.DeserializeObject<BonusSkillDef>(json);

        [Test]
        public void ABonus_ReadsAsAPercentOnAStat_ForWhoItIsFor()
        {
            Assert.AreEqual("+20 % Damage for every Tech ally",
                SkillText.LeaderSentence(Bonus("{ 'bonuses': [ { 'stat': 'damage', 'percent': 20, 'who': { 'trait': 'tech' } } ] }")));
            Assert.AreEqual("+20 % Crit Chance for every Hunter ally",
                SkillText.LeaderSentence(Bonus("{ 'bonuses': [ { 'stat': 'crit_chance', 'percent': 20, 'who': { 'class': 'hunter' } } ] }")));
            Assert.AreEqual("+10 % Speed for every ally",
                SkillText.LeaderSentence(Bonus("{ 'bonuses': [ { 'stat': 'speed', 'percent': 10 } ] }")));
            Assert.AreEqual("+12.5 % Armor for every Hero ally; +15 % Health for every Hero ally",
                SkillText.LeaderSentence(Bonus("{ 'bonuses': [ { 'stat': 'armor', 'percent': 12.5, 'who': { 'trait': 'hero' } }, { 'stat': 'health', 'percent': 15, 'who': { 'trait': 'hero' } } ] }")));
        }

        [Test]
        public void APassiveBonus_NeedsNoWho_AndSaysItsCondition()
        {
            Assert.AreEqual("+5 % Dodge Chance for each other ally that is Greek",
                SkillText.PassiveSentence(Bonus("{ 'bonuses': [ { 'stat': 'dodge_chance', 'percent': 5, 'if': { 'test': 'per_ally_with', 'trait': 'greek' } } ] }")));
            Assert.AreEqual("+100 % Crit Chance if it is this hero's first attack",
                SkillText.PassiveSentence(Bonus("{ 'bonuses': [ { 'stat': 'crit_chance', 'percent': 100, 'if': { 'test': 'first_attack' } } ] }")));
        }

        [Test]
        public void ATrigger_SaysWhen_AndWho_AndWhatItDoes()
        {
            Assert.AreEqual("At the start of the fight, every Greek ally: fill their own turn meter by 50 %",
                SkillText.LeaderSentence(Bonus("{ 'triggers': [ { 'when': 'fight_start', 'who': { 'trait': 'greek' }, 'pieces': [ { 'do': 'meter', 'target': 'self', 'percent': 50 } ] } ] }")));
            Assert.AreEqual("At the start of its turn: Counter on itself for 1 turn",
                SkillText.PassiveSentence(Bonus("{ 'triggers': [ { 'when': 'turn_start', 'pieces': [ { 'do': 'effect', 'target': 'self', 'effect': 'counter', 'turns': 1 } ] } ] }")));
            Assert.AreEqual("When an ally falls below half Health: Taunt on itself for 1 turn",
                SkillText.PassiveSentence(Bonus("{ 'triggers': [ { 'when': 'ally_below_half', 'pieces': [ { 'do': 'effect', 'target': 'self', 'effect': 'taunt', 'turns': 1 } ] } ] }")));
            Assert.AreEqual("Whenever its attack hits: Momentum on itself",
                SkillText.PassiveSentence(Bonus("{ 'triggers': [ { 'when': 'attack_hits', 'pieces': [ { 'do': 'effect', 'target': 'self', 'effect': 'momentum' } ] } ] }")));
        }

        [Test]
        public void ANamedBonusSkill_StartsWithItsName()
        {
            Assert.AreEqual("Crew: +10 % Damage for every ally",
                SkillText.LeaderSentence(Bonus("{ 'name': 'Crew', 'bonuses': [ { 'stat': 'damage', 'percent': 10 } ] }")));
        }

        [TestCase("skipjack", "At the start of the fight, every Greek ally: fill their own turn meter by 50 %", "+5 % Dodge Chance for each other ally that is Greek")]
        [TestCase("strix", "+20 % Crit Chance for every Hunter ally", "+100 % Crit Chance if it is this hero's first attack")]
        [TestCase("kal", "+20 % Damage for every Tech ally", "+20 % Damage for each other ally that is Tech")]
        [TestCase("halvard", "+15 % Health for every Hero ally; +15 % Armor for every Hero ally", "When an ally falls below half Health: Taunt on itself for 1 turn")]
        [TestCase("thordis", "+20 % Damage for every Outlaw ally", "Whenever its attack hits: Momentum on itself")]
        [TestCase("ibis", "+20 % Armor for every Sworn ally; At the start of the fight, every Sworn ally: Ward on themselves for 2 turns", "At the start of the fight: Ward on itself for 2 turns, then Ward on the most hurt ally for 2 turns")]
        [TestCase("hori", "+20 % Damage for every Egyptian ally", "At the start of its turn: Counter on itself for 1 turn")]
        [TestCase("doc_nefer", "At the start of its turn, every ally: heal themselves for 3 % of max Health", "+20 % Healing if it is Outlaw")]
        public void TheEightRealHeroes_LeaderSkillAndPassive_ReadAsTheirKitIdeaDoes(string hero, string leader, string passive)
        {
            var h = data.Heroes[hero];
            Assert.AreEqual(leader, SkillText.LeaderSentence(h.Leader));
            Assert.AreEqual(passive, SkillText.PassiveSentence(h.Passive));
        }

        [Test]
        public void All16RealLeaderSkillsAndPassives_GiveASentenceWithNoRawDataName()
        {
            var all = data.Heroes.Values.SelectMany(h => new[] { (h.Leader, true), (h.Passive, false) }).ToList();
            Assert.AreEqual(16, all.Count, "set-up: eight heroes, a leader skill and a passive each");

            foreach (var (def, leader) in all)
            {
                var sentence = leader ? SkillText.LeaderSentence(def) : SkillText.PassiveSentence(def);
                Assert.IsNotEmpty(sentence);
                Assert.IsFalse(sentence.Contains("_"), $"'{sentence}' shows a raw data name (underscore)");
                StringAssert.DoesNotContain("null", sentence);
                // the ids as the file writes them (lower case) never show: "dodge_chance", "greek", "ward"...
                var raw = (def.Bonuses ?? new BonusDef[0]).SelectMany(b => new[] { b.Stat, b.Who?.Trait, b.Who?.Class, b.If?.Trait, b.If?.Class })
                    .Concat((def.Triggers ?? new TriggerDef[0]).SelectMany(t => new[] { t.Who?.Trait, t.Who?.Class }
                        .Concat(t.Pieces.Select(p => p.Effect))))
                    .Where(r => r != null);
                foreach (var id in raw)
                    StringAssert.DoesNotContain(id, sentence, $"'{sentence}' shows the data name '{id}'");
            }
        }
    }
}
