using System.Collections.Generic;
using Newtonsoft.Json;

namespace HeroHQ.Rules.Data
{
    // Plain classes that mirror the game's JSON files (03 Architecture 3.2, 4.1).
    // Read-only once loaded: every property has a private setter, which Newtonsoft fills in.
    //
    // Words left out of the data read as null (numbers are nullable on purpose): that is how the loader
    // (GameData, P1-02) can tell "Armor left out by mistake" from "Armor is 0". The flags default to false.
    // Words are kept as the text in the file (ids, "damage", "fight_start"…): GameData checks them against the
    // lists in 03 Architecture 3.4, so the code never names a hero, class or trait.

    /// <summary>One hero (heroes.json).</summary>
    public class HeroDef
    {
        [JsonProperty("id")] public string Id { get; private set; }
        [JsonProperty("name")] public string Name { get; private set; }
        /// <summary>Id of a class in classes.json.</summary>
        [JsonProperty("class")] public string Class { get; private set; }
        /// <summary>Ids of traits in traits.json (one side, one pantheon, one origin).</summary>
        [JsonProperty("traits")] public IReadOnlyList<string> Traits { get; private set; }
        /// <summary>Stat name → value, for the twelve attributes. A stat left out is not in the list.</summary>
        [JsonProperty("stats")] public IReadOnlyDictionary<string, double> Stats { get; private set; }
        [JsonProperty("basic")] public SkillDef Basic { get; private set; }
        [JsonProperty("special")] public SkillDef Special { get; private set; }
        [JsonProperty("ultimate")] public SkillDef Ultimate { get; private set; }
        [JsonProperty("leader")] public BonusSkillDef Leader { get; private set; }
        [JsonProperty("passive")] public BonusSkillDef Passive { get; private set; }
        [JsonProperty("cutin")] public CutinDef Cutin { get; private set; }
    }

    /// <summary>A button: a name and a list of pieces done in order (02 GDD 7).</summary>
    public class SkillDef
    {
        [JsonProperty("name")] public string Name { get; private set; }
        /// <summary>The hero's own turns before the Special can be used again; null on Basic and Ultimate.</summary>
        [JsonProperty("cooldown")] public int? Cooldown { get; private set; }
        [JsonProperty("pieces")] public IReadOnlyList<PieceDef> Pieces { get; private set; }
    }

    /// <summary>One piece of a skill or trigger (02 GDD 7.1). Which settings it needs depends on <see cref="Do"/>.</summary>
    public class PieceDef
    {
        /// <summary>damage, heal, effect, clear or meter.</summary>
        [JsonProperty("do")] public string Do { get; private set; }
        /// <summary>One of the eight targets of 02 GDD 7.2.</summary>
        [JsonProperty("target")] public string Target { get; private set; }
        /// <summary>Damage: % of the hero's Damage stat.</summary>
        [JsonProperty("power")] public double? Power { get; private set; }
        /// <summary>Damage: number of hits.</summary>
        [JsonProperty("hits")] public int? Hits { get; private set; }
        /// <summary>Heal: % of max Health. Meter: % of a full bar, negative drains. Barrier effect: its size, % of the holder's max Health.</summary>
        [JsonProperty("percent")] public double? Percent { get; private set; }
        /// <summary>Effect: which effect.</summary>
        [JsonProperty("effect")] public string Effect { get; private set; }
        /// <summary>Effect: how many turns.</summary>
        [JsonProperty("turns")] public int? Turns { get; private set; }
        /// <summary>Clear: how many bad effects.</summary>
        [JsonProperty("count")] public int? Count { get; private set; }
        [JsonProperty("cant_miss")] public bool CantMiss { get; private set; }
        [JsonProperty("always_crits")] public bool AlwaysCrits { get; private set; }
        [JsonProperty("reaches_back_row")] public bool ReachesBackRow { get; private set; }
        /// <summary>At most one condition; null = always.</summary>
        [JsonProperty("if")] public ConditionDef If { get; private set; }
    }

    /// <summary>One of the six conditions of 02 GDD 7.3, on a piece or a bonus.</summary>
    public class ConditionDef
    {
        /// <summary>allies_with, per_ally_with, target_has_effect, target_has, target_not_acted or first_attack.</summary>
        [JsonProperty("test")] public string Test { get; private set; }
        /// <summary>allies_with: how many allies are needed.</summary>
        [JsonProperty("count")] public int? Count { get; private set; }
        [JsonProperty("trait")] public string Trait { get; private set; }
        [JsonProperty("class")] public string Class { get; private set; }
        [JsonProperty("effect")] public string Effect { get; private set; }
    }

    /// <summary>Which heroes a bonus or trigger is for: a trait, a class, or (both null) everyone.</summary>
    public class WhoDef
    {
        [JsonProperty("trait")] public string Trait { get; private set; }
        [JsonProperty("class")] public string Class { get; private set; }
    }

    /// <summary>A % on one stat (02 GDD 8). <see cref="Stat"/> may also be "healing".</summary>
    public class BonusDef
    {
        [JsonProperty("stat")] public string Stat { get; private set; }
        [JsonProperty("percent")] public double? Percent { get; private set; }
        /// <summary>Leader skills: who receives it. Null on a passive (the hero alone).</summary>
        [JsonProperty("who")] public WhoDef Who { get; private set; }
        [JsonProperty("if")] public ConditionDef If { get; private set; }
    }

    /// <summary>Pieces that fire at a set moment (02 GDD 8).</summary>
    public class TriggerDef
    {
        /// <summary>fight_start, turn_start, attack_hits or ally_below_half.</summary>
        [JsonProperty("when")] public string When { get; private set; }
        /// <summary>Leader skills: runs on each matching ally as that ally's own. Null on a passive.</summary>
        [JsonProperty("who")] public WhoDef Who { get; private set; }
        [JsonProperty("pieces")] public IReadOnlyList<PieceDef> Pieces { get; private set; }
    }

    /// <summary>A leader skill or a passive: both are built the same way (02 GDD 8).</summary>
    public class BonusSkillDef
    {
        /// <summary>Optional (03 Architecture 3.2).</summary>
        [JsonProperty("name")] public string Name { get; private set; }
        [JsonProperty("bonuses")] public IReadOnlyList<BonusDef> Bonuses { get; private set; }
        [JsonProperty("triggers")] public IReadOnlyList<TriggerDef> Triggers { get; private set; }
    }

    /// <summary>What the cut-in template needs (03 Architecture 3.3). The rules carry it and never use it.</summary>
    public class CutinDef
    {
        [JsonProperty("shout")] public string Shout { get; private set; }
        /// <summary>Picture name; null = the placeholder silhouette.</summary>
        [JsonProperty("drawing")] public string Drawing { get; private set; }
        /// <summary>Optional behind-layer picture name.</summary>
        [JsonProperty("behind")] public string Behind { get; private set; }
    }

    /// <summary>One class (classes.json).</summary>
    public class ClassDef
    {
        [JsonProperty("id")] public string Id { get; private set; }
        [JsonProperty("name")] public string Name { get; private set; }
    }

    /// <summary>One trait (traits.json).</summary>
    public class TraitDef
    {
        [JsonProperty("id")] public string Id { get; private set; }
        [JsonProperty("name")] public string Name { get; private set; }
        /// <summary>side, pantheon or origin.</summary>
        [JsonProperty("kind")] public string Kind { get; private set; }
        /// <summary>Pantheon traits only.</summary>
        [JsonProperty("cutin_colors")] public CutinColorsDef CutinColors { get; private set; }
    }

    public class CutinColorsDef
    {
        [JsonProperty("background")] public string Background { get; private set; }
        [JsonProperty("glow")] public string Glow { get; private set; }
    }

    /// <summary>One formation: its five slots in slot order (formations.json, 03 Architecture 7.1).</summary>
    public class FormationDef
    {
        /// <summary>The name teams use: "3-2", "2-3", "4-1" or "1-4".</summary>
        [JsonProperty("id")] public string Id { get; private set; }
        [JsonProperty("slots")] public IReadOnlyList<FormationSlotDef> Slots { get; private set; }
    }

    /// <summary>One slot of a formation. Slots are numbered 1–5 here, as in 02 GDD 2; the code counts from 0 (reading R18).</summary>
    public class FormationSlotDef
    {
        /// <summary>1 to 5: front row top to bottom, then the back row.</summary>
        [JsonProperty("slot")] public int? Slot { get; private set; }
        /// <summary>front or back.</summary>
        [JsonProperty("row")] public string Row { get; private set; }
        /// <summary>Back slots only: the front slot numbers standing in front of this one (the lane map of 02 GDD 2).</summary>
        [JsonProperty("covered_by")] public IReadOnlyList<int> CoveredBy { get; private set; }
        /// <summary>Where the hero's box stands on screen: x, y in reference pixels on the player's side. The rules never read it.</summary>
        [JsonProperty("at")] public IReadOnlyList<double> At { get; private set; }
    }

    /// <summary>The enemy crew or a target team (teams.json).</summary>
    public class TeamDef
    {
        [JsonProperty("id")] public string Id { get; private set; }
        /// <summary>enemy or target.</summary>
        [JsonProperty("kind")] public string Kind { get; private set; }
        /// <summary>The id of a formation in formations.json (3-2, 2-3, 4-1 or 1-4).</summary>
        [JsonProperty("formation")] public string Formation { get; private set; }
        /// <summary>Hero id of the named leader.</summary>
        [JsonProperty("leader")] public string Leader { get; private set; }
        /// <summary>Five hero ids in slot order: front row top to bottom, then the back row.</summary>
        [JsonProperty("slots")] public IReadOnlyList<string> Slots { get; private set; }
    }
}
