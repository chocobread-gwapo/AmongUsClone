// Plain-language text for goals and ability tooltips, kept in one place.
public static class RoleInfo
{
    public static string Goal(bool maligno, bool alive)
    {
        if (maligno)
            return alive
                ? "Eliminate the Villagers until you equal their number. Don't get caught."
                : "You were caught. Your fellow Maligno fight on.";
        return alive
            ? "Finish every chore, and cast out the Maligno."
            : "You are a ghost. Keep finishing chores to help your village.";
    }

    public static (string name, string how) Maligno(MalignoType t)
    {
        switch (t)
        {
            case MalignoType.Duwende: return ("Curse Trap", "Plant a hidden mound. A Villager who steps on it is cursed: slowed and half-blind for a while.");
            case MalignoType.Tikbalang: return ("Bewilder", "Invert a nearby Villager's controls for a short time. Stand close to your target.");
            case MalignoType.BalBal: return ("Bal-Bal", "Your kills leave no body to report. The victim is replaced by a banana trunk.");
            case MalignoType.Aswang: return ("Shapeshift", "Copy the look and name of a nearby player for a short time.");
            case MalignoType.Kapre: return ("Smoke Screen", "Blow smoke that shrinks the vision of nearby Villagers.");
            default: return ("Ability", "Your Maligno power.");
        }
    }

    public static (string name, string how) Survivor(SurvivorClass c)
    {
        switch (c)
        {
            case SurvivorClass.ThirdEyeUser: return ("Third Eye", "See recent Maligno footprints and hidden traps nearby.");
            case SurvivorClass.ReikiPractitioner: return ("Aura Sense", "Sense roughly where Maligno are around you.");
            case SurvivorClass.Albularyo: return ("Pag-aalaga", "Cure a nearby Villager who has been cursed.");
            case SurvivorClass.Babaylan: return ("Judge", "In a meeting, condemn one player (once per match). Guess wrong and you are exiled.");
            case SurvivorClass.TarotUser: return ("Sapalaran", "Draw a card for a random buff or curse. Fortune can go either way.");
            default: return ("", "");
        }
    }
}
