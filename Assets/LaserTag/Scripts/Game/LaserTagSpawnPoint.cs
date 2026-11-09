using UnityEngine;

/// <summary>
/// A predetermined place where a player can start a round or respawn. Its blue (Z) arrow is the direction
/// the player faces after spawning. The <see cref="SpawnManager"/> skips points that are occupied.
/// </summary>
public class LaserTagSpawnPoint : MonoBehaviour
{
    public float Yaw => transform.eulerAngles.y;

    void OnDrawGizmos()
    {
        Gizmos.color = new Color(0.1f, 1f, 0.4f, 0.8f);
        Gizmos.DrawWireSphere(transform.position + Vector3.up * 0.05f, 0.35f);
        Gizmos.DrawLine(transform.position, transform.position + Vector3.up * 1.8f);
        Gizmos.DrawLine(transform.position + Vector3.up * 1.2f, transform.position + Vector3.up * 1.2f + transform.forward * 0.6f);
    }
}
