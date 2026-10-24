using UnityEngine;

public class TestPlayer : MonoBehaviour
{
    [Header("Player Identity")]
    public int playerId;
    public string displayName;

    [Header("Simulated Movement")]
    public float moveRadius = 1.5f;
    public float moveSpeed = 0.8f;
    public float rotationSpeed = 35f;

    private Vector3 startingPosition;
    private float timeOffset;

    public void Initialize(int id, string name, Vector3 position)
    {
        playerId = id;
        displayName = name;

        transform.position = position;
        startingPosition = position;
        timeOffset = id * 1.7f;

        gameObject.name = "TestPlayer_" + id;
    }

    void Update()
    {
        float t = Time.time * moveSpeed + timeOffset;

        Vector3 offset = new Vector3(
            Mathf.Sin(t) * moveRadius,
            0f,
            Mathf.Cos(t) * moveRadius
        );

        transform.position = startingPosition + offset;
        transform.Rotate(Vector3.up, rotationSpeed * Time.deltaTime);
    }
}
