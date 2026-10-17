using UnityEngine;

public enum Role
{
    Crewmate,
    Impostor
}

public class PlayerRole : MonoBehaviour
{
    [SerializeField] private Role currentRole = Role.Crewmate;
    public Role CurrentRole => currentRole;
    public bool IsImpostor => currentRole == Role.Impostor;
    public bool IsAlive { get; private set; } = true;

    public void SetRole(Role role)
    {
        currentRole = role;
    }

    public void Kill()
    {
        if (!IsAlive) return;
        IsAlive = false;
        Debug.Log($"{gameObject.name} was eliminated.");

        // Disable movement/interaction on death
        var controller = GetComponent<PlayerController>();
        if (controller != null) controller.enabled = false;

        var rb = GetComponent<Rigidbody2D>();
        if (rb != null) rb.linearVelocity = Vector2.zero;

        var sr = GetComponent<SpriteRenderer>();
        if (sr != null) sr.color = new Color(sr.color.r, sr.color.g, sr.color.b, 0.3f); // fade to show "dead"
    }
}