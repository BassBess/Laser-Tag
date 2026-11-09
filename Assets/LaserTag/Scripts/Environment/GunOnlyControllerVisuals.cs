using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

/// <summary>
/// Put on a controller: hides every controller/hand model under it so the player only sees the gun.
/// Re-checks a few times per second because XR controller models are sometimes spawned at runtime.
/// Interactor visuals (teleport arc, reticle) and the gun itself are left alone.
/// </summary>
public class GunOnlyControllerVisuals : MonoBehaviour
{
    [Tooltip("The gun held by this controller. Everything under it stays visible.")]
    public Transform gun;

    [Min(0.1f)] public float recheckInterval = 0.5f;

    float nextCheck;

    void OnEnable() => nextCheck = 0f;

    void LateUpdate()
    {
        if (Time.unscaledTime < nextCheck) return;
        nextCheck = Time.unscaledTime + recheckInterval;

        foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
        {
            if (!r.enabled || !(r is MeshRenderer || r is SkinnedMeshRenderer)) continue;
            if (gun != null && r.transform.IsChildOf(gun)) continue;
            // Teleport reticles and other interactor visuals live under an interactor.
            if (r.GetComponentInParent<XRBaseInteractor>(true) != null) continue;
            r.enabled = false;
        }
    }
}
