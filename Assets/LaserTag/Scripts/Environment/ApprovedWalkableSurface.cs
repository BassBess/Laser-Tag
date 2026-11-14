using UnityEngine;

/// <summary>Explicit opt-in for reviewed generic ground, decks and stair tread colliders.
/// Normal/slope, support and body checks remain mandatory.</summary>
[DisallowMultipleComponent]
public sealed class ApprovedWalkableSurface : MonoBehaviour
{
    public Collider[] surfaces = new Collider[0];
    public bool stairTreads;
    [Tooltip("Only this stair collider may contact the lower leg volume; its upper body/railings still block.")]
    [Range(.05f,.35f)] public float maximumStepRise = .34f;
    void OnEnable() { TeleportClearance.InvalidateSurfaces(); }
    void OnDisable() { TeleportClearance.InvalidateSurfaces(); }
}
