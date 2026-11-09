using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;

/// <summary>
/// A fixed teleport point (used at the top and bottom of the stairs). Pointing the teleport ray at the
/// anchor's pad moves the player exactly onto the anchor. The anchor is blocked while another living player
/// is standing on it.
/// </summary>
[AddComponentMenu("Laser Tag/Safe Teleportation Anchor")]
public class SafeTeleportationAnchor : TeleportationAnchor
{
    [Tooltip("Minimum horizontal distance to any other living player, in metres.")]
    [Min(0f)] public float playerClearance = 1.0f;

    public override bool IsSelectableBy(IXRSelectInteractor interactor)
    {
        return base.IsSelectableBy(interactor) && IsFree();
    }

    protected override bool GenerateTeleportRequest(IXRInteractor interactor, RaycastHit raycastHit,
        ref TeleportRequest teleportRequest)
    {
        return base.GenerateTeleportRequest(interactor, raycastHit, ref teleportRequest) && IsFree();
    }

    public bool IsFree()
    {
        Transform target = teleportAnchorTransform != null ? teleportAnchorTransform : transform;
        return TeleportClearance.IsFarFromPlayers(target.position, playerClearance, NetworkPlayer.Local);
    }
}
