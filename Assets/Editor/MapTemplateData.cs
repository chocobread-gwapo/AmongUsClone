#if UNITY_EDITOR
// Generated from the map template image (walkable-area analysis). World units:
// the 4000px map is 62.5 units wide, centred on (0,0).
public static class MapTemplateData
{
    // Meeting-table centre (the cafeteria's emergency table).
    public static readonly UnityEngine.Vector2 MeetingTable = new UnityEngine.Vector2(3.97f, 9.67f);

    public static readonly (string room, string task, float x, float y)[] Tasks =
    {
        ("Reactor","Start Reactor",-24.06f,2.80f),
        ("Upper Engine","Align Engine",-17.50f,7.80f),
        ("Lower Engine","Align Engine",-17.50f,-6.89f),
        ("Security","Check Cameras",-12.19f,1.86f),
        ("MedBay","Submit Scan",-5.31f,4.05f),
        ("Electrical","Fix Wiring",-5.31f,-5.02f),
        ("Storage","Fuel Engines",2.19f,-10.64f),
        ("Admin","Swipe Card",10.94f,-1.89f),
        ("Comms","Upload Data",10.31f,-12.83f),
        ("Shields","Prime Shields",17.50f,-8.45f),
        ("O2","Clean Filter",13.12f,5.92f),
        ("Navigation","Chart Course",27.50f,2.80f),
        ("Weapons","Clear Asteroids",16.88f,11.55f),
        ("Cafeteria","Empty Garbage",7.81f,7.17f)
    };

}
#endif
