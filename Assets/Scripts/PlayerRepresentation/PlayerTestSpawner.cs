using UnityEngine;

public class PlayerTestSpawner : MonoBehaviour
{
    [Header("Test Settings")]
    [Min(1)] public int numberOfPlayers = 3;
    public float spawnSpacing = 4f;

    [Header("Optional Avatar")]
    public GameObject avatarPrefab;

    private void Start()
    {
        for (int i = 0; i < numberOfPlayers; i++)
        {
            int id = i + 1;

            Vector3 position = transform.position +
                new Vector3(i * spawnSpacing, 0f, 0f);

            GameObject player = new GameObject("TestPlayer_" + id);
            player.transform.position = position;

            if (avatarPrefab != null)
            {
                Instantiate(avatarPrefab, player.transform);
            }
            else
            {
                CreatePart(
                    player.transform, "VisualPlaceholder",
                    PrimitiveType.Capsule,
                    new Vector3(0f, 0.95f, 0f),
                    new Vector3(0.65f, 0.65f, 0.65f),
                    Color.HSVToRGB((i * 0.23f) % 1f, 0.7f, 0.9f)
                );
            }

            // Temporary tracking markers for testing.
            Transform head = CreatePart(
                player.transform, "Head",
                PrimitiveType.Sphere,
                new Vector3(0f, 1.65f, 0f),
                Vector3.one * 0.24f,
                Color.yellow
            );

            Transform leftHand = CreatePart(
                player.transform, "LeftHand",
                PrimitiveType.Sphere,
                new Vector3(-0.35f, 1.15f, 0.35f),
                Vector3.one * 0.16f,
                Color.cyan
            );

            Transform rightHand = CreatePart(
                player.transform, "RightHand",
                PrimitiveType.Sphere,
                new Vector3(0.35f, 1.15f, 0.35f),
                Vector3.one * 0.16f,
                Color.magenta
            );

            PlayerIdentity identity =
                player.AddComponent<PlayerIdentity>();

            AvatarPoseDriver driver =
                player.AddComponent<AvatarPoseDriver>();

            SimulatedPlayerMovement movement =
                player.AddComponent<SimulatedPlayerMovement>();

            SimulatedTrackedPose tracking =
                player.AddComponent<SimulatedTrackedPose>();

            identity.Initialize(id, "Player " + id);
            driver.SetPoseTargets(head, leftHand, rightHand);

            movement.Initialize(id);
            tracking.Initialize(id);
        }
    }

    private Transform CreatePart(
        Transform parent,
        string partName,
        PrimitiveType primitive,
        Vector3 localPosition,
        Vector3 localScale,
        Color color)
    {
        GameObject part = GameObject.CreatePrimitive(primitive);
        part.name = partName;

        part.transform.SetParent(parent, false);
        part.transform.localPosition = localPosition;
        part.transform.localScale = localScale;

        Collider collider = part.GetComponent<Collider>();
        if (collider != null)
            Destroy(collider);

        Renderer partRenderer = part.GetComponent<Renderer>();
        partRenderer.material.color = color;

        return part.transform;
    }
}
