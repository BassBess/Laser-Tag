using UnityEngine;

[RequireComponent(typeof(AvatarPoseDriver))]
public class SimulatedTrackedPose : MonoBehaviour
{
    private AvatarPoseDriver poseDriver;
    private float timeOffset;

    public void Initialize(int playerId)
    {
        poseDriver = GetComponent<AvatarPoseDriver>();
        timeOffset = playerId * 1.9f;
    }

    private void LateUpdate()
    {
        if (poseDriver == null)
            return;

        float t = Time.time + timeOffset;

        // Local positions relative to the player's body.
        Vector3 headLocal = new Vector3(
            Mathf.Sin(t * 0.7f) * 0.08f,
            1.65f + Mathf.Sin(t * 1.5f) * 0.04f,
            0f
        );

        Vector3 leftHandLocal = new Vector3(
            -0.35f,
            1.15f + Mathf.Sin(t * 2f) * 0.12f,
            0.35f
        );

        Vector3 rightHandLocal = new Vector3(
            0.35f,
            1.15f + Mathf.Cos(t * 2.3f) * 0.12f,
            0.35f
        );

        Quaternion headRotation = transform.rotation *
            Quaternion.Euler(0f, Mathf.Sin(t) * 20f, 0f);

        Quaternion leftRotation = transform.rotation *
            Quaternion.Euler(Mathf.Sin(t * 2f) * 20f, 0f, 0f);

        Quaternion rightRotation = transform.rotation *
            Quaternion.Euler(Mathf.Cos(t * 2f) * 20f, 0f, 0f);

        poseDriver.SetHeadPose(
            transform.TransformPoint(headLocal),
            headRotation
        );

        poseDriver.SetLeftHandPose(
            transform.TransformPoint(leftHandLocal),
            leftRotation
        );

        poseDriver.SetRightHandPose(
            transform.TransformPoint(rightHandLocal),
            rightRotation
        );
    }
}
