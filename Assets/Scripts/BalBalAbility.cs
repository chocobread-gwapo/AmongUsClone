using Unity.Netcode;
using UnityEngine;

public class BalBalAbility : NetworkBehaviour
{
    [Header("Banana Trunk Prop")]
    [SerializeField] private GameObject bananaTrunkPropPrefab;

    private PlayerRole myRole;

    private void Awake()
    {
        myRole = GetComponent<PlayerRole>();
    }

    public bool IsMyAbility()
    {
        return myRole != null && myRole.CurrentMalignoType == MalignoType.BalBal;
    }

    // Renamed from ServerConsumeBodyRpc — Netcode's code generator requires
    // the method name to END with "ServerRpc" exactly, not just contain "Rpc".
    [ServerRpc]
    public void ConsumeBodyServerRpc(ulong targetNetworkObjectId)
    {
        if (!IsMyAbility()) return;

        if (!NetworkManager.Singleton.SpawnManager.SpawnedObjects.TryGetValue(targetNetworkObjectId, out NetworkObject targetObj))
            return;

        PlayerRole targetRole = targetObj.GetComponent<PlayerRole>();
        if (targetRole == null || !targetRole.IsAlive) return;

        Vector3 deathPosition = targetObj.transform.position;

        targetRole.Kill();

        SpawnBananaTrunkClientRpc(deathPosition);
    }

    [ClientRpc]
    private void SpawnBananaTrunkClientRpc(Vector3 position)
    {
        if (bananaTrunkPropPrefab != null)
        {
            Instantiate(bananaTrunkPropPrefab, position, Quaternion.identity);
        }
    }
}