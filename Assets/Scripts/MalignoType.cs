// Shared enum for all Maligno (Impostor) subtypes.
// Add new folklore roles here as you build their abilities.
public enum MalignoType
{
    None,       // Crewmates, or an Impostor before assignment
    Duwende,    // Curse trap — slows + blinds Crewmates who trigger it
    Tikbalang,  // Inverts a target Crewmate's controls temporarily
    BalBal,     // Kills leave no reportable body — replaces with a prop instead
    Aswang,     // Disguises as a nearby Crewmate
    Kapre,      // Smokescreen — reduces nearby Crewmates' vision radius
}