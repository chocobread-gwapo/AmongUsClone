using UnityEngine;

[RequireComponent(typeof(Collider2D))]
public class Killable : MonoBehaviour
{
    private PlayerRole targetRole;

    private void Awake()
    {
        targetRole = GetComponent<PlayerRole>();
        if (targetRole == null)
        {
            Debug.LogWarning($"{gameObject.name} has Killable but no PlayerRole component.");
        }
    }

    public bool TryKill()
    {
        if (targetRole == null || !targetRole.IsAlive) return false;
        targetRole.Kill();
        return true;
    }
}