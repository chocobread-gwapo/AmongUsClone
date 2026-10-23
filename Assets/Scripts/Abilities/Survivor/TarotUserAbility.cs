using Unity.Netcode;
using UnityEngine;

// Tarot User — Survivor Class "Sapalaran" (Fortune)
// Press ABILITY anytime (same pattern as Tikbalang/Aswang/Kapre/Reiki — not
// meeting-restricted, unlike Babaylan). Each use draws a random card AND a
// random orientation (50/50 upright/reversed) — genuinely risky, not
// weighted, per design: you don't know if you'll be helped or hurt until
// the card lands.
//
// Card pool (expand the TarotCard enum + the switch in ApplyCardEffect to
// add more later):
//   The Fool  — Upright: +50% move speed. Reversed: -50% move speed (slow).
//   The Star  — Upright: +50% vision radius. Reversed: -50% vision radius.
//
// Reuses PlayerController.ServerApplySlow / PlayerVision.ServerApplyVisionReduction
// for both directions — a multiplier above 1 boosts, below 1 penalizes; same
// mechanism Duwende/Kapre already use for the "weaken" half of these calls.
public class TarotUserAbility : NetworkBehaviour
{
    private enum TarotCard { Fool, Star }

    [Header("Sapalaran (Fortune)")]
    [SerializeField] private float cooldownDuration = 27f;

    [Header("The Fool — Speed")]
    [SerializeField] private float foolDuration = 5f;
    [SerializeField] private float foolBoostMultiplier = 1.5f;
    [SerializeField] private float foolPenaltyMultiplier = 0.5f;

    [Header("The Star — Vision")]
    [SerializeField] private float starDuration = 8f;
    [SerializeField] private float starBoostMultiplier = 1.5f;
    [SerializeField] private float starPenaltyMultiplier = 0.5f;

    private PlayerRole myRole;

    // Server-only.
    private float serverCooldownEndTime = -999f;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.CurrentSurvivorClass == SurvivorClass.TarotUser;
    }

    // See TikbalangAbility.ServerResetForNewGame() for why this is needed.
    public void ServerResetForNewGame()
    {
        if (!IsServer) return;
        serverCooldownEndTime = -999f;
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
        DrawCardServerRpc();
    }

    [ServerRpc]
    private void DrawCardServerRpc()
    {
        if (!IsMyAbility()) return;
        if (myRole == null || !myRole.IsAlive) return;
        if (Time.time < serverCooldownEndTime) return;

        serverCooldownEndTime = Time.time + cooldownDuration;

        TarotCard card = Random.value < 0.5f ? TarotCard.Fool : TarotCard.Star;
        bool upright = Random.value < 0.5f; // true coin flip, not weighted — the risk is the point

        (string cardName, string effectDescription) = ApplyCardEffect(card, upright);

        NotifyCardDrawnClientRpc(cardName, effectDescription, upright, cooldownDuration, GetTargetOnlyOwnerParams());

        Debug.Log($"[Server] {gameObject.name} drew {cardName} ({(upright ? "Upright" : "Reversed")}).");
    }

    // Server-only — applies the actual gameplay effect for the drawn card,
    // returns a display name + a short human-readable effect summary for the
    // client's popup.
    private (string cardName, string effectDescription) ApplyCardEffect(TarotCard card, bool upright)
    {
        switch (card)
        {
            case TarotCard.Fool:
                {
                    float multiplier = upright ? foolBoostMultiplier : foolPenaltyMultiplier;
                    GetComponent<PlayerController>()?.ServerApplySlow(multiplier, foolDuration);
                    string effect = upright
                        ? $"+{Mathf.RoundToInt((foolBoostMultiplier - 1f) * 100f)}% Speed ({foolDuration:0}s)"
                        : $"-{Mathf.RoundToInt((1f - foolPenaltyMultiplier) * 100f)}% Speed ({foolDuration:0}s)";
                    return ("The Fool", effect);
                }
            case TarotCard.Star:
                {
                    float multiplier = upright ? starBoostMultiplier : starPenaltyMultiplier;
                    GetComponent<PlayerVision>()?.ServerApplyVisionReduction(multiplier, starDuration);
                    string effect = upright
                        ? $"+{Mathf.RoundToInt((starBoostMultiplier - 1f) * 100f)}% Vision ({starDuration:0}s)"
                        : $"-{Mathf.RoundToInt((1f - starPenaltyMultiplier) * 100f)}% Vision ({starDuration:0}s)";
                    return ("The Star", effect);
                }
            default:
                return ("Unknown Card", "");
        }
    }

    private ClientRpcParams GetTargetOnlyOwnerParams()
    {
        return new ClientRpcParams
        {
            Send = new ClientRpcSendParams { TargetClientIds = new[] { OwnerClientId } }
        };
    }

    // Targeted at the Tarot User's own client only — lets their own ability
    // cooldown UI start, and gives them (and only them) the flavor popup for
    // which card/orientation they drew. No one else needs to know.
    [ClientRpc]
    private void NotifyCardDrawnClientRpc(string cardName, string effectDescription, bool upright, float cooldownDuration, ClientRpcParams rpcParams = default)
    {
        if (!IsOwner) return;

        string orientation = upright ? "Upright" : "Reversed";
        Debug.Log($"[Client] Drew {cardName} ({orientation}) — {effectDescription}");
        AbilityCooldownUI.Instance?.StartAbilityCooldown(cooldownDuration);
        TarotCardPopupUI.Instance?.ShowCard(cardName, orientation, effectDescription, upright);
    }
}
