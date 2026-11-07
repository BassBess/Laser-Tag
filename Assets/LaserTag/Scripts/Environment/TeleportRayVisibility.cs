using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals;

/// <summary>
/// Shows the teleport arc only while the player is aiming a teleport (thumbstick pushed forward),
/// so it does not clutter the view during the game. The interactor itself stays active, so releasing the
/// thumbstick still teleports.
/// </summary>
[RequireComponent(typeof(XRRayInteractor))]
public class TeleportRayVisibility : MonoBehaviour
{
    XRRayInteractor ray;
    XRInteractorLineVisual lineVisual;

    void Awake()
    {
        ray = GetComponent<XRRayInteractor>();
        lineVisual = GetComponent<XRInteractorLineVisual>();
    }

    void Update()
    {
        if (lineVisual == null) return;
        bool aiming = ray.hasSelection || ray.selectInput.ReadIsPerformed();
        if (lineVisual.enabled != aiming) lineVisual.enabled = aiming;
    }
}
