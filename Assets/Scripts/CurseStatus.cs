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

        isCursed.Value = true;

        GetComponent<PlayerController>()?.ServerApplySlow(slowMultiplier, PersistentEffectDuration);
        GetComponent<PlayerVision>()?.ServerApplyVisionReduction(visionMultiplier, PersistentEffectDuration);

        if (deathRoutine != null) StopCoroutine(deathRoutine);
        deathRoutine = StartCoroutine(DeathTimerRoutine());
    }

    private IEnumerator DeathTimerRoutine()
    {
        yield return new WaitForSeconds(deathTimerSeconds);

        // Could already be cured, or the player could already be dead some
        // other way — either way, nothing left to do here.
        if (!isCursed.Value) yield break;

        Killable killable = GetComponent<Killable>();
        if (killable != null && killable.TryKill())
        {
            Debug.Log($"[Server] {gameObject.name} succumbed to Duwende's curse.");
        }

        isCursed.Value = false;
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

        GetComponent<PlayerController>()?.ServerClearSlow();
        GetComponent<PlayerVision>()?.ServerClearVisionReduction();
    }
}