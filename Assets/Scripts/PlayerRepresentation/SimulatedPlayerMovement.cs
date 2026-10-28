using UnityEngine;

[RequireComponent(typeof(AvatarPoseDriver))]
public class SimulatedPlayerMovement : MonoBehaviour
{
    [Header("Simulated Movement")]
    [SerializeField] private float moveRadius = 1.5f;
    [SerializeField] private float moveSpeed = 0.8f;
    [SerializeField] private float rotationSpeed = 35f;

    private AvatarPoseDriver poseDriver;
    private Vector3 startingPosition;
    private Quaternion currentRotation;
    private float timeOffset;

    public void Initialize(int playerId)
    {
        poseDriver = GetComponent<AvatarPoseDriver>();
        startingPosition = transform.position;
        currentRotation = transform.rotation;
        timeOffset = playerId * 1.7f;
    }

    private void Update()
    {
        if (poseDriver == null)
            return;

        float t = Time.time * moveSpeed + timeOffset;

        Vector3 offset = new Vector3(
            Mathf.Sin(t) * moveRadius,
            0f,
            Mathf.Cos(t) * moveRadius
        );

        currentRotation *= Quaternion.Euler(
            0f,
            rotationSpeed * Time.deltaTime,
            0f
        );

        poseDriver.SetBodyPose(startingPosition + offset, currentRotation);
    }
}
