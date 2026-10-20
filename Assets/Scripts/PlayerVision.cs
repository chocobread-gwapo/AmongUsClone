using System.Collections;
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

    public float CurrentVisionRadius => baseVisionRadius * visionMultiplier.Value;

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
        visionLight.pointLightOuterRadius = baseVisionRadius * visionMultiplier.Value;
    }

    // Called server-side by an ability (Kapre's Tabako Smoke, so far) to
    // temporarily shrink this player's vision radius. Same shape as
    // PlayerController.ServerApplySlow — last reduction to end "wins" the
    // revert, so overlapping smoke clouds don't prematurely restore vision.
    public void ServerApplyVisionReduction(float multiplier, float duration)
    {
        if (!IsServer) return;
        StartCoroutine(ApplyVisionReductionRoutine(multiplier, duration));
    }

    // Called by CurseStatus.ServerCure() (Albularyo's heal) to end a vision
    // reduction early. Same idempotent-safe shape as PlayerController's
    // ServerClearSlow — the original routine's own revert check just no-ops.
    public void ServerClearVisionReduction()
    {
        if (!IsServer) return;
        visionMultiplier.Value = 1f;
    }

    private IEnumerator ApplyVisionReductionRoutine(float multiplier, float duration)
    {
        visionMultiplier.Value = multiplier;
        yield return new WaitForSeconds(duration);
        if (visionMultiplier.Value == multiplier)
        {
            visionMultiplier.Value = 1f;
        }
    }
}