using System;
using System.IO;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using HeroHQ.Screens;
using Newtonsoft.Json;
using NUnit.Framework;
using UnityEngine;

namespace HeroHQ.Tests
{
    // P2b-02 (03 Architecture 8.1 A2, decision D2): EffectLook is the screen's one table of effect icons and of the
    // symbols and target mark a skill button shows (02 GDD 17.5, 17.6). Plain C#, so these tests need no screen.
    public class EffectLookTests
    {
        [Test]
        public void EveryOneOfTheTenEffects_HasAnEntry_SoANewEffectFailsHereUntilItHasAnIcon()
        {
            Assert.AreEqual(10, Effects.Kinds.Count, "02 GDD 7.4's ten effects");
            foreach (var kind in Effects.Kinds)
            {
                var look = EffectLook.For(kind);
                Assert.IsFalse(string.IsNullOrWhiteSpace(look.Mark), $"{kind}: a mark");
                Assert.AreEqual(Effects.IsBad(kind), look.Bad, $"{kind}: buff or bad is Effects.IsBad, never written twice");
            }
            var marks = Effects.Kinds.Select(k => EffectLook.For(k).Mark).ToList();
            CollectionAssert.AllItemsAreUnique(marks, "no two effects share a mark");
        }

        [Test]
        public void AnEffectNobodyGaveAnIcon_Throws()
        {
            Assert.Throws<ArgumentException>(() => EffectLook.For("teleport"));
            Assert.Throws<ArgumentException>(() => EffectLook.For(null));
        }

        static SkillDef Skill(params string[] pieces) =>
            JsonConvert.DeserializeObject<SkillDef>("{ \"name\": \"X\", \"pieces\": [" + string.Join(",", pieces) + "] }");

        const string Hit = "{ \"do\": \"damage\", \"target\": \"enemy\", \"power\": 100 }";

        [TestCase("{ \"do\": \"damage\", \"target\": \"enemy\", \"power\": 100 }", EffectLook.Symbol.Damage)]
        [TestCase("{ \"do\": \"heal\", \"target\": \"ally\", \"percent\": 20 }", EffectLook.Symbol.Heal)]
        [TestCase("{ \"do\": \"meter\", \"target\": \"next_enemy\", \"percent\": -30 }", EffectLook.Symbol.Meter)]
        [TestCase("{ \"do\": \"effect\", \"target\": \"self\", \"effect\": \"offense_up\", \"turns\": 2 }", EffectLook.Symbol.Buff)]
        [TestCase("{ \"do\": \"effect\", \"target\": \"enemy\", \"effect\": \"stun\" }", EffectLook.Symbol.BadEffect)]
        [TestCase("{ \"do\": \"effect\", \"target\": \"enemy\", \"effect\": \"defense_down\", \"turns\": 2 }", EffectLook.Symbol.BadEffect)]
        [TestCase("{ \"do\": \"clear\", \"target\": \"ally\" }", EffectLook.Symbol.Buff)]
        public void EachPieceKind_HasItsSymbol(string piece, EffectLook.Symbol expected) =>
            CollectionAssert.AreEqual(new[] { expected }, EffectLook.Symbols(Skill(piece)));

        [Test]
        public void ASkillShowsOneSymbolPerKindOfPiece_InTheOrderTheyFirstAppear()
        {
            var twoHitsAndABuff = Skill(Hit, Hit, "{ \"do\": \"effect\", \"target\": \"self\", \"effect\": \"defense_up\", \"turns\": 1 }", Hit);
            CollectionAssert.AreEqual(new[] { EffectLook.Symbol.Damage, EffectLook.Symbol.Buff }, EffectLook.Symbols(twoHitsAndABuff));

            var everything = Skill("{ \"do\": \"meter\", \"target\": \"all_enemies\", \"percent\": -10 }", Hit,
                                   "{ \"do\": \"effect\", \"target\": \"enemy\", \"effect\": \"stun\" }",
                                   "{ \"do\": \"heal\", \"target\": \"self\", \"percent\": 10 }",
                                   "{ \"do\": \"effect\", \"target\": \"self\", \"effect\": \"ward\", \"turns\": 2 }");
            CollectionAssert.AreEqual(new[]
            {
                EffectLook.Symbol.Meter, EffectLook.Symbol.Damage, EffectLook.Symbol.BadEffect, EffectLook.Symbol.Heal, EffectLook.Symbol.Buff,
            }, EffectLook.Symbols(everything));
        }

        [Test]
        public void EverySymbol_HasAMark_AndDamageAndBadEffectsAreHarsh()
        {
            foreach (EffectLook.Symbol symbol in Enum.GetValues(typeof(EffectLook.Symbol)))
                Assert.IsFalse(string.IsNullOrWhiteSpace(EffectLook.Mark(symbol)), symbol.ToString());
            Assert.IsTrue(EffectLook.IsHarsh(EffectLook.Symbol.Damage));
            Assert.IsTrue(EffectLook.IsHarsh(EffectLook.Symbol.BadEffect));
            Assert.IsFalse(EffectLook.IsHarsh(EffectLook.Symbol.Heal));
            Assert.IsFalse(EffectLook.IsHarsh(EffectLook.Symbol.Buff));
            Assert.IsFalse(EffectLook.IsHarsh(EffectLook.Symbol.Meter));
        }

        [TestCase("enemy", EffectLook.Reach.One, 1)]
        [TestCase("ally", EffectLook.Reach.One, 1)]
        [TestCase("most_hurt_ally", EffectLook.Reach.One, 1)]
        [TestCase("next_enemy", EffectLook.Reach.One, 1)]
        [TestCase("enemy_and_neighbours", EffectLook.Reach.Three, 3)]
        [TestCase("all_enemies", EffectLook.Reach.All, 5)]
        [TestCase("all_allies", EffectLook.Reach.All, 5)]
        [TestCase("self", EffectLook.Reach.Self, 1)]
        public void EveryTarget_HasItsMark_OneDotThreeDotsAllDotsOrSelf(string target, EffectLook.Reach reach, int dots)
        {
            var skill = Skill("{ \"do\": \"heal\", \"target\": \"" + target + "\", \"percent\": 10 }");
            Assert.AreEqual(reach, EffectLook.ReachOf(skill));
            Assert.AreEqual(dots, EffectLook.Dots(reach));
        }

        [Test]
        public void TheWidestTargetWins_SoDamageOneEnemyAndABuffOnSelfIsOneDot()
        {
            var skill = Skill(Hit, "{ \"do\": \"effect\", \"target\": \"self\", \"effect\": \"offense_up\", \"turns\": 2 }");
            Assert.AreEqual(EffectLook.Reach.One, EffectLook.ReachOf(skill));
            var wide = Skill(Hit, "{ \"do\": \"heal\", \"target\": \"all_allies\", \"percent\": 5 }");
            Assert.AreEqual(EffectLook.Reach.All, EffectLook.ReachOf(wide));
        }

        [Test]
        public void EveryRealSkill_HasSymbolsAndATargetMark()
        {
            string Read(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "Data", file));
            var data = GameData.Load(Read("heroes.json"), Read("classes.json"), Read("traits.json"),
                                     Read("tuning.json"), Read("teams.json"), Read("formations.json"));
            foreach (var hero in data.Heroes.Values)
                foreach (var skill in new[] { hero.Basic, hero.Special, hero.Ultimate })
                {
                    Assert.DoesNotThrow(() => EffectLook.Symbols(skill), $"{hero.Id} {skill.Name}");
                    Assert.IsNotEmpty(EffectLook.Symbols(skill), $"{hero.Id} {skill.Name}");
                    Assert.DoesNotThrow(() => EffectLook.ReachOf(skill), $"{hero.Id} {skill.Name}");
                    Assert.LessOrEqual(EffectLook.Symbols(skill).Count, 4, $"{hero.Id} {skill.Name}: the symbols row holds four");
                }
        }
    }
}
