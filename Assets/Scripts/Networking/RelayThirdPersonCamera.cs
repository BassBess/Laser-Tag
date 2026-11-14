using UnityEngine;

// Desktop testing camera. Later a Quest/XR camera replaces this for local VR play.
[RequireComponent(typeof(Camera))]
public sealed class RelayThirdPersonCamera : MonoBehaviour
{
    private Transform target;
    private bool firstFrame = true;
    public void Follow(Transform newTarget)
    {
        target = newTarget;
        firstFrame = true;
    }

    private void LateUpdate()
    {
        if (!target) return;
        Vector3 desired = target.position + target.rotation * new Vector3(0f, 2.7f, -4.3f);
        if (firstFrame)
        {
            transform.position = desired;
            firstFrame = false;
        }
        else
        {
            transform.position = Vector3.Lerp(transform.position, desired, 1f - Mathf.Exp(-8f * Time.deltaTime));
        }
        transform.LookAt(target.position + Vector3.up * 1.25f);
    }
}
