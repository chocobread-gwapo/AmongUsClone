using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class MeetingManager : NetworkBehaviour
{
    public static MeetingManager Instance { get; private set; }

    [Header("Meeting Settings")]
    [Tooltip("Fallback used only if GameStartManager isn't found: normal play reads GameStartManager.Instance.MeetingDuration instead, so the Host Settings Panel's value actually takes effect.")]
    [SerializeField] private float votingDuration = 30f;
    [SerializeField] private float reportRange = 1.5f;

    // Everyone needs to know a meeting is happening (to freeze movement,
    // show the voting panel, etc.) — this one is fine to sync to everyone.
    private NetworkVariable<bool> isMeetingActive = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsMeetingActive => isMeetingActive.Value;

    // Who has already voted this meeting (NOT who they voted for), so every client can show a
    // "voted / waiting" badge next to each player. Kept in step with the server-only votes dictionary.
    private readonly NetworkList<ulong> votedClients = new NetworkList<ulong>(
        null, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    public bool HasVoted(ulong clientId) => votedClients.Contains(clientId);

    private void SyncVoted()
    {
        if (!IsServer) return;
        bool same = votedClients.Count == votes.Count;
        if (same) foreach (var id in votes.Keys) if (!votedClients.Contains(id)) { same = false; break; }
        if (same) return;
        votedClients.Clear();
        foreach (var id in votes.Keys) votedClients.Add(id);
    }

    [Header("Emergency Meeting Rules")]
    [Tooltip("How many emergency meetings each player may call per match (body reports are unlimited).")]
    [SerializeField] private int emergencyCallsPerPlayer = 1;
    [Tooltip("Seconds after a match starts, and after every meeting ends, before an emergency meeting can be called.")]
    [SerializeField] private float emergencyCooldown = 20f;

    // Server time (seconds) at which emergency calls unlock; synced so every client can show a countdown.
    private readonly NetworkVariable<double> emergencyReadyAt = new NetworkVariable<double>(
        0, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private readonly Dictionary<ulong, int> emergencyCallsUsed = new Dictionary<ulong, int>();
    private bool sawGameStarted;

    // Host-set values from the Game Settings panel win; the Inspector values are the fallback.
    public int EmergencyCallsPerPlayer => GameStartManager.Instance != null ? GameStartManager.Instance.EmergencyMeetings : emergencyCallsPerPlayer;
    private float EmergencyCooldownSeconds => GameStartManager.Instance != null ? GameStartManager.Instance.EmergencyCooldown : emergencyCooldown;
    private float DiscussionSeconds => GameStartManager.Instance != null ? GameStartManager.Instance.DiscussionTime : 0f;
    private float serverDiscussionEndsAt;
    public float EmergencyCooldownRemaining =>
        NetworkManager.Singleton == null ? 0f : Mathf.Max(0f, (float)(emergencyReadyAt.Value - NetworkManager.Singleton.ServerTime.Time));

    // Local client's remaining emergency calls (set by the server via RPC; -1 = unknown, treat as full).
    public static int LocalCallsLeft = -1;
    public static event System.Action<string> EmergencyDenied;

    private void Update()
    {
        if (!IsServer) return;
        bool started = GameStartManager.Instance != null && GameStartManager.Instance.GameStarted;
        if (started && !sawGameStarted)
        {
            emergencyCallsUsed.Clear();
            emergencyReadyAt.Value = NetworkManager.ServerTime.Time + EmergencyCooldownSeconds;
            SyncCallsLeftClientRpc(EmergencyCallsPerPlayer);
        }
        sawGameStarted = started;
    }

    [ClientRpc]
    private void SyncCallsLeftClientRpc(int left, ClientRpcParams rpcParams = default)
    {
        LocalCallsLeft = left;
    }

    [ClientRpc]
    private void EmergencyDeniedClientRpc(string message, ClientRpcParams rpcParams = default)
    {
        EmergencyDenied?.Invoke(message);
    }

    private static ClientRpcParams Only(ulong id)
    {
        return new ClientRpcParams { Send = new ClientRpcSendParams { TargetClientIds = new[] { id } } };
    }

    // Called by DisconnectHandler on the server after a player drops. Forgets their
    // vote and emergency-call count, and finishes an open vote if everyone left has voted.
    public void ServerOnPlayerLeft(ulong clientId)
    {
        if (!IsServer) return;
        votes.Remove(clientId); SyncVoted();
        emergencyCallsUsed.Remove(clientId);
        if (isMeetingActive.Value && AllAlivePlayersHaveVoted()) ResolveVotes();
    }

    private void ServerOnMeetingEnded()
    {
        emergencyReadyAt.Value = NetworkManager.ServerTime.Time + EmergencyCooldownSeconds;
    }

    // Server-only state — votes are tallied here, never exposed directly to clients
    private Dictionary<ulong, ulong> votes = new Dictionary<ulong, ulong>(); // voterClientId -> votedClientId
    private const ulong SKIP_VOTE = ulong.MaxValue;
    private Coroutine votingCoroutine;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    // ---------- Reporting a body ----------

    [ServerRpc(RequireOwnership = false)]
    public void RequestReportBodyServerRpc(ulong bodyNetworkObjectId, ServerRpcParams rpcParams = default)
    {
        if (isMeetingActive.Value) return;

        // Ghosts can't report bodies — this is the real authority, the
        // client-side TryReport() check is just a UX nicety.
        ulong reporterId = rpcParams.Receive.SenderClientId;
        if (!TryGetPlayerRoleByClientId(reporterId, out PlayerRole reporterRole) || !reporterRole.IsAlive) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(bodyNetworkObjectId, out NetworkObject bodyObj))
            return;

        // bodyNetworkObjectId now usually points at a stationary DeadBody
        // marker rather than the dead player's own object (ghosts keep
        // moving after death, so the body can't just be them) — resolve
        // back to the real PlayerRole it represents either way, so the
        // Bal-Bal / alive checks below stay exactly as they were.
        PlayerRole bodyRole;
        bool orphanBody = false; // the dead player disconnected; their body can still be reported
        DeadBody deadBody = bodyObj.GetComponent<DeadBody>();
        if (deadBody != null)
        {
            if (NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(deadBody.OriginalPlayerId, out NetworkObject originalPlayerObj))
                bodyRole = originalPlayerObj.GetComponent<PlayerRole>();
            else
            {
                bodyRole = null;
                orphanBody = true;
            }
        }
        else
        {
            bodyRole = bodyObj.GetComponent<PlayerRole>();
        }

        if (!orphanBody && (bodyRole == null || bodyRole.IsAlive)) return; // can't report a living player

        // Bal-bal's victim leaves no trace — the real body is never
        // reportable, even by someone standing right on top of it.
        if (bodyRole != null && bodyRole.IsBalBalConsumed) return;

        Debug.Log($"[Server] Body reported. Starting meeting.");
        StartMeeting();
    }

    // ---------- Calling an emergency meeting (no body needed) ----------

    [ServerRpc(RequireOwnership = false)]
    public void RequestCallMeetingServerRpc(ServerRpcParams rpcParams = default)
    {
        if (isMeetingActive.Value) return;

        ulong callerId = rpcParams.Receive.SenderClientId;

        // Ghosts can't call meetings — this is the real authority, the
        // client-side CallMeetingButton check is just a UX nicety.
        if (!TryGetPlayerRoleByClientId(callerId, out PlayerRole callerRole) || !callerRole.IsAlive) return;

        if (GameStartManager.Instance == null || !GameStartManager.Instance.GameStarted) return;
        if (GameResultManager.Instance != null && GameResultManager.Instance.GameEnded) return;

        float wait = EmergencyCooldownRemaining;
        if (wait > 0f)
        {
            EmergencyDeniedClientRpc("Emergency meeting is on cooldown (" + Mathf.CeilToInt(wait) + "s).", Only(callerId));
            return;
        }

        emergencyCallsUsed.TryGetValue(callerId, out int used);
        if (used >= EmergencyCallsPerPlayer)
        {
            EmergencyDeniedClientRpc("You have no emergency meetings left.", Only(callerId));
            return;
        }
        emergencyCallsUsed[callerId] = used + 1;
        SyncCallsLeftClientRpc(EmergencyCallsPerPlayer - (used + 1), Only(callerId));

        Debug.Log($"[Server] Emergency meeting called by client {callerId} ({used + 1}/{EmergencyCallsPerPlayer}).");
        StartMeeting();
    }

    // ---------- Core meeting flow ----------

    // Server-only — when this meeting actually started, so ResolveVotes()/
    // ResolveJudgment() can work out exactly how long it ran and give that
    // time back to everyone's cooldowns.
    private float serverMeetingStartTime;

    private void StartMeeting()
    {
        if (!IsServer || isMeetingActive.Value) return;

        // Matches the familiar "body disappears once the meeting begins"
        // behavior, whether this meeting was triggered by a report or an
        // emergency call.
        DeadBody.DespawnAll();

        // Everyone is moved back to the spawn area the moment a meeting starts. The meeting screen hides
        // the map, so nobody sees the jump, and the match resumes right at the table afterwards.
        SceneSpawnPoints.PlaceAllPlayers();

        serverMeetingStartTime = Time.time;
        serverDiscussionEndsAt = Time.time + DiscussionSeconds;

        votes.Clear(); SyncVoted();
        isMeetingActive.Value = true;

        if (votingCoroutine != null) StopCoroutine(votingCoroutine);
        votingCoroutine = StartCoroutine(VotingTimer());
    }

    // Pushes every connected player's currently-active cooldowns (kill +
    // every ability) forward by exactly how long this meeting ran. Meetings
    // already block kills/abilities entirely (PlayerController.IsBlocked()),
    // so without this, time spent in a meeting would just silently eat into
    // a cooldown the player never got a chance to use. Only touches a
    // cooldown that's actually still counting down right now — a player
    // with nothing on cooldown is left untouched. The client-side countdown
    // (AbilityCooldownUI) independently freezes itself while a meeting is
    // active, so the two stay in sync without needing an extra RPC here.
    private void ExtendAllCooldownsByMeetingDuration()
    {
        float elapsed = Time.time - serverMeetingStartTime;
        if (elapsed <= 0f) return;

        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject == null) continue;

            client.PlayerObject.GetComponent<PlayerController>()?.ServerExtendKillCooldownIfActive(elapsed);
            client.PlayerObject.GetComponent<TikbalangAbility>()?.ServerExtendCooldownIfActive(elapsed);
            client.PlayerObject.GetComponent<AswangAbility>()?.ServerExtendCooldownIfActive(elapsed);
            client.PlayerObject.GetComponent<KapreAbility>()?.ServerExtendCooldownIfActive(elapsed);
            client.PlayerObject.GetComponent<ThirdEyeAbility>()?.ServerExtendCooldownIfActive(elapsed);
            client.PlayerObject.GetComponent<ReikiAbility>()?.ServerExtendCooldownIfActive(elapsed);
            client.PlayerObject.GetComponent<AlbularyoAbility>()?.ServerExtendCooldownIfActive(elapsed);
            client.PlayerObject.GetComponent<TarotUserAbility>()?.ServerExtendCooldownIfActive(elapsed);
        }
    }

    private IEnumerator VotingTimer()
    {
        float duration = GameStartManager.Instance != null ? GameStartManager.Instance.MeetingDuration : votingDuration;
        yield return new WaitForSeconds(DiscussionSeconds + duration);
        ResolveVotes();
    }

    [ServerRpc(RequireOwnership = false)]
    public void CastVoteServerRpc(ulong votedClientId, ServerRpcParams rpcParams = default)
    {
        if (!isMeetingActive.Value) return;

        ulong voterId = rpcParams.Receive.SenderClientId;

        // Only alive players can vote — verify server-side
        if (!TryGetPlayerRoleByClientId(voterId, out PlayerRole voterRole) || !voterRole.IsAlive)
            return;

        if (Time.time < serverDiscussionEndsAt) return; // still discussing

        votes[voterId] = votedClientId; // votedClientId == SKIP_VOTE means they chose to skip
        SyncVoted();
        Debug.Log($"[Server] Client {voterId} voted for {(votedClientId == SKIP_VOTE ? "SKIP" : votedClientId.ToString())}");

        if (AllAlivePlayersHaveVoted())
        {
            ResolveVotes();
        }
    }

    private bool AllAlivePlayersHaveVoted()
    {
        int aliveCount = 0;
        foreach (var client in NetworkManager.Singleton.ConnectedClientsList)
        {
            if (client.PlayerObject != null)
            {
                PlayerRole role = client.PlayerObject.GetComponent<PlayerRole>();
                if (role != null && role.IsAlive) aliveCount++;
            }
        }
        return votes.Count >= aliveCount;
    }

    private void ResolveVotes()
    {
        if (votingCoroutine != null)
        {
            StopCoroutine(votingCoroutine);
            votingCoroutine = null;
        }

        Dictionary<ulong, int> tally = new Dictionary<ulong, int>();
        foreach (var vote in votes.Values)
        {
            if (vote == SKIP_VOTE) continue;
            if (!NetworkManager.Singleton.ConnectedClients.ContainsKey(vote)) continue; // target left
            if (!tally.ContainsKey(vote)) tally[vote] = 0;
            tally[vote]++;
        }

        ulong ejectedClientId = SKIP_VOTE;
        int highestVotes = 0;
        bool isTie = false;

        foreach (var kvp in tally)
        {
            if (kvp.Value > highestVotes)
            {
                highestVotes = kvp.Value;
                ejectedClientId = kvp.Key;
                isTie = false;
            }
            else if (kvp.Value == highestVotes && highestVotes > 0)
            {
                isTie = true;
            }
        }

        string ejectedName = "No one";
        bool ejectedWasImpostor = false;

        if (!isTie && ejectedClientId != SKIP_VOTE && highestVotes > 0)
        {
            if (TryGetPlayerRoleByClientId(ejectedClientId, out PlayerRole ejectedRole))
            {
                ejectedWasImpostor = ejectedRole.IsImpostor;
                ejectedName = PlayerLabel.DisplayName(ejectedRole.OwnerClientId);
                ejectedRole.Kill(); // reuse existing kill/fade logic for ejection too
            }
        }

        Debug.Log($"[Server] Meeting resolved. Ejected: {ejectedName}, Was Impostor: {ejectedWasImpostor}, Tie: {isTie}");

        // Kill() above already ran GameResultManager.CheckWinConditions()
        // synchronously — if that ejection just ended the match, skip the
        // meeting's own result announcement entirely and let GameOverUI's
        // panel (already shown by now) be the only thing players see,
        // instead of a brief ejection message immediately replaced by it.
        bool matchJustEnded = GameResultManager.Instance != null && GameResultManager.Instance.GameEnded;
        if (!matchJustEnded)
        {
            AnnounceResultsClientRpc(ejectedName, ejectedWasImpostor, isTie || ejectedClientId == SKIP_VOTE, BuildVoteDetails());
        }

        ExtendAllCooldownsByMeetingDuration();
        ServerOnMeetingEnded();
        votes.Clear(); SyncVoted();
        isMeetingActive.Value = false;
    }

    // ---------- Babaylan's Judge Skill: bypasses voting entirely ----------
    // Called by BabaylanAbility.JudgeServerRpc() once it's already validated
    // the caller IS the Babaylan, is alive, hasn't judged before, and a
    // meeting is active. Ends the meeting immediately — no vote tally.
    public void ResolveJudgment(ulong judgeClientId, ulong targetClientId)
    {
        if (!IsServer || !isMeetingActive.Value) return;

        if (votingCoroutine != null)
        {
            StopCoroutine(votingCoroutine);
            votingCoroutine = null;
        }

        if (!TryGetPlayerRoleByClientId(judgeClientId, out PlayerRole judgeRole) ||
            !TryGetPlayerRoleByClientId(targetClientId, out PlayerRole targetRole))
        {
            // Shouldn't happen (BabaylanAbility already validated the caller),
            // but bail out cleanly rather than leaving the meeting stuck.
            votes.Clear(); SyncVoted();
            isMeetingActive.Value = false;
            return;
        }

        bool guessWasCorrect = targetRole.IsImpostor;
        string banishedName;
        bool banishedWasImpostor;

        if (guessWasCorrect)
        {
            targetRole.Kill();
            banishedName = PlayerLabel.DisplayName(targetRole.OwnerClientId);
            banishedWasImpostor = true;
        }
        else
        {
            // Wrong guess — the Babaylan pays for it instead.
            judgeRole.Kill();
            banishedName = PlayerLabel.DisplayName(judgeRole.OwnerClientId);
            banishedWasImpostor = judgeRole.IsImpostor; // always false — Babaylan is a Survivor Class, Crewmate-only
        }

        Debug.Log($"[Server] Babaylan's judgment: {judgeRole.gameObject.name} judged {targetRole.gameObject.name}: {(guessWasCorrect ? "correct, target banished" : "WRONG, Babaylan banished instead")}.");

        // Same as ResolveVotes() — if the Kill() above just ended the match,
        // skip the judgment announcement and let GameOverUI be the only
        // panel players see.
        bool matchJustEnded = GameResultManager.Instance != null && GameResultManager.Instance.GameEnded;
        if (!matchJustEnded)
        {
            AnnounceJudgmentResultClientRpc(PlayerLabel.DisplayName(judgeRole.OwnerClientId), PlayerLabel.DisplayName(targetRole.OwnerClientId), guessWasCorrect, banishedName);
        }

        ExtendAllCooldownsByMeetingDuration();
        ServerOnMeetingEnded();
        votes.Clear(); SyncVoted();
        isMeetingActive.Value = false;
    }

    [ClientRpc]
    private void AnnounceJudgmentResultClientRpc(string judgeName, string targetName, bool guessWasCorrect, string banishedName)
    {
        string message = guessWasCorrect
            ? $"{judgeName} used the Babaylan's judgment on {targetName}: correct! {banishedName} was a Maligno and has been exiled to the forest."
            : $"{judgeName} used the Babaylan's judgment on {targetName}: WRONG. {banishedName} the Babaylan has been exiled to the forest instead.";

        Debug.Log($"[Client] {message}");
        MeetingUI.Instance?.ShowResult(message);
    }

    private bool TryGetPlayerRoleByClientId(ulong clientId, out PlayerRole role)
    {
        role = null;
        if (NetworkManager.Singleton.ConnectedClients.TryGetValue(clientId, out var client))
        {
            if (client.PlayerObject != null)
            {
                role = client.PlayerObject.GetComponent<PlayerRole>();
                return role != null;
            }
        }
        return false;
    }

    [ClientRpc]
    private void AnnounceResultsClientRpc(string ejectedName, bool wasImpostor, bool noOneEjected, string voteDetails)
    {
        // MeetingUI listens for this via the isMeetingActive value change instead,
        // but this also fires so UI can show a specific result message.
        string message = noOneEjected
            ? "No one was exiled to the forest (tie or skipped)."
            : (GameStartManager.Instance == null || GameStartManager.Instance.ConfirmEjects)
                ? $"{ejectedName} was exiled to the forest. They {(wasImpostor ? "WERE" : "were NOT")} a Maligno."
                : $"{ejectedName} was exiled to the forest.";
        if (!string.IsNullOrEmpty(voteDetails)) message += "\n" + voteDetails;

        Debug.Log($"[Client] {message}");
        MeetingUI.Instance?.ShowResult(message);
    }

    // With Anonymous Votes off, the result also says who voted for whom.
    private string BuildVoteDetails()
    {
        if (GameStartManager.Instance == null || GameStartManager.Instance.AnonymousVotes || votes.Count == 0) return "";
        var lines = new List<string>();
        foreach (var kvp in votes)
        {
            string voter = PlayerLabel.DisplayName(kvp.Key);
            lines.Add(kvp.Value == SKIP_VOTE ? $"{voter} skipped." : $"{voter} voted {PlayerLabel.DisplayName(kvp.Value)}.");
        }
        return string.Join("\n", lines);
    }

    public static ulong SkipVoteValue => SKIP_VOTE;
}
