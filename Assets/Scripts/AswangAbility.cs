using System.Collections;
using Unity.Collections;
using Unity.Netcode;
using UnityEngine;

// Aswang — Anyo (Shapeshift)
// Assumes a chosen nearby player's look (sprite color + nametag) for 10s.
// A reversed-footprint trail is left as a TODO hook for a subtle visual tell
// once a footstep/particle system exists.
public class AswangAbility : NetworkBehaviour
{
    [Header("Anyo (Shapeshift)")]
    [SerializeField] private float targetRange = 2f;
    [SerializeField] private float disguiseDuration = 10f;
    [SerializeField] private float cooldownDuration = 30f;

    // Synced to everyone on purpose: onlookers are *meant* to see this player
    // wearing someone else's look. The Aswang's true identity stays private
    // through the existing role/MalignoType privacy system — this is separate.
    private readonly NetworkVariable<FixedString64Bytes> disguiseLabel = new NetworkVariable<FixedString64Bytes>(
        "", NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<Color> disguiseColor = new NetworkVariable<Color>(
        Color.white, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    private readonly NetworkVariable<bool> disguiseActive = new NetworkVariable<bool>(
        false, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private PlayerRole myRole;
    private SpriteRenderer spriteRenderer;
    private WorldLabel worldLabel;
    private Color originalColor;

    // Server-only.
    private float serverCooldownEndTime = -999f;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
        spriteRenderer = GetComponent<SpriteRenderer>();
        worldLabel = GetComponentInChildren<WorldLabel>();
        if (spriteRenderer != null) originalColor = spriteRenderer.color;
    }

    public override void OnNetworkSpawn()
    {
        disguiseActive.OnValueChanged += HandleDisguiseChanged;
        disguiseColor.OnValueChanged += HandleVisualFieldChanged;
        disguiseLabel.OnValueChanged += HandleVisualFieldChanged;

        RefreshVisual();
    }

    public override void OnNetworkDespawn()
    {
        disguiseActive.OnValueChanged -= HandleDisguiseChanged;
        disguiseColor.OnValueChanged -= HandleVisualFieldChanged;
        disguiseLabel.OnValueChanged -= HandleVisualFieldChanged;
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.CurrentMalignoType == MalignoType.Aswang;
    }

    // See TikbalangAbility.ServerResetForNewGame() for why this is needed.
    // Also force-ends an active disguise — a shapeshift mid-round that never
    // reverted (e.g. the match ended before RevertAfter's coroutine finished)
    // would otherwise still show as an active disguise at the start of the
    // next match.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;
        serverCooldownEndTime = -999f;
        disguiseActive.Value = false;
        disguiseLabel.Value = "";
    }

    public void ServerExtendCooldownIfActive(float seconds)
    {
        if (!IsServer) return;
        if (Time.time < serverCooldownEndTime)
        {
            serverCooldownEndTime += seconds;
        }
    }

    public void TryActivate()
    {
        if (!IsOwner || !IsMyAbility()) return;

        Collider2D[] hits = Physics2D.OverlapCircleAll(transform.position, targetRange);
        foreach (var hit in hits)
        {
            if (hit.gameObject == gameObject) continue;

            PlayerRole targetRole = hit.GetComponent<PlayerRole>();
            NetworkObject targetNetObj = hit.GetComponent<NetworkObject>();
            if (targetRole == null || targetNetObj == null || !targetRole.IsAlive) continue;

            // Unlike kill/bewilder/trap, disguising doesn't harm the target —
            // an Aswang is allowed to wear a fellow Maligno's look too
            // (useful for misdirection alongside a teammate).
            ShapeshiftServerRpc(targetNetObj.NetworkObjectId);
            break;
        }
    }

    [ServerRpc]
    private void ShapeshiftServerRpc(ulong targetNetworkObjectId)
    {
        if (!IsMyAbility()) return;
        if (Time.time < serverCooldownEndTime) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj))
            return;

        PlayerRole targetRole = targetObj.GetComponent<PlayerRole>();
        if (targetRole == null || !targetRole.IsAlive) return;

        SpriteRenderer targetSprite = targetObj.GetComponent<SpriteRenderer>();
        string targetName = $"Player {targetObj.OwnerClientId + 1}";

        serverCooldownEndTime = Time.time + cooldownDuration;
        NotifyCooldownStartedClientRpc(cooldownDuration);

        disguiseLabel.Value = targetName;
        disguiseColor.Value = targetSprite != null ? targetSprite.color : Color.white;
        disguiseActive.Value = true;

        StartCoroutine(RevertAfter(disguiseDuration));
    }

    // Targeted at the Aswang's own owner only, same pattern as Tikbalang's —
    // the cooldown UI listens here, nothing secret leaks to other clients.
    [ClientRpc]
    private void NotifyCooldownStartedClientRpc(float duration)
    {
        if (!IsOwner) return;
        Debug.Log($"[Client] Anyo used — {duration}s cooldown.");
        AbilityCooldownUI.Instance?.StartAbilityCooldown(duration);
    }

    private IEnumerator RevertAfter(float duration)
    {
        yield return new WaitForSeconds(duration);
        disguiseActive.Value = false;
        disguiseLabel.Value = "";
    }

    private void HandleDisguiseChanged(bool previous, bool current) => RefreshVisual();
    private void HandleVisualFieldChanged(FixedString64Bytes previous, FixedString64Bytes current) => RefreshVisual();
    private void HandleVisualFieldChanged(Color previous, Color current) => RefreshVisual();

    private void RefreshVisual()
    {
        if (spriteRenderer != null)
        {
            spriteRenderer.color = disguiseActive.Value ? disguiseColor.Value : originalColor;
        }

        if (worldLabel != null)
        {
            worldLabel.SetLabel(disguiseActive.Value ? disguiseLabel.Value.ToString() : $"Player {OwnerClientId + 1}");
        }

        // TODO: spawn a reversed-footprint particle trail while disguiseActive.Value
        // is true, as the subtle visual tell called for in the PRD.
    }
}
