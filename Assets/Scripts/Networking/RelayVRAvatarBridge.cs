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

        // Keep the local scene avatar instantaneously tracked, but never write
        // the network mirror or its hitboxes from unvalidated owner transforms.
        networkPlayer.PublishTrackedPose(binding);
    }
}
