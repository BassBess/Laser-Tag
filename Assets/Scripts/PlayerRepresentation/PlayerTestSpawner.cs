using UnityEngine;

public class PlayerTestSpawner : MonoBehaviour
{
    [Header("Test Settings")]
    public int numberOfPlayers = 3;
    public float spawnSpacing = 4f;

    [Header("Optional Avatar")]
    public GameObject avatarPrefab;

    void Start()
    {
        for (int i = 0; i < numberOfPlayers; i++)
        {
            Vector3 position = transform.position +
                new Vector3(i * spawnSpacing, 0f, 0f);

            GameObject player;

            if (avatarPrefab != null)
            {
                player = Instantiate(avatarPrefab, position, Quaternion.identity);
            }
            else
            {
                player = GameObject.CreatePrimitive(PrimitiveType.Capsule);
                player.transform.position = position + Vector3.up;

                // The capsule itself is the placeholder representation.
                Destroy(player.GetComponent<Collider>());

                Renderer renderer = player.GetComponent<Renderer>();
                renderer.material.color = Color.HSVToRGB(
                    (i * 0.23f) % 1f, 0.7f, 0.9f
                );
            }

            TestPlayer testPlayer = player.GetComponent<TestPlayer>();

            if (testPlayer == null)
                testPlayer = player.AddComponent<TestPlayer>();

            testPlayer.Initialize(i + 1, "Player " + (i + 1), player.transform.position);
        }
    }
}
