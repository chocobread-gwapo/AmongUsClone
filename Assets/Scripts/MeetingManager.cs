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

        // Ghosts can't report bodies — this is the real authority, the
        // client-side TryReport() check is just a UX nicety.
        ulong reporterId = rpcParams.Receive.SenderClientId;
        if (!TryGetPlayerRoleByClientId(reporterId, out PlayerRole reporterRole) || !reporterRole.IsAlive) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(bodyNetworkObjectId, out NetworkObject bodyObj))
            return;

        PlayerRole bodyRole = bodyObj.GetComponent<PlayerRole>();
        if (bodyRole == null || bodyRole.IsAlive) return; // can't report a living player

        // Bal-bal's victim leaves no trace — the real body is never
        // reportable, even by someone standing right on top of it.
        if (bodyRole.IsBalBalConsumed) return;

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

        // Kill() above already ran GameResultManager.CheckWinConditions()
        // synchronously — if that ejection just ended the match, skip the
        // meeting's own result announcement entirely and let GameOverUI's
        // panel (already shown by now) be the only thing players see,
        // instead of a brief ejection message immediately replaced by it.
        bool matchJustEnded = GameResultManager.Instance != null && GameResultManager.Instance.GameEnded;
        if (!matchJustEnded)
        {
            AnnounceResultsClientRpc(ejectedName, ejectedWasImpostor, isTie || ejectedClientId == SKIP_VOTE);
        }

        votes.Clear();
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
            votes.Clear();
            isMeetingActive.Value = false;
            return;
        }

        bool guessWasCorrect = targetRole.IsImpostor;
        string banishedName;
        bool banishedWasImpostor;

        if (guessWasCorrect)
        {
            targetRole.Kill();
            banishedName = targetRole.gameObject.name;
            banishedWasImpostor = true;
        }
        else
        {
            // Wrong guess — the Babaylan pays for it instead.
            judgeRole.Kill();
            banishedName = judgeRole.gameObject.name;
            banishedWasImpostor = judgeRole.IsImpostor; // always false — Babaylan is a Survivor Class, Crewmate-only
        }

        Debug.Log($"[Server] Babaylan's judgment: {judgeRole.gameObject.name} judged {targetRole.gameObject.name} — {(guessWasCorrect ? "correct, target banished" : "WRONG, Babaylan banished instead")}.");

        // Same as ResolveVotes() — if the Kill() above just ended the match,
        // skip the judgment announcement and let GameOverUI be the only
        // panel players see.
        bool matchJustEnded = GameResultManager.Instance != null && GameResultManager.Instance.GameEnded;
        if (!matchJustEnded)
        {
            AnnounceJudgmentResultClientRpc(judgeRole.gameObject.name, targetRole.gameObject.name, guessWasCorrect, banishedName);
        }

        votes.Clear();
        isMeetingActive.Value = false;
    }

    [ClientRpc]
    private void AnnounceJudgmentResultClientRpc(string judgeName, string targetName, bool guessWasCorrect, string banishedName)
    {
        string message = guessWasCorrect
            ? $"{judgeName} invoked Judge Skill on {targetName} — correct! {banishedName} was a Maligno and has been exiled to the forest."
            : $"{judgeName} invoked Judge Skill on {targetName} — WRONG. {banishedName} the Babaylan has been exiled to the forest instead.";

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
    private void AnnounceResultsClientRpc(string ejectedName, bool wasImpostor, bool noOneEjected)
    {
        // MeetingUI listens for this via the isMeetingActive value change instead,
        // but this also fires so UI can show a specific result message.
        string message = noOneEjected
            ? "No one was exiled to the forest (tie or skipped)."
            : $"{ejectedName} was exiled to the forest. They {(wasImpostor ? "WERE" : "were NOT")} a Maligno.";

        Debug.Log($"[Client] {message}");
        MeetingUI.Instance?.ShowResult(message);
    }

    public static ulong SkipVoteValue => SKIP_VOTE;
}