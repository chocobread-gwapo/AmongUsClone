using System.Collections;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

public class MeetingManager : NetworkBehaviour
{
    public static MeetingManager Instance { get; private set; }

    [Header("Meeting Settings")]
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

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(bodyNetworkObjectId, out NetworkObject bodyObj))
            return;

        PlayerRole bodyRole = bodyObj.GetComponent<PlayerRole>();
        if (bodyRole == null || bodyRole.IsAlive) return; // can't report a living player

        Debug.Log($"[Server] Body reported. Starting meeting.");
        StartMeeting();
    }

    // ---------- Calling an emergency meeting (no body needed) ----------

    [ServerRpc(RequireOwnership = false)]
    public void RequestCallMeetingServerRpc(ServerRpcParams rpcParams = default)
    {
        if (isMeetingActive.Value) return;

        ulong callerId = rpcParams.Receive.SenderClientId;
        // (Optional future improvement: track per-player meeting call limits here)

        Debug.Log($"[Server] Emergency meeting called by client {callerId}.");
        StartMeeting();
    }

    // ---------- Core meeting flow ----------

    private void StartMeeting()
    {
        if (!IsServer || isMeetingActive.Value) return;

        votes.Clear();
        isMeetingActive.Value = true;

        if (votingCoroutine != null) StopCoroutine(votingCoroutine);
        votingCoroutine = StartCoroutine(VotingTimer());
    }

    private IEnumerator VotingTimer()
    {
        yield return new WaitForSeconds(votingDuration);
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

        votes[voterId] = votedClientId; // votedClientId == SKIP_VOTE means they chose to skip
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
                ejectedName = ejectedRole.gameObject.name;
                ejectedRole.Kill(); // reuse existing kill/fade logic for ejection too
            }
        }

        Debug.Log($"[Server] Meeting resolved. Ejected: {ejectedName}, Was Impostor: {ejectedWasImpostor}, Tie: {isTie}");

        AnnounceResultsClientRpc(ejectedName, ejectedWasImpostor, isTie || ejectedClientId == SKIP_VOTE);

        votes.Clear();
        isMeetingActive.Value = false;
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
    private void AnnounceResultsClientRpc(string ejectedName, bool wasImpostor, bool noOneEjected)
    {
        // MeetingUI listens for this via the isMeetingActive value change instead,
        // but this also fires so UI can show a specific result message.
        string message = noOneEjected
            ? "No one was ejected (tie or skipped)."
            : $"{ejectedName} was ejected. They {(wasImpostor ? "WERE" : "were NOT")} the Impostor.";

        Debug.Log($"[Client] {message}");
        MeetingUI.Instance?.ShowResult(message);
    }

    public static ulong SkipVoteValue => SKIP_VOTE;
}