using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using Random = UnityEngine.Random;

// The actual trap object a Duwende places. Networked (server-spawned) so every
// client sees the same trap exist and trigger at the same moment.
[RequireComponent(typeof(Collider2D))]
[RequireComponent(typeof(NetworkObject))]
public class NunoMoundTrap : NetworkBehaviour
{
    [Header("Trap Effect")]
    [Tooltip("How much the curse slows/dims the victim. The curse itself is indefinite now — CurseStatus owns how long it lasts and what happens if it isn't cured (see its Death Timer Seconds field).")]
    [SerializeField] private float slowMultiplier = 0.5f;
    [SerializeField] private float visionReduction = 0.6f;

    // Raised server-side so DuwendeAbility can drop this trap from its active-count tracking.
    public event Action<NunoMoundTrap> OnConsumed;

    // Server-only registry of every currently-active trap across all Duwendes —
    // DuwendeAbility only tracks its own placer's traps, but Third Eye Sight
    // needs to reveal ALL active traps regardless of who placed them.
    public static readonly List<NunoMoundTrap> ActiveTraps = new List<NunoMoundTrap>();

    [Header("Hidden mound (everyone except Maligno)")]
    [Tooltip("Within this distance a Villager hears a faint pulse from the mound, even though they cannot see it.")]
    [SerializeField] private float hearRadius = 6f;
    [SerializeField] private float quietVolume = 0.05f;
    [SerializeField] private float loudVolume = 0.7f;
    [Tooltip("Seconds between pulses at the edge of hearing, and when standing right next to the mound.")]
    [SerializeField] private float farInterval = 1.8f;
    [SerializeField] private float nearInterval = 0.35f;

    private bool triggered = false;
    private Renderer[] renderers;
    private TMPro.TMP_Text[] texts;
    private WorldLabel[] labels;
    private bool visibleToMe = true;
    private bool localMaligno;
    private float revealUntil;
    private float nextVisCheck;
    private float nextPulse;
    private AudioSource pulseSource;
    private static AudioClip pulseClip;

    // Server-only — set right after spawn by DuwendeAbility so the Duwende
    // can't immediately trip their own trap (it spawns on top of them).
    private ulong placerClientId;
    private bool placerSet = false;

    private void Awake()
    {
        GetComponent<Collider2D>().isTrigger = true;
    }

    public override void OnNetworkSpawn()
    {
        if (IsServer) ActiveTraps.Add(this);

        renderers = GetComponentsInChildren<Renderer>(true);
        texts = GetComponentsInChildren<TMPro.TMP_Text>(true);
        labels = GetComponentsInChildren<WorldLabel>(true);
        SetVisible(false);                       // hidden until we know the local player is a Maligno
        nextPulse = Time.time + Random.Range(0f, 1f);
    }

    // Only Maligno may see mounds. Everyone else finds them with Third Eye (which draws its own
    // markers) or hears them (see Update). Roles never change mid-match, so a slow check is plenty.
    private static bool LocalIsMaligno()
    {
        var nm = NetworkManager.Singleton;
        var obj = nm != null ? nm.LocalClient?.PlayerObject : null;
        var role = obj != null ? obj.GetComponent<PlayerRole>() : null;
        return role != null && role.IsImpostor;
    }

    // Called on the Third Eye user's own client: shows this real mound (with a soft glow so it is
    // visible even in the dark) for a few seconds.
    public void RevealFor(float seconds)
    {
        revealUntil = Time.time + seconds;
        var go = new GameObject("MoundRevealGlow");
        go.transform.SetParent(transform, false);
        var glow = go.AddComponent<UnityEngine.Rendering.Universal.Light2D>();
        glow.lightType = UnityEngine.Rendering.Universal.Light2D.LightType.Point;
        glow.color = new Color(0.8f, 0.6f, 1f);
        glow.intensity = 1f;
        glow.pointLightOuterRadius = 1.4f;
        Destroy(go, seconds);
    }

    private void SetVisible(bool on)
    {
        visibleToMe = on;
        if (renderers != null) foreach (var r in renderers) if (r != null) r.enabled = on;
        if (texts != null) foreach (var t in texts) if (t != null) t.enabled = on;
        if (labels != null) foreach (var l in labels) if (l != null) l.enabled = on;
    }

    private static AudioClip MakePulseClip()
    {
        // A soft low "thump": two decaying sine tones, built in code so no audio asset is needed.
        const int rate = 22050;
        int n = (int)(rate * 0.32f);
        var data = new float[n];
        for (int i = 0; i < n; i++)
        {
            float t = i / (float)rate;
            float f = Mathf.Lerp(120f, 70f, Mathf.Clamp01(t / 0.25f));
            float body = Mathf.Sin(2f * Mathf.PI * f * t) * Mathf.Exp(-9f * t);
            float over = Mathf.Sin(2f * Mathf.PI * 240f * t) * Mathf.Exp(-16f * t) * 0.25f;
            float attack = Mathf.Clamp01(t / 0.006f); // avoid a click at the start
            data[i] = (body + over) * attack * 0.9f;
        }
        var clip = AudioClip.Create("MoundPulse", n, 1, rate, false);
        clip.SetData(data, 0);
        return clip;
    }

    private void Update()
    {
        if (!IsSpawned || triggered) return;

        if (Time.time >= nextVisCheck)
        {
            nextVisCheck = Time.time + 0.5f;
            localMaligno = LocalIsMaligno();
        }
        bool show = localMaligno || Time.time < revealUntil;
        if (show != visibleToMe) SetVisible(show);
        if (localMaligno) return; // Maligno already see it; no pulse needed

        // Pulse for living Villagers who get close, faint far away and louder + faster up close.
        PlayerVision local = PlayerVision.LocalInstance;
        if (local == null) return;
        var role = local.GetComponent<PlayerRole>();
        if (role != null && !role.IsAlive) return;

        Vector2 d = transform.position - local.transform.position;
        float dist = d.magnitude;
        if (dist > hearRadius) return;
        if (Time.time < nextPulse) return;

        float closeness = 1f - dist / hearRadius;            // 0 at the edge, 1 on top of it
        nextPulse = Time.time + Mathf.Lerp(farInterval, nearInterval, closeness);

        if (pulseClip == null) pulseClip = MakePulseClip();
        if (pulseSource == null)
        {
            pulseSource = gameObject.AddComponent<AudioSource>();
            pulseSource.playOnAwake = false;
            pulseSource.spatialBlend = 0f;
        }
        pulseSource.panStereo = Mathf.Clamp(d.x / hearRadius, -1f, 1f) * 0.8f;
        float vol = Mathf.Lerp(quietVolume, loudVolume, closeness * closeness) * Sfx.MasterVolume;
        pulseSource.PlayOneShot(pulseClip, vol);
    }

    public override void OnNetworkDespawn()
    {
        if (IsServer) ActiveTraps.Remove(this);
    }

    public void ServerSetPlacer(ulong clientId)
    {
        if (!IsServer) return;
        placerClientId = clientId;
        placerSet = true;
    }

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (!IsServer || triggered) return;

        PlayerController targetController = other.GetComponent<PlayerController>();
        PlayerRole targetRole = other.GetComponent<PlayerRole>();
        NetworkObject targetNetObj = other.GetComponent<NetworkObject>();
        if (targetController == null || targetRole == null || !targetRole.IsAlive) return;

        // Never trigger on the Duwende who placed this trap.
        if (placerSet && targetNetObj != null && targetNetObj.OwnerClientId == placerClientId) return;

        // Maligno don't trigger each other's traps either.
        if (targetRole.IsImpostor) return;

        // Walking over a trap while already cursed does nothing — the trap stays armed.
        CurseStatus existing = other.GetComponent<CurseStatus>();
        if (existing != null && existing.IsCursed) return;

        triggered = true;

        // CurseStatus now owns the whole effect: it applies the persistent
        // slow + vision reduction itself AND starts the death timer. An
        // un-healed curse no longer wears off — only Albularyo can end it.
        CurseStatus curse = other.GetComponent<CurseStatus>();
        if (curse != null)
        {
            curse.ServerApplyCurse(slowMultiplier, visionReduction);
        }
        else
        {
            // Defensive fallback in case a player is somehow missing
            // CurseStatus — falls back to the old timed-effect behavior so a
            // trigger never does nothing at all.
            Debug.LogWarning($"{other.gameObject.name} has no CurseStatus — applying a one-off timed slow/vision effect instead of a real curse.");
            targetController.ServerApplySlow(slowMultiplier, 4f);
            other.GetComponent<PlayerVision>()?.ServerApplyVisionReduction(visionReduction, 4f);
        }

        Debug.Log($"[Server] {other.gameObject.name} triggered a Nuno mound trap.");

        OnConsumed?.Invoke(this);

        if (NetworkObject.IsSpawned)
        {
            NetworkObject.Despawn();
        }
    }
}