using Unity.Netcode;
using UnityEngine;

// Kapre — Tabako Smoke
// Conjures a localized smoke cloud covering a room/corridor for 8s, obscuring
// sightlines for everyone except the Kapre.
public class KapreAbility : NetworkBehaviour
{
    [Header("Tabako Smoke")]
    [Tooltip("A visual-only prefab (no NetworkObject needed) — each client spawns " +
             "its own local copy via ClientRpc, the same pattern BalBalAbility uses " +
             "for the banana trunk prop.")]
    [SerializeField] private GameObject smokeCloudPrefab;
    [SerializeField] private float smokeDuration = 8f;
    [SerializeField] private float cooldownDuration = 30f;

    [Header("Vision Obscuration")]
    [Tooltip("Radius around the cast point that gets checked for players to obscure.")]
    [SerializeField] private float smokeVisionRadius = 2.5f;
    [Tooltip("Vision-radius multiplier applied to anyone caught in the smoke (0.35 = 35% of normal vision).")]
    [SerializeField] private float smokeVisionMultiplier = 0.35f;

    private PlayerRole myRole;

    // Server-only.
    private float serverCooldownEndTime = -999f;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.CurrentMalignoType == MalignoType.Kapre;
    }

    public void TryActivate()
    {
        if (!IsOwner || !IsMyAbility()) return;
        ConjureSmokeServerRpc(transform.position);
    }

    [ServerRpc]
    private void ConjureSmokeServerRpc(Vector3 position)
    {
        if (!IsMyAbility()) return;
        if (Time.time < serverCooldownEndTime) return;

        serverCooldownEndTime = Time.time + cooldownDuration;
        NotifyCooldownStartedClientRpc(cooldownDuration);

        Debug.Log("[Server] Kapre conjured a smokescreen.");
        SpawnSmokeClientRpc(position, smokeDuration, OwnerClientId);

        ObscureVisionNearby(position);
    }

    // Shrinks the vision radius of anyone caught in the smoke at the moment
    // it's cast — the Kapre itself is skipped, since they can see through
    // their own smoke. Doesn't follow players who walk in afterward; that's
    // a reasonable first pass and can be made trigger-based later if needed.
    private void ObscureVisionNearby(Vector3 position)
    {
        Collider2D[] hits = Physics2D.OverlapCircleAll(position, smokeVisionRadius);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            PlayerVision targetVision = hit.GetComponent<PlayerVision>();
            targetVision?.ServerApplyVisionReduction(smokeVisionMultiplier, smokeDuration);
        }
    }

    // Targeted at the Kapre's own owner only — same pattern as the other
    // three abilities, so the shared Ability-cooldown UI picks it up.
    [ClientRpc]
    private void NotifyCooldownStartedClientRpc(float duration)
    {
        if (!IsOwner) return;
        Debug.Log($"[Client] Tabako Smoke used — {duration}s cooldown.");
        AbilityCooldownUI.Instance?.StartAbilityCooldown(duration);
    }

    // Every client spawns its own local smoke prop, except the Kapre's own
    // client, which can see through its own smoke.
    [ClientRpc]
    private void SpawnSmokeClientRpc(Vector3 position, float duration, ulong kapreClientId)
    {
        if (NetworkManager.Singleton.LocalClientId == kapreClientId) return;
        if (smokeCloudPrefab == null) return;

        GameObject smoke = Instantiate(smokeCloudPrefab, position, Quaternion.identity);
        Destroy(smoke, duration);

        // TODO: once a vision-cone/lighting system exists, also suppress local
        // light radius / raycast sight checks for its duration, rather than
        // relying purely on an opaque visual sprite sitting on top of the scene.
    }
}