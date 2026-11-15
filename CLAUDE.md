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
| `Assets/Data/` | Content as JSON: heroes, classes, traits. The code never names a hero, class or trait |
| `Assets/Scripts/Rules/` | The fight's logic as plain C#: no scene objects, nothing drawn, no waiting. Tested by EditMode tests |
| `Assets/Scripts/Screens/` | What you see and touch; asks `Rules` what happens and plays it back |
| `Assets/Scenes/` | Scenes (TeamSelect, Battle when they exist; `SampleScene` from the template until then) |
| `Assets/Art/` | Placeholder art and the cut-in template's pieces |
| `Assets/Tests/EditMode/` | Automated tests (assembly `HeroHQ.Tests.EditMode`) |
| `tools/` | `test.sh` |

Screen: **landscape only** (Player Settings); UI on a Canvas scaled from **1920 × 1080**, kept inside the phone's safe area; **uGUI**. Test on PC first (Game view set to a landscape phone), Android later.

## Test

The Unity editor must be **closed** (a project can be open only once).

```bash
tools/test.sh
```

Starts Unity with no window, runs every EditMode test, prints `N tests: P passed, F failed`, and exits 0 only if all pass. `tools/test.sh ProjectSetupTests` runs a subset. Results and log: `TestResults/` (git-ignored). The first run after a fresh clone is slow (Unity rebuilds `Library/`).

## Git

Own repository, **no remote** until the owner says. Commit only when the owner asks. Never commit `Library/`, `Temp/`, `Logs/`, `UserSettings/`, builds or test results (`.gitignore`).
