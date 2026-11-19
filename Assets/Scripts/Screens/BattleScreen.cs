using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HeroHQ.Rules;
using HeroHQ.Rules.Data;
using UnityEngine;

namespace HeroHQ.Screens
{
    // The one script in the Battle scene (03 Architecture 7.2). BattleSceneBuilder fills in the six data files;
    // nothing here is set by hand in the editor. P2a-07: the fight plays itself, the computer choosing for both sides.
    // P2a-08: each action is played event by event, with beats, numbers, the banner and the off look.
    // P2a-09: your side waits for your taps (arm, then commit); the enemy side is still chosen by EnemyAI.
    // P2b-01: an ultimate plays the real cut-in (CutIn) and the hits wait for it; "seen it" is kept per hero id.
    // P2b-03: a hold on a figure or a turn-bar entry opens the hero card; a hold on a button reads its sentence.
    // P2b-04: a tap on a hero you can't pick shakes it; the view slides the buttons in on your choice and out on your commit.
    public class BattleScreen : MonoBehaviour
    {
        [Header("The six data files (set by BattleSceneBuilder)")]
        public TextAsset heroes;
        public TextAsset classes;
        public TextAsset traits;
        public TextAsset tuning;
        public TextAsset teams;
        public TextAsset formations;

        [Tooltip("How long one beat lasts, in seconds (02 GDD 17.6). Change it while playing to feel the pace.")]
        public float beatSeconds = 0.4f;

        [Tooltip("The cut-in pictures in Assets/Art/CutIns (set by BattleSceneBuilder); heroes.json names them.")]
        public Sprite[] cutInArt;

        /// <summary>The cut-in setting (02 GDD 17.8): the default until P2b-05's settings panel can change it.</summary>
        public CutInSetting CutInSetting { get; set; } = CutInSetting.FirstTimeFullThenShort;

        // Phase 2a's fixed teams (04 Phases, OWNER 2026-11-17): the Greek strike against the crew. Team select is 2b.
        const string PlayerTeam = "greek_strike", EnemyTeam = "enemy_crew";

        public GameData Data { get; private set; }
        public BattleView View { get; private set; }

        Fight fight;
        Playback playback;
        int turns;
        bool newFightTapped;

        void Start()
        {
            // GameData.Load collects every problem in the files and throws once with the whole list,
            // which Unity shows in the Console.
            Data = GameData.Load(heroes.text, classes.text, traits.text, tuning.text, teams.text, formations.text);
            BuildView();
            StartCoroutine(Run());
        }

        /// <summary>Builds the screen's boxes and hands the view's taps to <see cref="TapHero"/>, <see cref="TapEmpty"/>
        /// and <see cref="TapButton"/>. Public so a test can build it without playing the scene.</summary>
        public void BuildView()
        {
            View = new BattleView();
            View.Build(transform);
            View.HeroTapped = TapHero;
            View.EmptyTapped = TapEmpty;
            View.ButtonTapped = TapButton;
            View.HeroHeld = HoldHero;
            View.ButtonHeld = HoldButton;
            View.HoldEnded = EndHold;
        }

        void Update()
        {
            // The phone turns between its two landscape sides, which moves the notch: keep the boxes clear of it.
            View?.FitSafeArea();
        }

        // The loop of 03 Architecture 7.2 (4.3 with the waiting added). A coroutine is a method Unity runs a piece
        // at a time: at each `yield` it stops and carries on from there a later frame, so the game keeps drawing
        // while the fight waits (see the Glossary).
        IEnumerator Run()
        {
            var players = Data.Teams[PlayerTeam];
            var enemies = Data.Teams[EnemyTeam];
            var barLength = (int)Data.Tuning["turn_bar_length"];
            while (true)
            {
                // A new seed every fight, printed in the corner and the Console so a strange fight can be replayed.
                var seed = Random.Range(1, 1000000);
                Debug.Log($"Battle: {PlayerTeam} against {EnemyTeam}, seed {seed}");
                fight = Fight.Create(Data, players, enemies, seed);
                PlayerLeaderId = players.Leader;
                EnemyLeaderId = enemies.Leader;
                playback = new Playback(fight);
                turns = 0;
                View.ShowFight(fight, Data.Formations[players.Formation], Data.Formations[enemies.Formation]);
                View.ShowSeed(seed);
                yield return StartCoroutine(Play(fight.StartEvents));   // the fight-start leader skills and passives

                while (fight.Outcome == Outcome.Going)
                {
                    yield return StartCoroutine(Play(fight.AdvanceToChoice()));   // turn starts; a stunned turn is skipped here
                    if (fight.Acting == null) break;                    // nobody left to choose: the fight is over
                    View.ShowBar(fight.PredictTurns(barLength));        // NOW first
                    View.ShowActing(fight.Acting);

                    Choice choice;
                    if (fight.Acting.Side == Side.Player)
                    {
                        BeginYourTurn(fight);                           // P2a-09: your side waits for your taps
                        yield return new WaitUntil(() => Committed.HasValue);
                        choice = Committed.Value;
                    }
                    else
                    {
                        choice = EnemyAI.Choose(fight);
                        View.ShowEnemyAction(fight.Acting, choice);     // banner and a red outline on the target (17.7)
                        yield return new WaitForSeconds(beatSeconds);   // one beat to see whom it aims at
                        View.ClearAim();
                    }

                    yield return StartCoroutine(Play(fight.Act(choice)));
                    View.ShowActing(null);
                }

                View.ShowBar(fight.PredictTurns(barLength));
                Debug.Log($"Battle: {fight.Outcome} after {turns} turns (seed {seed})");
                newFightTapped = false;
                View.ShowEnd(fight.Outcome, turns, () => newFightTapped = true);
                yield return new WaitUntil(() => newFightTapped);      // a tap starts a new fight in place
            }
        }

        // Shows a batch of events one at a time (P2a-08). Show takes each event into Playback, draws it and says how
        // long to wait: a beat for what shows over a hero, 0.8 s for a skipped turn's banner, nothing for bookkeeping;
        // an ultimate waits for its cut-in to hand the screen back (P2b-01), or 0.3 s for the off look. At the end the
        // effect chips and the Barrier are redrawn from the fighters. A turn counts when it starts, a skipped one too
        // (as the fight report counts them, 4.10).
        IEnumerator Play(IReadOnlyList<FightEvent> batch)
        {
            turns += batch.Count(e => e.Kind == FightEventKind.TurnStarted);
            foreach (var e in batch)
            {
                var hold = Show(fight, playback, e, out var cutIn);
                if (cutIn != null) yield return new WaitUntil(() => cutIn == null || cutIn.BattleShows);
                else if (hold > 0) yield return new WaitForSeconds(hold);
            }
            View.SyncAfterAction(fight, playback);
        }

        // ---- the cut-in (P2b-01; 03 Architecture 8.1 A1, 8.5; 02 GDD 17.7) -------------------------------------------

        /// <summary>
        /// Shows one event and says how many seconds to wait. An ultimate's use starts the cut-in instead, when the
        /// setting and the "seen" flags give full or short (<paramref name="cutIn"/> is then set: wait until it hands the
        /// screen back, acceptance 3); with "off" it is the 0.3 s off look as in 2a. Every other event is
        /// <see cref="BattleView.ShowEvent"/>'s. Public so a test can play events without the coroutine.
        /// </summary>
        public float Show(Fight fight, Playback playback, FightEvent e, out CutIn cutIn)
        {
            cutIn = null;
            if (e.Kind == FightEventKind.SkillUsed && e.Button == HeroHQ.Rules.Button.Ultimate)
            {
                var hero = e.Who.Hero;
                var version = CutInTiming.Pick(CutInSetting, HasSeen(hero.Id));
                if (version != CutInVersion.Off)
                {
                    if (version == CutInVersion.Full) MarkSeen(hero.Id);   // reading R30: seen when the full one starts
                    playback.Apply(e);
                    var colours = Colours(fight.Data, hero);
                    cutIn = CutIn.Show(View.Canvas.transform, e.Who, version, colours.background, colours.glow,
                                       Art(hero.Cutin.Drawing), Art(hero.Cutin.Behind));
                    return 0;
                }
            }
            return View.ShowEvent(e, playback, beatSeconds);
        }

        /// <summary>The picture <paramref name="name"/> names (heroes.json's <c>drawing</c> or <c>behind</c>), or null.</summary>
        public Sprite Art(string name) =>
            name == null || cutInArt == null ? null : cutInArt.FirstOrDefault(s => s != null && s.name == name);

        /// <summary>The hero's pantheon trait's cut-in colours (03 Architecture 3.3).</summary>
        public static (Color background, Color glow) Colours(GameData data, HeroDef hero)
        {
            var colours = hero.Traits.Select(t => data.Traits[t]).First(t => t.Kind == "pantheon").CutinColors;
            return (Hex(colours.Background), Hex(colours.Glow));
        }

        static Color Hex(string hex) =>
            ColorUtility.TryParseHtmlString(hex, out var colour) ? colour : throw new System.FormatException($"not a colour: {hex}");

        /// <summary>
        /// "Seen it" per hero id, both sides (02 GDD 17.7), kept between runs in PlayerPrefs (a small built-in store of
        /// named values that survives closing the game; 03 Architecture 8.5). The key is this prefix and the hero's id.
        /// </summary>
        public const string SeenKeyPrefix = "cutin_seen_";

        public static bool HasSeen(string heroId) => PlayerPrefs.GetInt(SeenKeyPrefix + heroId, 0) == 1;

        public static void MarkSeen(string heroId)
        {
            if (HasSeen(heroId)) return;
            PlayerPrefs.SetInt(SeenKeyPrefix + heroId, 1);
            PlayerPrefs.Save();
        }

        // ---- holds (P2b-03; 02 GDD 17.1, 17.4, 17.5; 03 Architecture 8.1 A3) ------------------------------------------
        //
        // A finger held 0.4 s (Hold) opens the hero card, or reads a button's sentence; lifting closes it. A hold never
        // arms, commits or re-aims: these methods touch no tap state, and the click that ends a hold is dropped by Hold.

        /// <summary>The hero ids of the two named leaders (teams.json), set when a fight starts; the card shows the leader skill only for them.</summary>
        public string PlayerLeaderId { get; set; }
        public string EnemyLeaderId { get; set; }

        /// <summary>The hero whose card is open, or null.</summary>
        public Fighter CardOf { get; private set; }

        /// <summary>
        /// A hold on a hero's figure, or on one of their turn-bar entries (<paramref name="fromBar"/>): their card opens;
        /// from the bar, every entry of theirs lights and their figure gets the bright ring (17.4). Public so a test can hold.
        /// </summary>
        public void HoldHero(Fighter hero, bool fromBar)
        {
            var data = View.Fight?.Data ?? Data;
            if (data == null) return;
            var leader = hero.Hero.Id == (hero.Side == Side.Player ? PlayerLeaderId : EnemyLeaderId);
            CardOf = hero;
            View.ShowCard(HeroCard.Of(data, hero, leader));
            if (fromBar) View.ShowSpot(hero);
        }

        /// <summary>
        /// A hold on a skill button (17.5): the info strip shows that skill's sentence, even for a button that is grey
        /// (what will my ultimate do?). Nothing is armed. Only while your hero is choosing, when there are skills to read.
        /// </summary>
        public void HoldButton(Button button)
        {
            if (!Choosing) return;
            View.ShowInfo(SkillText.Sentence(fight.Options[(int)button].Skill));
        }

        /// <summary>The finger lifted: the card closes, the lights go out, and the info strip says what it said before.</summary>
        public void EndHold()
        {
            CardOf = null;
            View.HideHold();
            if (Choosing) ShowChoosing();
        }

        // ---- your turn (P2a-09; 02 GDD 17.5: arm, then commit) -----------------------------------------------------
        //
        // Plain methods, so a test can tap without a mouse. The rules decide who may be chosen (Fight.Options), what a
        // skill will touch (SkillResolver.WillTouch) and who is covered (Targeting.Covered); these methods only keep
        // which button is armed and which hero is aimed at, and say when the choice is made.

        /// <summary>The armed button, or null.</summary>
        public Button? Armed { get; private set; }
        /// <summary>The hero an area skill is aimed at (its first tap), or null.</summary>
        public Fighter Aimed { get; private set; }
        /// <summary>Set by the committing tap; the fight loop then plays it. Null while you are still choosing.</summary>
        public Choice? Committed { get; private set; }
        /// <summary>Why the last tap did nothing ("Front row first."), or null; shown in the info strip until the next tap.</summary>
        public string Reason { get; private set; }

        public const string FrontRowFirst = "Front row first.";

        /// <summary>Your hero is choosing: nothing armed, the buttons unlocked. Public so a test can start a turn.</summary>
        public void BeginYourTurn(Fight fight)
        {
            if (fight.Acting == null || fight.Acting.Side != Side.Player)
                throw new System.InvalidOperationException("Only your own hero's turn waits for taps.");
            this.fight = fight;
            Armed = null;
            Aimed = null;
            Committed = null;
            Reason = null;
            ShowChoosing();
        }

        // Taps count only while your hero is choosing: on an enemy's turn and while it plays back the buttons are hidden (17.3.1).
        bool Choosing => fight != null && fight.Acting != null && fight.Acting.Side == Side.Player && !Committed.HasValue;

        /// <summary>
        /// A button: an unusable one does nothing (its grey look and number say why); another button arms it; the armed
        /// one again commits a skill with no choice, or an area skill already aimed, and otherwise takes it back.
        /// </summary>
        public void TapButton(Button button)
        {
            if (!Choosing) return;
            var option = fight.Options[(int)button];
            Reason = null;
            if (Armed == button)
            {
                if (!option.NeedsChoice) { Commit(new Choice(button, null)); return; }
                if (Aimed != null) { Commit(new Choice(button, Aimed)); return; }
                Armed = null;
            }
            else if (option.Usable)
            {
                Armed = button;
                Aimed = null;
            }
            ShowChoosing();
        }

        /// <summary>
        /// A hero: with a one-hero skill armed, a hero you may choose commits at once; with an area skill, the first tap
        /// aims (the heroes it will hit are outlined), the same hero again commits, another re-aims. A hero you can't
        /// choose commits nothing and the info strip says why. With nothing armed, nothing happens. A skill that needs
        /// no choice commits on a tap on any hero it will touch (they are outlined); another hero does nothing.
        /// </summary>
        public void TapHero(Fighter hero)
        {
            if (!Choosing || !Armed.HasValue) return;
            var option = fight.Options[(int)Armed.Value];
            if (!option.NeedsChoice)
            {
                // No choice (17.5, OWNER 2026-11-18): a tap on any outlined hero fires it; any other hero does nothing.
                if (SkillResolver.WillTouch(fight, fight.Acting, option.Button, null).Contains(hero))
                    Commit(new Choice(Armed.Value, null));
                return;
            }
            if (option.Choosable.Contains(hero))
            {
                Reason = null;
                if (!AimsFirst(option) || Aimed == hero)
                {
                    Commit(new Choice(Armed.Value, hero));
                    return;
                }
                Aimed = hero;
            }
            else
            {
                Reason = WhyNot(option, hero);
                if (!hero.KnockedOut) View.Figures[hero].Shake();   // 17.3.1 Q4: a hero you can't pick shakes and nothing else happens
            }
            ShowChoosing();
        }

        /// <summary>The field where no hero stands: takes the armed skill back.</summary>
        public void TapEmpty()
        {
            if (!Choosing) return;
            Armed = null;
            Aimed = null;
            Reason = null;
            ShowChoosing();
        }

        // 17.5's middle shape: a skill that hits the chosen hero and their neighbours aims on the first tap.
        static bool AimsFirst(ButtonOption option) => option.Skill.Pieces.Any(p => p.Target == "enemy_and_neighbours");

        // The reason for a hero you can't choose, on the side the skill picks from: Taunt first (it wins over the lane
        // rule, as in Targeting.Choosable), then a covered back-row hero. Anything else: no reason.
        string WhyNot(ButtonOption option, Fighter hero)
        {
            if (hero.KnockedOut || option.Choosable.Count == 0 || hero.Side != option.Choosable[0].Side) return null;
            var taunting = option.Choosable.Where(f => f.Effects.Any(e => e.Kind == "taunt")).Select(f => f.Hero.Name).ToList();
            if (taunting.Count > 0)
                return $"{string.Join(" and ", taunting)} {(taunting.Count == 1 ? "has" : "have")} Taunt: pick them.";
            return Targeting.Covered(fight, hero) ? FrontRowFirst : null;
        }

        void Commit(Choice choice)
        {
            Committed = choice;
            Armed = null;
            Aimed = null;
            Reason = null;
            View.ClearChoosing();   // the buttons slide out while what happened plays back
        }

        // Redraws the buttons, the info strip and every hero's glow, dim, outline and hit chip from the rules.
        void ShowChoosing()
        {
            var actor = fight.Acting;
            var options = fight.Options;
            // The Special's turns left and the Ultimate's charge are read from the acting hero at a choice, between two
            // played-back batches, where they equal what Playback counted (P2a-05's 60-fight test): not mid-playback.
            View.ShowButtons(options, Armed, actor.Cooldown, actor.Charge);
            var option = Armed.HasValue ? options[(int)Armed.Value] : null;
            View.ShowInfo(Reason ?? (option == null ? "" : SkillText.Sentence(option.Skill)));

            var touched = option == null ? new List<Fighter>()
                        : !option.NeedsChoice ? SkillResolver.WillTouch(fight, actor, option.Button, null).ToList()
                        : Aimed != null ? SkillResolver.WillTouch(fight, actor, option.Button, Aimed).ToList()
                        : new List<Fighter>();
            // Dimming is on the side the skill picks from only; skills that need no choice never dim (17.5).
            Side? pickSide = option != null && option.NeedsChoice && option.Choosable.Count > 0 ? option.Choosable[0].Side : (Side?)null;
            var cantMiss = option != null && option.Skill.Pieces.Any(p => p.CantMiss);
            foreach (var pair in View.Figures)
            {
                var hero = pair.Key;
                var glow = option != null && option.Choosable.Contains(hero);
                var aimed = touched.Contains(hero);
                // The hit chip (OWNER 2026-11-17/18): on the heroes you may tap for a one-hero skill, on the outlined ones
                // otherwise; on every enemy the skill can hit, "Hit 100 %" included. None for heals and buffs (Options gives
                // them no miss chance) and none for a "can't miss" skill.
                var chipped = option != null && !cantMiss && (option.NeedsChoice && !AimsFirst(option) ? glow : aimed);
                double? hit = chipped && option.MissChance.TryGetValue(hero, out var miss) ? 100 - miss : (double?)null;
                pair.Value.ShowChoosing(glow, pickSide == hero.Side && !glow, aimed, hit);
            }
        }
    }
}
