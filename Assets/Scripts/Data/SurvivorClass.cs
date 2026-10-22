// Shared enum for all Survivor Class subtypes — the Villager-side counterpart
// to MalignoType. Add new ideas here as they get built.
public enum SurvivorClass
{
    None,              // A plain Villager, or a Maligno (Survivor Classes are Crewmate-only)
    ThirdEyeUser,      // Third Eye Sight — reveals recent Maligno footprints + active traps
    ReikiPractitioner, // Aura Sense — reveals a fuzzy, jittered sense of nearby Maligno auras
    Albularyo,         // Pag-aalaga (Heal) — cures a nearby Villager cursed by Duwende's trap
    Babaylan,          // Judge Skill — meeting-only, once per match: instantly condemns a chosen player, or gets banished themselves if wrong
    TarotUser,         // Sapalaran (Fortune) — draws a random card for a random upright/reversed buff or debuff
}