// Shared enum for all Survivor Class subtypes — the Villager-side counterpart
// to MalignoType. Add new ideas here as they get built (Babaylan, Albularyo,
// Reiki Practitioner, Tarot User are scoped in the task list but not yet
// implemented).
public enum SurvivorClass
{
    None,              // A plain Villager, or a Maligno (Survivor Classes are Crewmate-only)
    ThirdEyeUser,      // Third Eye Sight — reveals recent Maligno footprints + active traps
    ReikiPractitioner, // Aura Sense — reveals a fuzzy, jittered sense of nearby Maligno auras
    Albularyo,         // Pag-aalaga (Heal) — cures a nearby Villager cursed by Duwende's trap
}