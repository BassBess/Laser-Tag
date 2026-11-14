using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;

namespace LaserTag.Menu.Editor
{
    public static class MenuSceneBuilder
    {
        public const string MainPath="Assets/Scenes/MainMenu.unity",GamePath="Assets/Scenes/RelayVRGame.unity";
        const string LobbyPath="Assets/Networking/MenuLobby.prefab";
        [MenuItem("Tools/Laser Tag/Menu/Create Menu and Integration Scenes")]
        public static void Create()
        {
            if(EditorApplication.isPlaying)throw new InvalidOperationException("Stop Play mode before generating scenes.");
            if(AssetDatabase.LoadAssetAtPath<SceneAsset>(MainPath)||AssetDatabase.LoadAssetAtPath<SceneAsset>(GamePath)||AssetDatabase.LoadAssetAtPath<GameObject>(LobbyPath)||AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/Networking/MenuNetworkPrefabs.asset"))throw new InvalidOperationException("Generated menu assets already exist. Open them; this tool does not overwrite reviewed assets.");
            if(!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())return;
            if(!AssetDatabase.CopyAsset("Assets/Scenes/RelayVR.unity",GamePath))throw new InvalidOperationException("Cannot copy the verified RelayVR scene.");
            var game=EditorSceneManager.OpenScene(GamePath);
            foreach(var manager in UnityEngine.Object.FindObjectsByType<NetworkManager>(FindObjectsInactive.Include,FindObjectsSortMode.None))UnityEngine.Object.DestroyImmediate(manager.gameObject);
            var binding=UnityEngine.Object.FindFirstObjectByType<ScoutLocalRigBinding>();
            if(!binding||!binding.HasValidReferences||!binding.GetComponent<RelayVRAvatarBridge>())throw new InvalidOperationException("Copied XR bridge or avatar tracking references are invalid.");
            bool snap=UnityEngine.Object.FindObjectsByType<SnapTurnProvider>(FindObjectsSortMode.None).Length>0;
            EditorSceneManager.SaveScene(game);
            var lobbyGo=new GameObject("MenuLobby",typeof(NetworkObject),typeof(LobbyNetworkState));
            var lobbyPrefab=PrefabUtility.SaveAsPrefabAsset(lobbyGo,LobbyPath);UnityEngine.Object.DestroyImmediate(lobbyGo);
            var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
            var root=new GameObject("Menu Session");
            var transport=root.AddComponent<UnityTransport>();var net=root.AddComponent<NetworkManager>();
            net.NetworkConfig.NetworkTransport=transport;net.NetworkConfig.EnableSceneManagement=true;net.NetworkConfig.ConnectionApproval=true;net.NetworkConfig.LoadSceneTimeOut=120;
            var player=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Networking/RelayAvatarPlayer.prefab");
            net.NetworkConfig.PlayerPrefab=player;
            var registry=ScriptableObject.CreateInstance<NetworkPrefabsList>();registry.Add(new NetworkPrefab{Prefab=player});registry.Add(new NetworkPrefab{Prefab=lobbyPrefab});
            AssetDatabase.CreateAsset(registry,"Assets/Networking/MenuNetworkPrefabs.asset");
            net.NetworkConfig.Prefabs.NetworkPrefabsLists.Clear();net.NetworkConfig.Prefabs.NetworkPrefabsLists.Add(registry);
            var relay=root.AddComponent<RelayRoomUI>();relay.autoHostInEditor=false;relay.showDebugUI=false;
            var session=root.AddComponent<MenuSessionController>();session.Manager=net;session.Relay=relay;session.LobbyPrefab=lobbyPrefab;
            var settings=root.AddComponent<SettingsController>();settings.SupportsSnapTurn=snap;root.AddComponent<MenuAudio>();
            var ui=root.AddComponent<MainMenuController>();
            var camera=new GameObject("Menu Camera",typeof(Camera),typeof(AudioListener));camera.transform.SetParent(root.transform);ui.MenuCamera=camera.GetComponent<Camera>();ui.MenuCamera.clearFlags=CameraClearFlags.SolidColor;ui.MenuCamera.backgroundColor=new Color(.02f,.04f,.07f);
            EditorSceneManager.SaveScene(scene,MainPath);
            // Intentional build-list change only; original playground/test entries remain available.
            EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene(MainPath,true),new EditorBuildSettingsScene(GamePath,true)}.Concat(EditorBuildSettings.scenes.Where(s=>s.path!=MainPath&&s.path!=GamePath)).ToArray();
            // NGO's importer may auto-register a new NetworkObject prefab in the
            // shared default list. Keep this feature's registration isolated.
            AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
            var shared=AssetDatabase.LoadAssetAtPath<NetworkPrefabsList>("Assets/DefaultNetworkPrefabs.asset");
            if(shared){var added=shared.PrefabList.FirstOrDefault(p=>p.Prefab==lobbyPrefab);if(added!=null){shared.Remove(added);EditorUtility.SetDirty(shared);}}
            AssetDatabase.SaveAssets();Debug.Log("MENU_SCENES_CREATED");
        }
    }
}


