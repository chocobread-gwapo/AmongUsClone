using System.Collections;
using Unity.Netcode;
using UnityEngine;

// Tracks whether this player is currently "cursed" by Duwende's Nuno mound
// trap. A curse is INDEFINITE — unlike a normal timed status effect, it does
// NOT wear off on its own. Instead it does two things at once:
//   1. Applies a persistent slow + vision reduction (via PlayerController /
//      PlayerVision — reusing their existing coroutines, just with an
//      effectively-infinite duration so their own auto-revert never fires
//      in practice).
//   2. Starts a death timer: if nobody cures the victim within
//      deathTimerSeconds, they die — the same TryKill() path as a normal
//      Maligno kill, so it counts toward the Impostors' win condition and
//      leaves a normal reportable body.
//
// Only Albularyo's ServerCure() can stop this early. That's the whole point:
// a curse is a real threat, not a minor inconvenience, and Albularyo is the
// only thing standing between it and a death.
//
// Synced to everyone on purpose — same pattern as controlsInverted/slowMultiplier:
// it's a visible status effect, not secret information, so Albularyo (and
// anyone else) can see who's cursed without a server round-trip first.
public class CurseStatus : NetworkBehaviour
{
    [Tooltip("Child visual (e.g. a red glowing marker) shown above this player while cursed. Purely local/cosmetic — toggled on every client from the NetworkVariable below, since it's a visible status effect everyone should be able to see.")]
    [SerializeField] private GameObject curseVisual;

    [Header("Curse — Slow Death")]
    [Tooltip("How long an un-healed curse takes to kill the victim, in seconds.")]
    [SerializeField] private float deathTimerSeconds = 20f;

    // Effectively-infinite duration passed to PlayerController/PlayerVision's
    // own timed-revert coroutines — a curse is meant to persist until
    // ServerCure() ends it, not time out on its own.
    private const float PersistentEffectDuration = 9999f;

    private readonly NetworkVariable<bool> isCursed = new NetworkVariable<bool>(
        false,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public bool IsCursed => isCursed.Value;

    private Coroutine deathRoutine;
    private const string CurseTag = "curse";

    [ClientRpc]
    private void CurseSfxClientRpc(Vector3 pos)
    {
        Sfx.PlayAt("trap", pos);
    }

    // Removes only the curse's own slow/vision effects (other effects like
    // Third Eye or Tarot are left alone).
    private void ClearCurseEffects()
    {
        GetComponent<PlayerController>()?.ServerClearSlow(CurseTag);
        GetComponent<PlayerVision>()?.ServerClearVisionReduction(CurseTag);
    }

    // Called by PlayerRole.ServerResetForNewGame so a curse never carries over into the next match.
    public void ServerReset()
    {
        if (!IsServer) return;
        if (deathRoutine != null) { StopCoroutine(deathRoutine); deathRoutine = null; }
        isCursed.Value = false;
        ClearCurseEffects();
    }

    public override void OnNetworkSpawn()
    {
        isCursed.OnValueChanged += HandleCursedChanged;
        ApplyVisual(isCursed.Value);
    }

    public override void OnNetworkDespawn()
    {
        isCursed.OnValueChanged -= HandleCursedChanged;
    }

    private void HandleCursedChanged(bool previous, bool current) => ApplyVisual(current);

    private void ApplyVisual(bool cursed)
    {
        if (curseVisual != null)
        {
            curseVisual.SetActive(cursed);
        }
    }

    // Called by NunoMoundTrap when it triggers. Applies the curse: persistent
    // slow + vision reduction, plus a death timer that kills the victim if
    // nobody cures them in time.
    public void ServerApplyCurse(float slowMultiplier, float visionMultiplier)
    {
        if (!IsServer) return;
        if (isCursed.Value) return; // already cursed — never stack a second curse

        isCursed.Value = true;
        CurseSfxClientRpc(transform.position);

        GetComponent<PlayerController>()?.ServerApplySlow(slowMultiplier, PersistentEffectDuration, CurseTag);
        GetComponent<PlayerVision>()?.ServerApplyVisionReduction(visionMultiplier, PersistentEffectDuration, CurseTag);

        if (deathRoutine != null) StopCoroutine(deathRoutine);
        deathRoutine = StartCoroutine(DeathTimerRoutine());
    }

    private IEnumerator DeathTimerRoutine()
    {
        PlayerRole role = GetComponent<PlayerRole>();
        float remaining = deathTimerSeconds;

        while (remaining > 0f)
        {
            yield return null;

            // Died some other way (kill, ejection, judgment) — nothing left to curse.
            if (role != null && !role.IsAlive)
            {
                isCursed.Value = false;
                ClearCurseEffects();
                deathRoutine = null;
                yield break;
            }

            // The clock pauses during meetings, so a curse can't kill someone mid-vote.
            bool inMeeting = MeetingManager.Instance != null && MeetingManager.Instance.IsMeetingActive;
            if (!inMeeting) remaining -= Time.deltaTime;
        }

        if (!isCursed.Value) yield break; // cured in the meantime

        Killable killable = GetComponent<Killable>();
        if (killable != null && killable.TryKill())
        {
            Debug.Log($"[Server] {gameObject.name} succumbed to Duwende's curse.");
        }

        // Dead players shouldn't stay slowed / dimmed as ghosts.
        isCursed.Value = false;
        ClearCurseEffects();
        deathRoutine = null;
    }

    // Called by AlbularyoAbility — ends the curse (and the slow/vision
    // effects it came with) immediately, and cancels the death timer.
    public void ServerCure()
    {
        if (!IsServer) return;
        if (!isCursed.Value) return;

        if (deathRoutine != null)
        {
            StopCoroutine(deathRoutine);
            deathRoutine = null;
        }
        isCursed.Value = false;

        ClearCurseEffects();
    }
}