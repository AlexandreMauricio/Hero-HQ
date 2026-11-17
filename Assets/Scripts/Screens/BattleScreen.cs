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
    // P2a-09 hands your side to your taps.
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
            View = new BattleView();
            View.Build(transform);
            StartCoroutine(Run());
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

                    var actor = fight.Acting;
                    var choice = EnemyAI.Choose(fight);                 // both sides by the computer until P2a-09
                    if (actor.Side == Side.Enemy) View.ShowEnemyAction(actor, choice);   // banner and a red outline on the target (17.7)
                    yield return new WaitForSeconds(beatSeconds);       // one beat to see who acts (and, for an enemy, whom it aims at)
                    View.ClearAim();

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

        // Shows a batch of events one at a time (P2a-08). BattleView.ShowEvent takes each event into Playback, draws it
        // and says how long to wait: a beat for what shows over a hero, 0.3 s for an ultimate's off look, 0.8 s for a
        // skipped turn's banner, nothing for bookkeeping. At the end the effect chips and the Barrier are redrawn from
        // the fighters. A turn counts when it starts, a skipped one too (as the fight report counts them, 4.10).
        IEnumerator Play(IReadOnlyList<FightEvent> batch)
        {
            turns += batch.Count(e => e.Kind == FightEventKind.TurnStarted);
            foreach (var e in batch)
            {
                var hold = View.ShowEvent(e, playback, beatSeconds);
                if (hold > 0) yield return new WaitForSeconds(hold);
            }
            View.SyncAfterAction(fight, playback);
        }
    }
}
