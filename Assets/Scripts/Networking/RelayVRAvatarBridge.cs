using UnityEngine;

// Runs after ScoutLocalRigBinding (50) and before
// RelayAvatarPlayer (100) publishes the network pose.
[DefaultExecutionOrder(75)]
[RequireComponent(typeof(ScoutLocalRigBinding))]
public sealed class RelayVRAvatarBridge : MonoBehaviour
{
    private ScoutLocalRigBinding binding;
    private RelayAvatarPlayer networkPlayer;
    private ScoutAvatar networkAvatar;

    private void Awake()
    {
        binding = GetComponent<ScoutLocalRigBinding>();
    }

    private void LateUpdate()
    {
        if (!binding || !binding.HasValidReferences)
            return;

        // Find only the player owned by this computer.
        if (!networkPlayer || !networkPlayer.IsSpawned || !networkPlayer.IsOwner)
        {
            networkPlayer = null;
            networkAvatar = null;

            foreach (var player in FindObjectsByType<RelayAvatarPlayer>(
                         FindObjectsSortMode.None))
            {
                if (!player.IsSpawned || !player.IsOwner)
                    continue;

                networkPlayer = player;
                break;
            }

            if (!networkPlayer)
                return;

            networkAvatar = networkPlayer.GetComponent<ScoutAvatar>();

            if (!networkAvatar)
                return;

            // Never use synthetic WASD/QE desktop movement.
            networkPlayer.SetXRTracking(true);

            // The original scene avatar remains visible locally.
            // Hide only its duplicate network representation here.
            foreach (var renderer in
                     networkAvatar.GetComponentsInChildren<Renderer>(true))
            {
                renderer.enabled = false;
            }

            Debug.Log("[RelayVR] Original VR avatar connected to networking.");
        }

        if (!networkAvatar)
            return;

        ScoutAvatar original = binding.avatar;

        // Copy the original working XR-controlled body pose.
        networkPlayer.transform.SetPositionAndRotation(
            original.transform.position,
            original.transform.rotation);

        // Preserve the existing headset and gun-grip poses.
        networkAvatar.head.SetPositionAndRotation(
            original.head.position,
            original.head.rotation);

        networkAvatar.leftHand.SetPositionAndRotation(
            original.leftHand.position,
            original.leftHand.rotation);

        networkAvatar.rightHand.SetPositionAndRotation(
            original.rightHand.position,
            original.rightHand.rotation);
    }
}
