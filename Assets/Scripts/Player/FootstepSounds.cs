using UnityEngine;

// Plays a soft footstep while a player is walking. Added at runtime to every player (see PlayerLabel).
// Your own steps play at full volume; other players' steps are quieter and fade out with distance,
// so you can hear someone walking near you but not across the map. Dead players (ghosts) make no sound.
public class FootstepSounds : MonoBehaviour
{
    [Tooltip("Seconds between steps while walking.")]
    [SerializeField] private float stepInterval = 0.3f;

    [SerializeField] private float ownVolume = 0.35f;
    [SerializeField] private float otherVolume = 0.5f;

    private PlayerController controller;
    private PlayerRole role;
    private float nextStep;
    private int last = -1;

    private void Awake()
    {
        controller = GetComponent<PlayerController>();
        role = GetComponent<PlayerRole>();
    }

    private void Update()
    {
        if (controller == null) return;
        if (!controller.NetworkIsMoving || (role != null && !role.IsAlive))
        {
            nextStep = Time.time + stepInterval * 0.5f; // first step lands shortly after starting to walk
            return;
        }
        if (Time.time < nextStep) return;
        nextStep = Time.time + stepInterval;

        int pick;
        do { pick = Random.Range(1, 4); } while (pick == last);
        last = pick;

        if (controller.IsOwner) Sfx.Play2D("step" + pick, ownVolume);
        else Sfx.PlayAt("step" + pick, transform.position, otherVolume);
    }
}
