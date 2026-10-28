using UnityEngine;

public class PlayerTestSpawner : MonoBehaviour
{
    [Header("Test Settings")]
    [Min(1)] public int numberOfPlayers = 3;
    public float spawnSpacing = 4f;

    [Header("Full-Body Avatar (Optional)")]
    public GameObject avatarPrefab;

    private void Start()
    {
        for (int i = 0; i < numberOfPlayers; i++)
        {
            int id = i + 1;

            GameObject player = new GameObject("TestPlayer_" + id);
            player.transform.position =
                transform.position + Vector3.right * (i * spawnSpacing);

            PlayerIdentity identity =
                player.AddComponent<PlayerIdentity>();

            AvatarPoseDriver driver =
                player.AddComponent<AvatarPoseDriver>();

            Transform head;
            Transform leftHand;
            Transform rightHand;

            if (avatarPrefab != null)
            {
                GameObject model =
                    Instantiate(avatarPrefab, player.transform);

                model.transform.localPosition = Vector3.zero;
                model.transform.localRotation = Quaternion.identity;
                model.transform.localScale = Vector3.one;

                ScoutAvatar avatar =
                    model.GetComponentInChildren<ScoutAvatar>(true);

                if (avatar == null ||
                    avatar.head == null ||
                    avatar.leftHand == null ||
                    avatar.rightHand == null)
                {
                    Debug.LogError(
                        "Avatar prefab needs a ScoutAvatar with head and hand references.",
                        model
                    );

                    Destroy(player);
                    continue;
                }

                // The simulated player uses AvatarPoseDriver,
                // not the real local player's XR tracking.
                foreach (ScoutLocalRigBinding binding in
                         model.GetComponentsInChildren<ScoutLocalRigBinding>(true))
                {
                    binding.enabled = false;
                }

                // Show the complete body to observer cameras.
                avatar.localCamera = null;

                head = avatar.head;
                leftHand = avatar.leftHand;
                rightHand = avatar.rightHand;
            }
            else
            {
                // Original placeholder mode remains available.
                CreatePart(
                    player.transform, "Body",
                    PrimitiveType.Capsule,
                    new Vector3(0, .95f, 0),
                    Vector3.one * .65f,
                    Color.HSVToRGB(i * .23f % 1f, .7f, .9f)
                );

                head = CreatePart(
                    player.transform, "Head",
                    PrimitiveType.Sphere,
                    new Vector3(0, 1.65f, 0),
                    Vector3.one * .24f,
                    Color.yellow
                );

                leftHand = CreatePart(
                    player.transform, "LeftHand",
                    PrimitiveType.Sphere,
                    new Vector3(-.35f, 1.15f, .35f),
                    Vector3.one * .16f,
                    Color.cyan
                );

                rightHand = CreatePart(
                    player.transform, "RightHand",
                    PrimitiveType.Sphere,
                    new Vector3(.35f, 1.15f, .35f),
                    Vector3.one * .16f,
                    Color.magenta
                );
            }

            driver.SetPoseTargets(head, leftHand, rightHand);

            SimulatedPlayerMovement movement =
                player.AddComponent<SimulatedPlayerMovement>();

            SimulatedTrackedPose tracking =
                player.AddComponent<SimulatedTrackedPose>();

            identity.Initialize(id, "Player " + id);
            movement.Initialize(id);
            tracking.Initialize(id);
        }
    }

    private static Transform CreatePart(
        Transform parent,
        string name,
        PrimitiveType type,
        Vector3 position,
        Vector3 scale,
        Color color)
    {
        GameObject obj = GameObject.CreatePrimitive(type);

        obj.name = name;
        obj.transform.SetParent(parent, false);
        obj.transform.localPosition = position;
        obj.transform.localScale = scale;

        Collider collider = obj.GetComponent<Collider>();
        if (collider != null)
            Object.Destroy(collider);

        Renderer renderer = obj.GetComponent<Renderer>();
        renderer.material.color = color;

        return obj.transform;
    }
}
