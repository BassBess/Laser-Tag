using UnityEngine;

public class AvatarPoseDriver : MonoBehaviour
{
    [Header("Avatar Tracking Targets")]
    [SerializeField] private Transform head;
    [SerializeField] private Transform leftHand;
    [SerializeField] private Transform rightHand;

    public void SetPoseTargets(
        Transform headTarget,
        Transform leftHandTarget,
        Transform rightHandTarget)
    {
        head = headTarget;
        leftHand = leftHandTarget;
        rightHand = rightHandTarget;
    }

    public void SetBodyPose(Vector3 position, Quaternion rotation)
    {
        transform.SetPositionAndRotation(position, rotation);
    }

    public void SetHeadPose(Vector3 position, Quaternion rotation)
    {
        if (head != null)
            head.SetPositionAndRotation(position, rotation);
    }

    public void SetLeftHandPose(Vector3 position, Quaternion rotation)
    {
        if (leftHand != null)
            leftHand.SetPositionAndRotation(position, rotation);
    }

    public void SetRightHandPose(Vector3 position, Quaternion rotation)
    {
        if (rightHand != null)
            rightHand.SetPositionAndRotation(position, rotation);
    }
}
