using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;

// Separate from PlayerController deliberately — PlayerController disables
// itself for non-owned player instances, but every client needs to see
// EVERY player's label correctly, not just their own. OnNetworkSpawn runs
// for all players on all clients regardless of ownership, so this works
// correctly without any ownership checks.
//
// Also owns the player's cosmetic LOOK (main color, shirt, pants, hat). It lives here rather than in
// a new NetworkBehaviour so the Player prefab needs no changes. The main color is unique per player
// (the server enforces it) and doubles as the name-tag color.
public class PlayerLabel : NetworkBehaviour
{
    private static readonly Dictionary<ulong, PlayerLabel> registry = new Dictionary<ulong, PlayerLabel>();
    // Server only: last look of every client, so a scene change (Lobby <-> Game respawns players) keeps it.
    private static readonly Dictionary<ulong, int> rememberedLooks = new Dictionary<ulong, int>();
    private static NetworkManager hookedManager;

    private readonly NetworkVariable<int> lookPacked = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);
    // >= 0 while an ability (Aswang) makes this player wear someone else's look.
    private readonly NetworkVariable<int> disguiseLook = new NetworkVariable<int>(
        -1, NetworkVariableReadPermission.Everyone, NetworkVariableWritePermission.Server);

    private LookData? preview;
    private bool pendingCommit;
    private float commitDeadline;

    // ---- naming (shared by every UI list) ----

    public static int ColorIndexFor(ulong ownerClientId)
    {
        if (registry.TryGetValue(ownerClientId, out PlayerLabel l) && l != null && l.lookPacked.Value >= 0)
            return LookData.Unpack(l.lookPacked.Value).color;
        return (int)(ownerClientId % (ulong)LookCatalog.PaletteCount);
    }

    // Same name every client shows above a player's head, for UI lists/messages.
    public static string DisplayName(ulong ownerClientId)
    {
        var (colorName, _) = LookCatalog.Palette[ColorIndexFor(ownerClientId)];
        return $"Player {ownerClientId + 1} ({colorName})";
    }

    public static Color ColorFor(ulong ownerClientId)
    {
        return LookCatalog.Palette[ColorIndexFor(ownerClientId)].Color;
    }

    // Two-line rich-text nametag; shared so ability disguises can restore/copy it.
    public static string RichLabel(ulong ownerClientId)
    {
        var (colorName, color) = LookCatalog.Palette[ColorIndexFor(ownerClientId)];
        string hex = ColorUtility.ToHtmlStringRGB(color);
        return $"<b>Player {ownerClientId + 1}</b>\n<color=#{hex}>{colorName}</color>";
    }

    // ---- look ----

    public int NetworkLookPacked => lookPacked.Value;

    public LookData NetworkLook =>
        lookPacked.Value >= 0 ? LookData.Unpack(lookPacked.Value) : LookData.Default(ColorIndexFor(OwnerClientId));

    // What is drawn right now: local preview > disguise > the real look.
    public LookData CurrentLook
    {
        get
        {
            if (preview.HasValue) return preview.Value;
            if (disguiseLook.Value >= 0) return LookData.Unpack(disguiseLook.Value);
            return NetworkLook;
        }
    }

    public void SetPreview(LookData look) { preview = look.Clamped(); pendingCommit = false; }

    // Sends the previewed look to the server; the preview stays until the server's answer arrives.
    public void CommitLook(LookData look)
    {
        look = look.Clamped();
        preview = look;
        pendingCommit = true;
        commitDeadline = Time.unscaledTime + 2f;
        SubmitLookServerRpc(look.Pack());
    }

    public bool IsColorTakenByOther(int colorIndex)
    {
        foreach (var kv in registry)
        {
            PlayerLabel other = kv.Value;
            if (other == null || other == this || other.lookPacked.Value < 0) continue;
            if (LookData.Unpack(other.lookPacked.Value).color == colorIndex) return true;
        }
        return false;
    }

    public void ServerSetDisguiseLook(int packedOrMinusOne)
    {
        if (IsServer) disguiseLook.Value = packedOrMinusOne;
    }

    private int FreeColor(int preferred)
    {
        int n = LookCatalog.PaletteCount;
        for (int i = 0; i < n; i++)
        {
            int c = (preferred + i) % n;
            if (!IsColorTakenByOther(c)) return c;
        }
        return preferred;
    }

    [ServerRpc]
    private void SubmitLookServerRpc(int packed)
    {
        LookData d = LookData.Unpack(packed);
        d.color = FreeColor(d.color);
        lookPacked.Value = d.Pack();
        rememberedLooks[OwnerClientId] = lookPacked.Value;
    }

    private void ServerInitLook()
    {
        if (hookedManager != NetworkManager.Singleton && NetworkManager.Singleton != null)
        {
            hookedManager = NetworkManager.Singleton;
            hookedManager.OnServerStopped += _ => rememberedLooks.Clear();
        }

        LookData d = rememberedLooks.TryGetValue(OwnerClientId, out int remembered)
            ? LookData.Unpack(remembered)
            : LookData.Default((int)(OwnerClientId % (ulong)LookCatalog.PaletteCount));
        d.color = FreeColor(d.color);
        lookPacked.Value = d.Pack();
        rememberedLooks[OwnerClientId] = lookPacked.Value;
    }

    private void HandleLookChanged(int previous, int current)
    {
        if (pendingCommit) { preview = null; pendingCommit = false; }

        WorldLabel label = GetComponent<WorldLabel>();
        if (label != null && disguiseLook.Value < 0) label.SetLabel(RichLabel(OwnerClientId));
    }

    private void Update()
    {
        if (pendingCommit && Time.unscaledTime > commitDeadline) { preview = null; pendingCommit = false; }
    }

    public override void OnNetworkSpawn()
    {
        registry[OwnerClientId] = this;
        lookPacked.OnValueChanged += HandleLookChanged;
        if (IsServer) ServerInitLook();

        WorldLabel label = GetComponent<WorldLabel>();
        if (label != null)
        {
            // OwnerClientId is visible to everyone for a spawned object (not secret),
            // so this produces the same label on every client's screen.
            // Host is typically client 0, so "Player 1" = host, matching your request.
            // Two-line rich-text label: bold name on top, colored "team color"
            // name below — matches the reference HUD's nametag style.
            label.SetLabel(RichLabel(OwnerClientId));
            if (IsOwner && GetComponent<RoleTag>() == null) gameObject.AddComponent<RoleTag>();
            if (!IsOwner && GetComponent<PlayerFogHide>() == null) gameObject.AddComponent<PlayerFogHide>();
            if (GetComponent<FootstepSounds>() == null) gameObject.AddComponent<FootstepSounds>();
        }

        if (GetComponent<PlayerLookRenderer>() == null) gameObject.AddComponent<PlayerLookRenderer>();

        // Wear the look this player saved on this computer.
        if (IsOwner)
        {
            int saved = LookCatalog.LoadSaved();
            if (saved >= 0) SubmitLookServerRpc(saved);
        }
    }

    public override void OnNetworkDespawn()
    {
        lookPacked.OnValueChanged -= HandleLookChanged;
        if (registry.TryGetValue(OwnerClientId, out PlayerLabel l) && l == this) registry.Remove(OwnerClientId);
    }
}
