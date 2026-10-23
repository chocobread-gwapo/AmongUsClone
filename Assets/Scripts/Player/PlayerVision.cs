using Unity.Netcode;
using UnityEngine;
using UnityEngine.Rendering.Universal;

// Fog-of-war vision radius for a single player. Sits on the Player prefab
// alongside PlayerController/PlayerRole. Each player owns a child Light2D
// that punches a visible circle out of the globally-dimmed scene — but that
// light is a purely local rendering concern, so only the OWNING client's own
// light is ever enabled. Nobody else's client cares about (or should be able
// to use) another player's light to infer their position.
public class PlayerVision : NetworkBehaviour
{
    // The local client's own vision, so other scripts (WorldLabel, for fog-of-war
    // label gating) can cheaply check "am I within sight?" without doing their
    // own NetworkManager/PlayerObject lookups every frame.
    public static PlayerVision LocalInstance { get; private set; }

    public float CurrentVisionRadius => (ghostSight ? ghostVisionRadius : baseVisionRadius * visionMultiplier.Value * RoleVision * transform.lossyScale.x); // world units

    [Header("Ghost")]
    [Tooltip("Vision radius (world units) for a dead local player, so ghosts can scout the map for their team.")]
    [SerializeField] private float ghostVisionRadius = 16f;
    private bool ghostSight;
    private PlayerRole role;

    // Host-set Maligno / Villager vision multipliers; only applied during a match.
    private float lastRoleVision = 1f;
    private float RoleVision
    {
        get
        {
            var g = GameStartManager.Instance;
            if (g == null || !g.GameStarted) return 1f;
            if (role == null) role = GetComponent<PlayerRole>();
            return (role != null && role.IsImpostor) ? g.MalignoVision : g.CrewVision;
        }
    }

    [Header("Vision")]
    [Tooltip("Child Light2D (Point/Freeform) that defines this player's visible radius. Only enabled for the owning client.")]
    [SerializeField] private Light2D visionLight;

    [Tooltip("Normal vision radius in world units, before any ability shrinks it (Kapre's smoke, etc).")]
    [SerializeField] private float baseVisionRadius = 3.5f;

    // Synced to everyone on purpose, same pattern as PlayerController's
    // slowMultiplier/controlsInverted — a status effect that's visibly true
    // for the affected player, not secret information.
    private readonly NetworkVariable<float> visionMultiplier = new NetworkVariable<float>(
        1f,
        NetworkVariableReadPermission.Everyone,
        NetworkVariableWritePermission.Server
    );

    public override void OnNetworkSpawn()
    {
        visionMultiplier.OnValueChanged += HandleVisionMultiplierChanged;

        if (visionLight != null)
        {
            // Only your own vision light should ever be lit for you.
            visionLight.enabled = IsOwner;
        }

        if (IsOwner)
        {
            LocalInstance = this;
        }

        ApplyRadius();
    }

    public override void OnNetworkDespawn()
    {
        visionMultiplier.OnValueChanged -= HandleVisionMultiplierChanged;

        if (LocalInstance == this)
        {
            LocalInstance = null;
        }
    }

    private void HandleVisionMultiplierChanged(float previous, float current) => ApplyRadius();

    private void ApplyRadius()
    {
        if (visionLight == null) return;
        visionLight.pointLightOuterRadius = ghostSight
            ? ghostVisionRadius / Mathf.Max(0.01f, transform.lossyScale.x)
            : baseVisionRadius * visionMultiplier.Value * RoleVision;
    }

    // Server-only list of active vision reductions. The synced multiplier is
    // the product of all unexpired ones, so overlapping effects (Third Eye,
    // Tarot Star, Kapre smoke) no longer clobber each other.
    private readonly System.Collections.Generic.List<(float mult, float end, string tag)> effects =
        new System.Collections.Generic.List<(float, float, string)>();

    public void ServerApplyVisionReduction(float multiplier, float duration, string tag = null)
    {
        if (!IsServer) return;
        effects.Add((multiplier, Time.time + duration, tag));
        Recompute();
    }

    // Called by CurseStatus.ServerCure() (Albularyo's heal) to end all reductions early.
    public void ServerClearVisionReduction(string tag = null)
    {
        if (!IsServer) return;
        if (tag == null) effects.Clear(); else effects.RemoveAll(e => e.tag == tag);
        Recompute();
    }

    private void Update()
    {
        // Local ghost sees much farther than the living.
        if (IsOwner)
        {
            if (role == null) role = GetComponent<PlayerRole>();
            bool g = role != null && !role.IsAlive;
            if (g != ghostSight) { ghostSight = g; ApplyRadius(); }
            float rv = RoleVision;
            if (!Mathf.Approximately(rv, lastRoleVision)) { lastRoleVision = rv; ApplyRadius(); }
        }

        if (!IsServer || effects.Count == 0) return;
        if (effects.RemoveAll(e => Time.time >= e.end) > 0) Recompute();
    }

    private void Recompute()
    {
        float m = 1f;
        foreach (var e in effects) m *= e.mult;
        if (!Mathf.Approximately(visionMultiplier.Value, m)) visionMultiplier.Value = m;
    }
}
