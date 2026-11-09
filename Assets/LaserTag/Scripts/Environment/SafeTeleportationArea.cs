using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// A teleport area that refuses destinations without room for the player.
///
/// The destination is rejected (the teleport ray shows its "blocked" look and nothing happens on release) when:
///  - a column of physics spheres from the feet to head height would overlap a wall, prop, door or avatar
///    collider (see <see cref="wallClearance"/>), or
///  - another living player stands closer than <see cref="playerClearance"/>.
///
/// The floors to teleport on are listed in <c>Colliders</c> (the builder fills this in). They can live
/// anywhere in the scene.
/// </summary>
[AddComponentMenu("Laser Tag/Safe Teleportation Area")]
public class SafeTeleportationArea : TeleportationArea
{
    [Header("Clearance (physics spheres)")]
    [Tooltip("Minimum gap between the player's body and any wall, prop or avatar collider, in metres.")]
    [Min(0.05f)] public float wallClearance = 0.4f;

    [Tooltip("Minimum horizontal distance to any other living player, in metres.")]
    [Min(0f)] public float playerClearance = 1.0f;

    [Tooltip("Height of the column of spheres that must be free, in metres.")]
    [Min(0.5f)] public float bodyHeight = 1.8f;

    readonly List<Transform> ignoreRoots = new List<Transform>(4);

    public override bool IsSelectableBy(IXRSelectInteractor interactor)
    {
        bool selectable = base.IsSelectableBy(interactor);
        if (selectable && interactor is XRRayInteractor ray && ray.TryGetCurrent3DRaycastHit(out RaycastHit hit) &&
            hit.collider != null && colliders.Contains(hit.collider))
        {
            selectable = IsDestinationClear(hit.point, hit.collider);
        }
        return selectable;
    }

    protected override bool GenerateTeleportRequest(IXRInteractor interactor, RaycastHit raycastHit,
        ref TeleportRequest teleportRequest)
    {
        if (!base.GenerateTeleportRequest(interactor, raycastHit, ref teleportRequest))
            return false;
        return IsDestinationClear(teleportRequest.destinationPosition, raycastHit.collider);
    }

    /// <summary>True if the local player could stand at <paramref name="feet"/>.</summary>
    public bool IsDestinationClear(Vector3 feet, Collider floor)
    {
        TeleportClearance.LocalPlayerRoots(ignoreRoots);
        return TeleportClearance.IsBodySpaceFree(feet, wallClearance, bodyHeight, floor, ignoreRoots) &&
               TeleportClearance.IsFarFromPlayers(feet, playerClearance, NetworkPlayer.Local);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.5f);
        foreach (Collider c in colliders)
            if (c != null)
                Gizmos.DrawWireCube(c.bounds.center, c.bounds.size);
    }
}
