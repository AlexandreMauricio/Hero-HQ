# Hero HQ (Unity project)

A turn-based 5v5 hero-squad game for phones held sideways (landscape): Strike Force-style classes and traits, anime cut-in ultimates, a base as the upgrade menu. Working title **Hero HQ**. Original; offline first, online later.

## Start here

The design, decisions and plans live in the owner's Obsidian vault, not in this repo:

1. `C:\Users\guita\Documents\GameDevBrain\07-Projects\Hero HQ\Hero HQ — 04 Roadmap and QA.md`, section **"Start here"**: current state and next step.
2. Same folder: `Hero HQ — 01 Vision.md` (what the game is), `— 02 GDD.md` (rules and numbers, once written), `— 03 Architecture.md` (**how this project is built; it wins on structure**), `— Studio.md` (open threads, owner rulings).
3. The vault's own rules: `C:\Users\guita\Documents\GameDevBrain\CLAUDE.md`. The studio's rules: `_meta/Studio/Studio Manual.md` there.

Build only what an approved spec asks for. Labels in the vault: **OWNER** (approved) vs **PROPOSAL**; never build a proposal as if approved.

**The owner** has some programming experience and no game-development experience. Explain game-dev and Unity terms in plain language, put a plain-language walkthrough beside any code you show, and give click-by-click steps for anything done in the editor.

## Engine

**Unity 6000.3.28f1 LTS** (owner, 2026-11-15), at `C:\Program Files\Unity\Hub\Editor\6000.3.28f1\Editor\Unity.exe`. Made from Unity's 2D template (Universal 2D: URP, 2D packages). Don't change the Unity version, packages or project settings without the owner's OK.

## How the project is laid out (03 Architecture section 2)

| Folder | What |
|---|---|
| `Assets/Data/` | Content as JSON, six files: heroes, classes, traits, tuning, teams, formations (each formation's rows, which front slots cover each back slot, and where the boxes stand). The code never names a hero, class or trait |
| `Assets/Scripts/Rules/` | The fight's logic as plain C#: no scene objects, nothing drawn, no waiting. Tested by EditMode tests |
| `Assets/Scripts/Screens/` | What you see and touch; asks `Rules` what happens and plays it back. Assembly `HeroHQ.Screens` (uses `HeroHQ.Rules`, uGUI, the Input System). `BattleScreen` is the one script in the Battle scene; `BattleView` builds every box by code under one Canvas (1920 × 1080, safe area, the four zones of 02 GDD 17.3). `Editor/` (assembly `HeroHQ.Screens.Editor`, editor only) holds `BattleSceneBuilder`, which makes the Battle scene |
| `Assets/Scenes/` | `Battle.unity`, **made by `tools/make-scene.sh`, never edited by hand** (change `BattleSceneBuilder` and run it again); `SampleScene` from the template until it's replaced. The build's scene list is unchanged: open `Battle.unity` directly in the editor |
| `Assets/Art/` | Placeholder art and the cut-in template's pieces |
| `Assets/Tests/EditMode/` | Automated tests (assembly `HeroHQ.Tests.EditMode`) |
| `tools/` | `test.sh` (tests), `make-scene.sh` (remakes the Battle scene) |

Screen: **landscape only** (Player Settings); UI on a Canvas scaled from **1920 × 1080**, kept inside the phone's safe area; **uGUI**. Test on PC first (Game view set to a landscape phone), Android later.

## Test

The Unity editor must be **closed** (a project can be open only once).

```bash
tools/test.sh
```

Starts Unity with no window, runs every EditMode test, prints `N tests: P passed, F failed`, and exits 0 only if all pass. `tools/test.sh ProjectSetupTests` runs a subset. Results and log: `TestResults/` (git-ignored). The first run after a fresh clone is slow (Unity rebuilds `Library/`).

## Make the scene

The Unity editor must be **closed**.

```bash
tools/make-scene.sh
```

Runs `HeroHQ.Screens.Editor.BattleSceneBuilder.Build` with no window and writes `Assets/Scenes/Battle.unity`: a camera, an EventSystem on the Input System, and one `Battle` object holding `BattleScreen` with the six data files. Exits 0 only if the scene was written. Log: `TestResults/make-scene.log`. In an open editor the same builder is the menu **Hero HQ > Make the Battle scene**. Run it whenever the builder changes, then `tools/test.sh` (`BattleSceneTests` checks the scene file). Commit the scene with the change: each run gives its objects new ids, so the file shows a diff even when nothing else changed.

## Git

Own repository, **no remote** until the owner says. Commit only when the owner asks. **Standing permission (owner, 2026-11-16):** during phase 1, commit after each finished task of 03 Architecture 4.12 once `tools/test.sh` passes, one commit per task, named after the task (e.g. `P1-01: Rules assembly and data classes`). **Extended (owner, 2026-11-17):** the same during phase 2a, for each finished task of 03 Architecture section 7 (the Lead Gameplay Programmer's 2a task list). Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`, builds or test results (`.gitignore`).
