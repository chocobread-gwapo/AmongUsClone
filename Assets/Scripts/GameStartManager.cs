using System.Collections.Generic;
using Unity.Netcode;
using Unity.Collections;
using UnityEngine;

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

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Construct here — the NetworkList object itself needs to exist
        // before NGO registers this NetworkBehaviour's variables. But don't
        // write any VALUES into it yet (see OnNetworkSpawn below): doing
        // that here triggers "NetworkVariable is written to, but doesn't
        // know its NetworkBehaviour yet" warnings, since Awake() runs
        // before the NetworkObject has spawned.
        malignoRoleSettings = new NetworkList<RoleSetting>();
        survivorRoleSettings = new NetworkList<RoleSetting>();
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

        if (gameStarted.Value)
        {
            Debug.LogWarning("Game already started.");
            return;
        }

        AssignRoles();
        gameStarted.Value = true;

        NotifyGameStartedClientRpc();
    }

    [ClientRpc]
    private void NotifyGameStartedClientRpc()
    {
        if (StartGameButtonUI.Instance != null) StartGameButtonUI.Instance.Hide();
        if (GameHudUI.Instance != null) GameHudUI.Instance.Show();
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
                MalignoType assignedType = PickWeightedMalignoType(assignedMalignoCounts);
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
        Dictionary<SurvivorClass, int> assignedSurvivorCounts = new Dictionary<SurvivorClass, int>();

        for (int i = actualImpostorCount; i < allPlayers.Count; i++)
        {
            SurvivorClass picked = PickWeightedSurvivorClass(assignedSurvivorCounts);
            if (picked == SurvivorClass.None) continue;

            assignedSurvivorCounts[picked] = assignedSurvivorCounts.GetValueOrDefault(picked, 0) + 1;
            allPlayers[i].ServerSetSurvivorClass(picked);
            Debug.Log($"[Server] {allPlayers[i].gameObject.name} assigned Survivor Class: {picked}");
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
            client.PlayerObject.GetComponent<ThirdEyeAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<ReikiAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<AlbularyoAbility>()?.ServerResetForNewGame();
            client.PlayerObject.GetComponent<TarotUserAbility>()?.ServerResetForNewGame();
        }

        TaskManager.Instance?.ServerResetAllTasks();
        GameResultManager.Instance?.ServerResetForNewGame();

        gameStarted.Value = false;

        Debug.Log("[Server] Returned to lobby — ready for a new match.");
        NotifyReturnedToLobbyClientRpc();
    }

    [ClientRpc]
    private void NotifyReturnedToLobbyClientRpc()
    {
        if (GameOverUI.Instance != null) GameOverUI.Instance.Hide();
        if (StartGameButtonUI.Instance != null) StartGameButtonUI.Instance.Show();
        if (GameHudUI.Instance != null) GameHudUI.Instance.Hide();

        // Banana-trunk decoys are purely local props (never NetworkObjects),
        // so nothing else clears them — do it here on every client so last
        // match's decoys don't carry over into the next one.
        BalBalAbility.ClearAllTrunks();

        // Force every ability/kill button back to ready right away, rather
        // than leaving it dimmed/counting down from whatever cooldown
        // happened to still be running when the match ended.
        AbilityCooldownUI.Instance?.ResetAll();
    }
}
