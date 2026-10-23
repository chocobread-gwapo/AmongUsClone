using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

// A role's configurable settings: how many of it can appear this match
// (a hard cap), and its relative weight when randomly picking among roles
// that still have room under their cap. Weights don't need to add up to
// 100 — they're relative, same idea as loot table weights.
public struct RoleSetting : INetworkSerializable, System.IEquatable<RoleSetting>
{
    public int maxCount;
    public int chancePercent;

    public void NetworkSerialize<T>(BufferSerializer<T> serializer) where T : IReaderWriter
    {
        serializer.SerializeValue(ref maxCount);
        serializer.SerializeValue(ref chancePercent);
    }

    public bool Equals(RoleSetting other) => maxCount == other.maxCount && chancePercent == other.chancePercent;
}

public class GameStartManager : NetworkBehaviour
{
    public static GameStartManager Instance { get; private set; }

    // ---------------------------------------------------------------------
    // Host-configurable match settings — all NetworkVariables/NetworkLists
    // (Everyone-read / Server-write), so the Host Settings Panel can change
    // them live in the lobby and every connected client sees current values,
    // not just the host.
    // ---------------------------------------------------------------------

    [Header("Match Settings (defaults — overridden live via Host Settings Panel)")]
    private NetworkVariable<int> impostorCount = new NetworkVariable<int>(
        1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Tooltip("How long an Impostor must wait between kills. PRD suggests 15 / 22.5 / 30 / 45 as preset options.")]
    private NetworkVariable<float> killCooldownDuration = new NetworkVariable<float>(
        15f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    [Tooltip("Total meeting length (discussion + voting combined) — read by MeetingManager instead of its own fixed value.")]
    private NetworkVariable<float> meetingDuration = new NetworkVariable<float>(
        30f, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public int ImpostorCount => impostorCount.Value;
    public float KillCooldownDuration => killCooldownDuration.Value;
    public float MeetingDuration => meetingDuration.Value;

    public void ServerSetImpostorCount(int value)
    {
        if (!IsServer) return;
        impostorCount.Value = Mathf.Clamp(value, 1, 3);
    }

    public void ServerSetKillCooldownDuration(float value)
    {
        if (!IsServer) return;
        killCooldownDuration.Value = value;
    }

    public void ServerSetMeetingDuration(float value)
    {
        if (!IsServer) return;
        meetingDuration.Value = value;
    }

    // ---------------------------------------------------------------------
    // Extended match settings (the Among Us style Game Settings panel).
    // Everything is stored on the host and synced to everyone. The three
    // settings above (Impostors, Kill Cooldown, Voting Time) keep their own
    // NetworkVariables; the rest live in one NetworkList<float>, indexed by
    // the Setting enum, so adding a setting is just one enum entry + one row
    // in Infos.
    // ---------------------------------------------------------------------

    public enum Setting
    {
        Impostors, KillCooldown, MalignoVision, KillDistance,
        PlayerSpeed, CrewVision,
        EmergencyMeetings, EmergencyCooldown, DiscussionTime, VotingTime, AnonymousVotes, ConfirmEjects,
        TaskBarUpdates,
        CommonTasks, LongTasks, ShortTasks,
        Capacity,
    }

    public class SettingInfo
    {
        public string label; public float min, max, step, def; public string unit = ""; public string[] options; public bool toggle;
        public string Format(float v)
        {
            if (toggle) return v > 0.5f ? "On" : "Off";
            if (options != null) return options[Mathf.Clamp(Mathf.RoundToInt(v), 0, options.Length - 1)];
            return v.ToString("0.##") + unit;
        }
    }

    private static readonly SettingInfo[] Infos =
    {
        new SettingInfo { label = "# Maligno",           min = 1,    max = 3,   step = 1,    def = 1 },
        new SettingInfo { label = "Kill Cooldown",       min = 10,   max = 60,  step = 2.5f, def = 15,  unit = "s" },
        new SettingInfo { label = "Maligno Vision",      min = 0.25f,max = 5,   step = 0.25f,def = 1,   unit = "x" },
        new SettingInfo { label = "Kill Distance",       min = 0,    max = 2,   step = 1,    def = 1,   options = new[] { "Short", "Medium", "Long" } },
        new SettingInfo { label = "Player Speed",        min = 0.5f, max = 3,   step = 0.25f,def = 1,   unit = "x" },
        new SettingInfo { label = "Villager Vision",     min = 0.25f,max = 5,   step = 0.25f,def = 1,   unit = "x" },
        new SettingInfo { label = "# Emergency Meetings",min = 0,    max = 9,   step = 1,    def = 1 },
        new SettingInfo { label = "Emergency Cooldown",  min = 0,    max = 60,  step = 5,    def = 20,  unit = "s" },
        new SettingInfo { label = "Discussion Time",     min = 0,    max = 120, step = 5,    def = 0,   unit = "s" },
        new SettingInfo { label = "Voting Time",         min = 10,   max = 300, step = 5,    def = 30,  unit = "s" },
        new SettingInfo { label = "Anonymous Votes",     min = 0,    max = 1,   step = 1,    def = 1,   toggle = true },
        new SettingInfo { label = "Confirm Ejects",      min = 0,    max = 1,   step = 1,    def = 1,   toggle = true },
        new SettingInfo { label = "Task Bar Updates",    min = 0,    max = 2,   step = 1,    def = 0,   options = new[] { "Always", "Meetings", "Never" } },
        new SettingInfo { label = "# Common Tasks",      min = 0,    max = 2,   step = 1,    def = 1 },
        new SettingInfo { label = "# Long Tasks",        min = 0,    max = 4,   step = 1,    def = 1 },
        new SettingInfo { label = "# Short Tasks",       min = 0,    max = 7,   step = 1,    def = 2 },
        new SettingInfo { label = "Capacity",            min = 4,    max = 15,  step = 1,    def = 10 },
    };

    public static SettingInfo Info(Setting s) => Infos[(int)s];

    private NetworkList<float> extraSettings;

    public float Get(Setting s)
    {
        switch (s)
        {
            case Setting.Impostors: return impostorCount.Value;
            case Setting.KillCooldown: return killCooldownDuration.Value;
            case Setting.VotingTime: return meetingDuration.Value;
        }
        int i = (int)s;
        if (extraSettings == null || i >= extraSettings.Count) return Infos[i].def;
        return extraSettings[i];
    }

    public void ServerSet(Setting s, float value)
    {
        if (!IsServer) return;
        SettingInfo info = Infos[(int)s];
        value = Mathf.Clamp(Mathf.Round(value / info.step) * info.step, info.min, info.max);
        value = Mathf.Round(value * 100f) / 100f;
        if (s == Setting.Capacity)
        {
            // Can't shrink the lobby below the players already inside it.
            int inside = NetworkManager != null ? NetworkManager.ConnectedClientsIds.Count : 0;
            value = Mathf.Min(info.max, Mathf.Max(value, inside));
            MultiplayerConnectionManager.Instance?.ApplyCapacity(Mathf.RoundToInt(value));
        }
        switch (s)
        {
            case Setting.Impostors: impostorCount.Value = Mathf.RoundToInt(value); return;
            case Setting.KillCooldown: killCooldownDuration.Value = value; return;
            case Setting.VotingTime: meetingDuration.Value = value; return;
        }
        int i = (int)s;
        if (extraSettings != null && i < extraSettings.Count && !Mathf.Approximately(extraSettings[i], value)) extraSettings[i] = value;
    }

    public float MalignoVision => Get(Setting.MalignoVision);
    public float CrewVision => Get(Setting.CrewVision);
    public float PlayerSpeed => Get(Setting.PlayerSpeed);
    public int KillDistanceIndex => Mathf.RoundToInt(Get(Setting.KillDistance));
    public float KillRangeScale => KillDistanceIndex == 0 ? 0.7f : (KillDistanceIndex == 2 ? 1.5f : 1f);
    public int EmergencyMeetings => Mathf.RoundToInt(Get(Setting.EmergencyMeetings));
    public float EmergencyCooldown => Get(Setting.EmergencyCooldown);
    public float DiscussionTime => Get(Setting.DiscussionTime);
    public bool AnonymousVotes => Get(Setting.AnonymousVotes) > 0.5f;
    public bool ConfirmEjects => Get(Setting.ConfirmEjects) > 0.5f;
    public int Capacity => Mathf.RoundToInt(Get(Setting.Capacity));
    public int TaskBarMode => Mathf.RoundToInt(Get(Setting.TaskBarUpdates));

    public static readonly string[] PresetNames = { "CLASSIC", "QUICK MATCH", "HARDCORE", "SOCIAL" };
    public static readonly string[] PresetBlurbs =
    {
        "The standard rules.",
        "Faster players, short meetings, quick cooldowns, no long tasks.",
        "Sharper Maligno, dim Villagers, extra long tasks, no role reveal on eject.",
        "Long discussions, open votes, two emergency calls.",
    };

    private static readonly (Setting s, float v)[][] PresetValues =
    {
        new (Setting, float)[0],
        new[] { (Setting.KillCooldown, 10f), (Setting.EmergencyCooldown, 10f), (Setting.VotingTime, 20f), (Setting.PlayerSpeed, 1.25f), (Setting.LongTasks, 0f) },
        new[] { (Setting.MalignoVision, 1.5f), (Setting.CrewVision, 0.75f), (Setting.KillDistance, 2f), (Setting.ConfirmEjects, 0f), (Setting.TaskBarUpdates, 1f), (Setting.LongTasks, 2f) },
        new[] { (Setting.DiscussionTime, 30f), (Setting.VotingTime, 60f), (Setting.EmergencyMeetings, 2f), (Setting.AnonymousVotes, 0f), (Setting.TaskBarUpdates, 1f) },
    };

    public void ServerResetSettings()
    {
        if (!IsServer) return;
        for (int i = 0; i < Infos.Length; i++) ServerSet((Setting)i, Infos[i].def);
    }

    public void ServerApplyPreset(int index)
    {
        if (!IsServer || index < 0 || index >= PresetValues.Length) return;
        ServerResetSettings();
        foreach (var (s, v) in PresetValues[index]) ServerSet(s, v);
    }

    // ---------------------------------------------------------------------
    // Per-role settings (Maligno sub-types + Survivor Classes): each role has
    // its own max-count cap and % chance weight. Fixed-order arrays map an
    // enum value to an index into the matching NetworkList below — same
    // pattern for both role families.
    // ---------------------------------------------------------------------

    [Header("Maligno Types")]
    [Tooltip("Which folklore roles can be randomly assigned, how many of each can appear (cap), and their relative pick weight.")]
    private static readonly MalignoType[] AllMalignoTypes = new MalignoType[]
    {
        MalignoType.Duwende,
        MalignoType.Tikbalang,
        MalignoType.BalBal,
        MalignoType.Aswang,
        MalignoType.Kapre,
    };

    // Defaults: cap 1, 20% weight each — matches the 5-type even split shown
    // in the reference screenshots (100% / 5 roles = 20% each).
    private NetworkList<RoleSetting> malignoRoleSettings;

    public static System.Collections.Generic.IReadOnlyList<MalignoType> AllKnownMalignoTypes => AllMalignoTypes;

    public int GetMalignoMaxCount(MalignoType type) => GetSetting(malignoRoleSettings, IndexOf(AllMalignoTypes, type)).maxCount;
    public int GetMalignoChance(MalignoType type) => GetSetting(malignoRoleSettings, IndexOf(AllMalignoTypes, type)).chancePercent;

    public void ServerSetMalignoMaxCount(MalignoType type, int value)
    {
        if (!IsServer) return;
        SetMaxCount(malignoRoleSettings, IndexOf(AllMalignoTypes, type), Mathf.Clamp(value, 0, 3));
    }

    public void ServerSetMalignoChance(MalignoType type, int value)
    {
        if (!IsServer) return;
        SetChance(malignoRoleSettings, IndexOf(AllMalignoTypes, type), Mathf.Clamp(value, 0, 100));
    }

    [Header("Survivor Classes")]
    [Tooltip("How many Crewmates can get each Survivor Class this match (cap), and its relative pick weight.")]
    private static readonly SurvivorClass[] AllSurvivorClasses = new SurvivorClass[]
    {
        SurvivorClass.ThirdEyeUser,
        SurvivorClass.ReikiPractitioner,
        SurvivorClass.Albularyo,
        SurvivorClass.Babaylan,
        SurvivorClass.TarotUser,
    };

    private NetworkList<RoleSetting> survivorRoleSettings;

    public static System.Collections.Generic.IReadOnlyList<SurvivorClass> AllKnownSurvivorClasses => AllSurvivorClasses;

    public int GetSurvivorMaxCount(SurvivorClass sc) => GetSetting(survivorRoleSettings, IndexOf(AllSurvivorClasses, sc)).maxCount;
    public int GetSurvivorChance(SurvivorClass sc) => GetSetting(survivorRoleSettings, IndexOf(AllSurvivorClasses, sc)).chancePercent;

    public void ServerSetSurvivorMaxCount(SurvivorClass sc, int value)
    {
        if (!IsServer) return;
        SetMaxCount(survivorRoleSettings, IndexOf(AllSurvivorClasses, sc), Mathf.Clamp(value, 0, 3));
    }

    public void ServerSetSurvivorChance(SurvivorClass sc, int value)
    {
        if (!IsServer) return;
        SetChance(survivorRoleSettings, IndexOf(AllSurvivorClasses, sc), Mathf.Clamp(value, 0, 100));
    }

    // ---- small helpers shared by both role families ----

    private static int IndexOf<T>(T[] array, T value)
    {
        for (int i = 0; i < array.Length; i++)
            if (EqualityComparer<T>.Default.Equals(array[i], value)) return i;
        return -1;
    }

    private static RoleSetting GetSetting(NetworkList<RoleSetting> list, int index)
    {
        if (list == null || index < 0 || index >= list.Count) return new RoleSetting { maxCount = 0, chancePercent = 0 };
        return list[index];
    }

    private static void SetMaxCount(NetworkList<RoleSetting> list, int index, int value)
    {
        if (list == null || index < 0 || index >= list.Count) return;
        RoleSetting setting = list[index];
        setting.maxCount = value;
        list[index] = setting;
    }

    private static void SetChance(NetworkList<RoleSetting> list, int index, int value)
    {
        if (list == null || index < 0 || index >= list.Count) return;
        RoleSetting setting = list[index];
        setting.chancePercent = value;
        list[index] = setting;
    }

    private NetworkVariable<bool> gameStarted = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool GameStarted => gameStarted.Value;

    [Header("Scenes (exact names from Build Settings)")]
    [SerializeField] private string gameSceneName = "Game";
    [SerializeField] private string lobbySceneName = "Lobby";

    // Guards against double-clicks / overlapping scene loads.
    private bool sceneTransitionInProgress = false;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Spawned once by MultiplayerConnectionManager and must survive the
        // Lobby <-> Game scene switches (NetworkObject "Destroy With Scene"
        // is unchecked on the prefab too).
        DontDestroyOnLoad(gameObject);

        // Construct here — the NetworkList object itself needs to exist
        // before NGO registers this NetworkBehaviour's variables. But don't
        // write any VALUES into it yet (see OnNetworkSpawn below): doing
        // that here triggers "NetworkVariable is written to, but doesn't
        // know its NetworkBehaviour yet" warnings, since Awake() runs
        // before the NetworkObject has spawned.
        malignoRoleSettings = new NetworkList<RoleSetting>();
        survivorRoleSettings = new NetworkList<RoleSetting>();
        extraSettings = new NetworkList<float>();
    }

    public override void OnNetworkSpawn()
    {
        base.OnNetworkSpawn();

        // Default: every known role gets cap 1, weight 20 (even split across
        // 5 roles) — matches today's "everyone starts at count 1" behavior
        // while adding the new chance weighting on top. Server-only: clients
        // receive these values via the NetworkList's own sync, not by
        // populating their own local copy.
        if (!IsServer) return;

        for (int i = 0; i < Infos.Length; i++)
            extraSettings.Add(Infos[i].def);

        for (int i = 0; i < AllMalignoTypes.Length; i++)
            malignoRoleSettings.Add(new RoleSetting { maxCount = 1, chancePercent = 20 });

        for (int i = 0; i < AllSurvivorClasses.Length; i++)
            survivorRoleSettings.Add(new RoleSetting { maxCount = 1, chancePercent = 20 });
    }

    public void HostStartGame()
    {
        if (!IsServer)
        {
            Debug.LogWarning("Only the host/server can start the game.");
            return;
        }

        if (gameStarted.Value || sceneTransitionInProgress)
        {
            Debug.LogWarning("Game already started (or starting).");
            return;
        }

        // Among Us flow: Start loads the match scene for everyone FIRST;
        // roles are assigned only once every client has finished loading it,
        // so RoleRevealUI / HUD actually exist when the role RPCs arrive.
        sceneTransitionInProgress = true;
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleGameSceneLoaded;

        var status = NetworkManager.Singleton.SceneManager.LoadScene(gameSceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[Server] Couldn't load \"{gameSceneName}\": {status}. Is it in Build Settings with that exact name?");
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleGameSceneLoaded;
            sceneTransitionInProgress = false;
        }
    }

    private void HandleGameSceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != gameSceneName) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleGameSceneLoaded;
        sceneTransitionInProgress = false;

        if (clientsTimedOut != null && clientsTimedOut.Count > 0)
            Debug.LogWarning($"[Server] {clientsTimedOut.Count} client(s) timed out loading the Game scene.");

        SceneSpawnPoints.PlaceAllPlayers();
        AssignRoles();
        TaskManager.Instance?.ServerAssignTasks(); // each Villager gets their own tasks
        gameStarted.Value = true;
    }

    private void AssignRoles()
    {
        List<PlayerRole> allPlayers = new List<PlayerRole>();
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                PlayerRole role = client.PlayerObject.GetComponent<PlayerRole>();
                if (role != null)
                {
                    allPlayers.Add(role);
                }
            }
        }

        if (allPlayers.Count == 0)
        {
            Debug.LogWarning("No players found to assign roles to.");
            return;
        }

        // Shuffle (Fisher-Yates) so impostor selection is unbiased
        for (int i = allPlayers.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (allPlayers[i], allPlayers[j]) = (allPlayers[j], allPlayers[i]);
        }

        int actualImpostorCount = Mathf.Min(impostorCount.Value, allPlayers.Count - 1);
        actualImpostorCount = Mathf.Max(actualImpostorCount, allPlayers.Count > 1 ? 1 : 0);

        List<ulong> impostorClientIds = new List<ulong>();

        // Track how many of each Maligno type have been assigned so far this
        // match, so each type's cap is respected as slots get filled.
        Dictionary<MalignoType, int> assignedMalignoCounts = new Dictionary<MalignoType, int>();

        for (int i = 0; i < allPlayers.Count; i++)
        {
            if (i < actualImpostorCount)
            {
                MalignoType assignedType = RollMalignoType(assignedMalignoCounts);
                assignedMalignoCounts[assignedType] = assignedMalignoCounts.GetValueOrDefault(assignedType, 0) + 1;

                allPlayers[i].ServerSetRole(Role.Impostor, assignedType);
                impostorClientIds.Add(allPlayers[i].OwnerClientId);

                Debug.Log($"[Server] {allPlayers[i].gameObject.name} assigned Impostor ({assignedType})");
            }
            else
            {
                allPlayers[i].ServerSetRole(Role.Crewmate, MalignoType.None);
            }
        }

        // Crewmates left after Impostors are picked each get an independent
        // weighted roll for a Survivor Class, bounded by each class's cap.
        // A crewmate whose roll lands on a class already at its cap (or if
        // every class is already full/zero-weight) just stays a plain
        // Crewmate — not every Crewmate is guaranteed a class.
        // Each class's % CHANCE is an independent probability per slot (not a share of a
        // pool): at 5% a class shows up in roughly 1 match in 20, at 100% it is guaranteed
        // up to its # OF ROLE cap. Classes are tried in random order so none is favoured.
        var classOrder = new List<int>();
        for (int i = 0; i < AllSurvivorClasses.Length; i++) classOrder.Add(i);
        for (int i = classOrder.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (classOrder[i], classOrder[j]) = (classOrder[j], classOrder[i]);
        }

        int nextCrew = actualImpostorCount; // allPlayers is already shuffled
        foreach (int ci in classOrder)
        {
            SurvivorClass sc = AllSurvivorClasses[ci];
            RoleSetting setting = GetSetting(survivorRoleSettings, ci);
            if (sc == SurvivorClass.None || setting.chancePercent <= 0) continue;

            for (int slot = 0; slot < setting.maxCount && nextCrew < allPlayers.Count; slot++)
            {
                if (Random.Range(0, 100) >= setting.chancePercent) continue;
                allPlayers[nextCrew].ServerSetSurvivorClass(sc);
                Debug.Log($"[Server] {allPlayers[nextCrew].gameObject.name} assigned Survivor Class: {sc}");
                nextCrew++;
            }
        }

        // Let each impostor know who their fellow impostor(s) are
        if (impostorClientIds.Count > 1)
        {
            ClientRpcParams targetImpostorsOnly = new ClientRpcParams
            {
                Send = new ClientRpcSendParams { TargetClientIds = impostorClientIds.ToArray() }
            };
            NotifyFellowImpostorsClientRpc(impostorClientIds.ToArray(), targetImpostorsOnly);
        }

        Debug.Log($"[Server] Assigned roles: {actualImpostorCount} Impostor(s) out of {allPlayers.Count} players.");
    }

    // Independent roll per Maligno type (same rule as Survivor classes): each type with room
    // under its # OF ROLE cap passes with its % CHANCE, types tried in random order, first pass
    // wins. If none pass the Maligno is a plain Maligno (kills, no special power).
    private MalignoType RollMalignoType(Dictionary<MalignoType, int> assignedSoFar)
    {
        var order = new List<int>();
        for (int i = 0; i < AllMalignoTypes.Length; i++) order.Add(i);
        for (int i = order.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (order[i], order[j]) = (order[j], order[i]);
        }
        foreach (int i in order)
        {
            MalignoType type = AllMalignoTypes[i];
            RoleSetting setting = GetSetting(malignoRoleSettings, i);
            if (type == MalignoType.None || setting.chancePercent <= 0) continue;
            if (assignedSoFar.GetValueOrDefault(type, 0) >= setting.maxCount) continue;
            if (Random.Range(0, 100) < setting.chancePercent) return type;
        }
        return MalignoType.None;
    }

    // Weighted random pick among Maligno types that still have room under
    // their cap. If every type is capped out or has zero weight (shouldn't
    // normally happen with sane settings), falls back to a uniform pick
    // among ALL types ignoring caps, so a match never fails to assign a type.
    private MalignoType PickWeightedMalignoType(Dictionary<MalignoType, int> assignedSoFar)
    {
        List<(MalignoType type, int weight)> pool = new List<(MalignoType, int)>();
        for (int i = 0; i < AllMalignoTypes.Length; i++)
        {
            MalignoType type = AllMalignoTypes[i];
            RoleSetting setting = GetSetting(malignoRoleSettings, i);
            int assigned = assignedSoFar.GetValueOrDefault(type, 0);
            if (assigned < setting.maxCount && setting.chancePercent > 0)
            {
                pool.Add((type, setting.chancePercent));
            }
        }

        MalignoType picked = WeightedPick(pool);
        if (picked != MalignoType.None) return picked;

        Debug.LogWarning("[Server] All Maligno types capped out or zero-weight — falling back to a uniform pick among all known types.");
        return AllMalignoTypes.Length > 0 ? AllMalignoTypes[Random.Range(0, AllMalignoTypes.Length)] : MalignoType.BalBal;
    }

    // Same idea as above, but can legitimately return None (a Crewmate who
    // just doesn't get a Survivor Class this match) once every class is
    // capped out — unlike Maligno types, there's no fallback-to-anything,
    // since not every Crewmate needs a class.
    private SurvivorClass PickWeightedSurvivorClass(Dictionary<SurvivorClass, int> assignedSoFar)
    {
        List<(SurvivorClass sc, int weight)> pool = new List<(SurvivorClass, int)>();
        for (int i = 0; i < AllSurvivorClasses.Length; i++)
        {
            SurvivorClass sc = AllSurvivorClasses[i];
            RoleSetting setting = GetSetting(survivorRoleSettings, i);
            int assigned = assignedSoFar.GetValueOrDefault(sc, 0);
            if (assigned < setting.maxCount && setting.chancePercent > 0)
            {
                pool.Add((sc, setting.chancePercent));
            }
        }

        return WeightedPick(pool);
    }

    // Generic weighted pick: returns default(T) (None for both our enums,
    // since None is always value 0) if the pool is empty.
    private static T WeightedPick<T>(List<(T value, int weight)> pool)
    {
        int totalWeight = 0;
        foreach (var entry in pool) totalWeight += entry.weight;
        if (totalWeight <= 0) return default;

        int roll = Random.Range(0, totalWeight);
        int cumulative = 0;
        foreach (var entry in pool)
        {
            cumulative += entry.weight;
            if (roll < cumulative) return entry.value;
        }

        return pool[pool.Count - 1].value; // shouldn't be reached, but safe fallback
    }

    [ClientRpc]
    private void NotifyFellowImpostorsClientRpc(ulong[] impostorClientIds, ClientRpcParams rpcParams = default)
    {
        Debug.Log($"[Client] Your fellow impostor client IDs: {string.Join(", ", impostorClientIds)}");
        // TODO: use this to highlight teammates in UI, e.g. colored name tags
    }

    // ---- Return to lobby ----
    // Wired to the GameOverPanel's "Return to Lobby" button. Only actually
    // does anything when called on the host/server's own instance — same
    // host-gated pattern as HostStartGame() above, so every client can safely
    // call this without a ServerRpc wrapper.
    public void HostReturnToLobby()
    {
        if (!IsServer)
        {
            Debug.LogWarning("Only the host/server can return everyone to the lobby.");
            return;
        }

        if (GameResultManager.Instance == null || !GameResultManager.Instance.GameEnded)
        {
            Debug.LogWarning("Can't return to lobby before the match has ended.");
            return;
        }

        if (sceneTransitionInProgress) return;

        // Reset every connected player: alive again, Crewmate until next
        // AssignRoles(), controls re-enabled.
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            PlayerRole role = client.PlayerObject.GetComponent<PlayerRole>();
            role?.ServerResetForNewGame();

            client.PlayerObject.GetComponent<PlayerController>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<DuwendeAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<BabaylanAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<TikbalangAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<AswangAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<KapreAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<BalBalAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<ThirdEyeAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<ReikiAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<AlbularyoAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<TarotUserAbility>()?.ServerResetForNewGame();
        }

        // TaskManager, GameResultManager, TaskZones, HUD, GameOverUI and
        // cooldown UI all live in the Game scene, which is unloaded below and
        // loaded fresh next match — nothing to reset for them. Only state on
        // the persistent player objects (above) and statics need clearing.
        gameStarted.Value = false;
        ClearStaticMatchStateClientRpc();

        sceneTransitionInProgress = true;
        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted += HandleLobbySceneLoaded;

        var status = NetworkManager.Singleton.SceneManager.LoadScene(lobbySceneName, LoadSceneMode.Single);
        if (status != SceneEventProgressStatus.Started)
        {
            Debug.LogError($"[Server] Couldn't load \"{lobbySceneName}\": {status}. Is it in Build Settings with that exact name?");
            NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLobbySceneLoaded;
            sceneTransitionInProgress = false;
        }
    }

    private void HandleLobbySceneLoaded(string sceneName, LoadSceneMode mode, List<ulong> clientsCompleted, List<ulong> clientsTimedOut)
    {
        if (sceneName != lobbySceneName) return;

        NetworkManager.Singleton.SceneManager.OnLoadEventCompleted -= HandleLobbySceneLoaded;
        sceneTransitionInProgress = false;
        SceneSpawnPoints.PlaceAllPlayers();
        Debug.Log("[Server] Back in the lobby — ready for a new match.");
    }

    // Statics survive scene unloads, so stale banana-trunk references would
    // otherwise pile up across matches.
    [ClientRpc]
    private void ClearStaticMatchStateClientRpc()
    {
        BalBalAbility.ClearAllTrunks();
    }
}
