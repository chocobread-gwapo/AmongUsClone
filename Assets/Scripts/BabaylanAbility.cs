using Unity.Netcode;
using UnityEngine;

// Babaylan — Survivor Class "Judge Skill"
// Meeting-only, usable ONCE PER MATCH: instantly condemns a chosen player,
// bypassing the vote entirely — no cooldown, since single-use already is
// the balancing factor. If the guess is wrong (the target isn't a Maligno),
// the Babaylan themselves gets banished instead.
//
// Unlike every other ability so far, this ISN'T driven by PlayerController's
// shared ABILITY button — it needs a player-picker, which MeetingUI already
// provides for voting. MeetingUI calls TryJudge() directly on the local
// Babaylan's instance (found via LocalInstance, same pattern as
// PlayerVision.LocalInstance) when its Judge Mode toggle is active and a
// player button is clicked.
public class BabaylanAbility : NetworkBehaviour
{
    // So MeetingUI (a plain MonoBehaviour, not itself networked) can cheaply
    // reach "the local player's own BabaylanAbility, if they have one."
    public static BabaylanAbility LocalInstance { get; private set; }

    private PlayerRole myRole;

    // Server-only — the real guard against reuse.
    private bool hasJudged = false;

    // Client-side (owner only) — only set true once the SERVER confirms the
    // judgment actually went through, not optimistically on click. That way
    // a judgment rejected server-side (e.g. the meeting ended a split second
    // earlier) never permanently locks out a Babaylan who never actually used it.
    private bool localHasJudged = false;
    public bool LocalHasJudged => localHasJudged;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    public override void OnNetworkSpawn()
    {
        if (IsOwner)
        {
            LocalInstance = this;
        }
    }

    public override void OnNetworkDespawn()
    {
        if (LocalInstance == this)
        {
            LocalInstance = null;
        }
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.CurrentSurvivorClass == SurvivorClass.Babaylan;
    }

    // Called by MeetingUI, locally, for the owner only.
    public void TryJudge(ulong targetClientId)
    {
        if (!IsOwner || !IsMyAbility()) return;
        if (localHasJudged) return;

        JudgeServerRpc(targetClientId);
    }

    [ServerRpc]
    private void JudgeServerRpc(ulong targetClientId)
    {
        if (!IsMyAbility()) return;
        if (hasJudged) return;
        if (myRole == null || !myRole.IsAlive) return;
        if (MeetingManager.Instance == null || !MeetingManager.Instance.IsMeetingActive) return;

        hasJudged = true;
        ConfirmJudgeUsedClientRpc();

        MeetingManager.Instance.ResolveJudgment(OwnerClientId, targetClientId);
    }

    [ClientRpc]
    private void ConfirmJudgeUsedClientRpc()
    {
        if (!IsOwner) return;
        localHasJudged = true;
    }

    // Called by GameStartManager.HostReturnToLobby() so a fresh match gives
    // Babaylan their one use back.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;
        hasJudged = false;
        ResetLocalJudgedClientRpc();
    }

    [ClientRpc]
    private void ResetLocalJudgedClientRpc()
    {
        if (!IsOwner) return;
        localHasJudged = false;
    }
}