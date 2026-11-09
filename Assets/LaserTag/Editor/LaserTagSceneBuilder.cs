using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Unity.Netcode;
using Unity.Netcode.Transports.UTP;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Locomotion;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation;
using UnityEngine.XR.Interaction.Toolkit.Locomotion.Turning;
using Object = UnityEngine.Object;

/// <summary>
/// One-click setup for Part 2 (Tools > Laser Tag > Part 2).
///
/// "Build Game Scene" copies the Task 1 arena (AvatarFirstPersonTest) to Assets/LaserTag/LaserTagArena.unity
/// and adds everything the game needs:
///   1. Colliders on every visible wall/floor/prop that has none (LOD0 only).
///   2. A teleport area on the floors with sphere-based clearance checks, plus teleport anchors at the
///      bottom and top of every staircase.
///   3. Darker lighting.
///   4. Teleport rays (thumbstick forward) and snap turn on the existing XR rig; guns with lasers instead
///      of controller models.
///   5. Local player (ammo, HUD), network manager with LAN auto-join, game manager, spawn points.
///   6. The networked player prefab (full-body avatar with colliders and guns).
/// Every step skips what already exists, so it is safe to run "Apply To Open Scene" again after editing.
/// </summary>
public static class LaserTagSceneBuilder
{
    const string SourceScene = "Assets/PlayerAvatar/AvatarFirstPersonTest.unity";
    const string GameScene = "Assets/LaserTag/LaserTagArena.unity";
    const string PrefabFolder = "Assets/LaserTag/Prefabs";
    const string MaterialFolder = "Assets/LaserTag/Materials";
    const string PlayerPrefabPath = PrefabFolder + "/NetworkPlayer.prefab";
    const string AnchorMaterialPath = MaterialFolder + "/TeleportAnchorPad.mat";
    const string AvatarPrefabPath = "Assets/PlayerAvatar/PlayerAvatar.prefab";
    const string PistolPrefabPath = "Assets/The Developer Train/Sci Fi Guns/Prefabs/Sci Fi Pistol.prefab";
    const string StarterAssets = "Assets/Samples/XR Interaction Toolkit/3.5.1/Starter Assets";
    const string InputActionsPath = StarterAssets + "/XRI Default Input Actions.inputactions";
    const string TeleportInteractorPrefabPath = StarterAssets + "/Prefabs/Interactors/Teleport Interactor.prefab";

    const string SetupRootName = "Laser Tag";
    const string LightingMarkerName = "Lighting Darkened (delete to darken again)";

    // Tunables for the generated content.
    const float LightScale = 0.45f;
    const float AmbientScale = 0.4f;
    const float SpawnSpacing = 3f;
    const int MaxSpawnPoints = 16;

    // Only the arena's own floor tiles. The big desert "Plane" around the building is NOT a floor, otherwise
    // players could spawn or teleport outside the arena.
    static readonly string[] FloorKeywords = { "floor" };
    static readonly string[] NotFloorKeywords = { "stair", "ornament", "lattice_only" };

    // =====================================================================================
    // Menu items
    // =====================================================================================

    [MenuItem("Tools/Laser Tag/Part 2/Build Game Scene", priority = 0)]
    public static void BuildGameScene()
    {
        if (!CanEditScenes()) return;
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScene) == null)
        {
            EditorUtility.DisplayDialog("Laser Tag", "The Task 1 arena scene is missing:\n" + SourceScene, "OK");
            return;
        }
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene) != null &&
            !EditorUtility.DisplayDialog("Laser Tag",
                "Rebuild " + GameScene + " from the Task 1 arena?\nChanges made to the game scene by hand will be lost.",
                "Rebuild", "Cancel"))
            return;

        EnsureFolder("Assets/LaserTag");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(GameScene) != null) AssetDatabase.DeleteAsset(GameScene);
        if (!AssetDatabase.CopyAsset(SourceScene, GameScene))
        {
            EditorUtility.DisplayDialog("Laser Tag", "Could not copy the arena scene.", "OK");
            return;
        }

        Scene scene = EditorSceneManager.OpenScene(GameScene, OpenSceneMode.Single);
        string report = Apply(scene);
        EditorSceneManager.SaveScene(scene);
        RefreshNetworkObjectIds(scene);
        AddSceneToBuildSettings(GameScene);
        Debug.Log("Laser Tag Part 2 setup:\n" + report);
        EditorUtility.DisplayDialog("Laser Tag", "Game scene ready: " + GameScene + "\n\n" + report, "OK");
    }

    [MenuItem("Tools/Laser Tag/Part 2/Apply To Open Scene", priority = 1)]
    public static void ApplyToOpenScene()
    {
        if (!CanEditScenes()) return;
        Scene scene = SceneManager.GetActiveScene();
        string report = Apply(scene);
        EditorSceneManager.MarkSceneDirty(scene);
        if (!string.IsNullOrEmpty(scene.path))
        {
            EditorSceneManager.SaveScene(scene);
            RefreshNetworkObjectIds(scene);
            AddSceneToBuildSettings(scene.path);
        }
        Debug.Log("Laser Tag Part 2 setup:\n" + report);
        EditorUtility.DisplayDialog("Laser Tag", report, "OK");
    }

    [MenuItem("Tools/Laser Tag/Part 2/Rebuild Network Player Prefab", priority = 20)]
    public static void RebuildPlayerPrefabMenu()
    {
        ScoutLocalRigBinding binding = Object.FindAnyObjectByType<ScoutLocalRigBinding>();
        GameObject prefab = BuildPlayerPrefab(binding);
        Selection.activeObject = prefab;
        EditorGUIUtility.PingObject(prefab);
    }

    [MenuItem("Tools/Laser Tag/Part 2/Regenerate Spawn Points", priority = 21)]
    public static void RegenerateSpawnPointsMenu()
    {
        SpawnManager spawns = Object.FindAnyObjectByType<SpawnManager>();
        SafeTeleportationArea area = Object.FindAnyObjectByType<SafeTeleportationArea>();
        if (spawns == null || area == null)
        {
            EditorUtility.DisplayDialog("Laser Tag", "Run Build Game Scene / Apply To Open Scene first.", "OK");
            return;
        }
        foreach (LaserTagSpawnPoint p in spawns.GetComponentsInChildren<LaserTagSpawnPoint>(true)) Undo.DestroyObjectImmediate(p.gameObject);
        var report = new StringBuilder();
        CreateSpawnPoints(spawns, area, FindRig(), report);
        EditorSceneManager.MarkSceneDirty(spawns.gameObject.scene);
        Debug.Log(report.ToString());
    }

    static bool CanEditScenes()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        {
            EditorUtility.DisplayDialog("Laser Tag", "Stop Play mode first.", "OK");
            return false;
        }
        return EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo();
    }

    // =====================================================================================
    // Main
    // =====================================================================================

    static string Apply(Scene scene)
    {
        var report = new StringBuilder();
        Physics.SyncTransforms();

        XROrigin rig = FindRig();
        if (rig == null) throw new InvalidOperationException("No active XR Origin found in the scene.");
        ScoutLocalRigBinding binding = Object.FindAnyObjectByType<ScoutLocalRigBinding>();
        ScoutAvatar localAvatar = binding != null ? binding.avatar : null;
        Transform leftController = binding != null && binding.leftController ? binding.leftController : FindDeep(rig.transform, "Left Controller");
        Transform rightController = binding != null && binding.rightController ? binding.rightController : FindDeep(rig.transform, "Right Controller");

        var excluded = new List<Transform> { rig.transform };
        if (localAvatar != null) excluded.Add(localAvatar.transform);
        if (binding != null) excluded.Add(binding.transform);

        GameObject setupRoot = FindOrCreateRoot(scene, SetupRootName);
        excluded.Add(setupRoot.transform);

        // 1. Colliders
        AddMissingColliders(scene, excluded, report);
        Physics.SyncTransforms();

        // 2. Teleport area + stairs anchors
        SafeTeleportationArea area = SetupTeleportArea(scene, setupRoot.transform, excluded, report);
        SetupStairAnchors(scene, setupRoot.transform, excluded, report);
        Physics.SyncTransforms();

        // 3. Lighting
        DarkenLighting(scene, setupRoot.transform, excluded, report);

        // 4. Rig: locomotion, teleport rays, guns
        SetupLocomotion(rig, report);
        LaserRay leftLaser = null, rightLaser = null;
        if (leftController) leftLaser = SetupController(leftController, binding ? binding.leftGun : null, true, report);
        if (rightController) rightLaser = SetupController(rightController, binding ? binding.rightGun : null, false, report);

        // 5. Local player, network and game
        SetupLocalPlayer(setupRoot.transform, rig, localAvatar, leftController, rightController,
            new[] { leftLaser, rightLaser }.Where(l => l != null).ToArray(), report);
        GameObject playerPrefab = BuildPlayerPrefab(binding);
        report.AppendLine("• Network player prefab: " + PlayerPrefabPath);
        SetupNetworkManager(setupRoot.transform, playerPrefab, report);
        SpawnManager spawns = SetupGameManager(setupRoot.transform, area, report);
        if (spawns.GetComponentsInChildren<LaserTagSpawnPoint>(true).Length == 0)
            CreateSpawnPoints(spawns, area, rig, report);
        else
            report.AppendLine("• Spawn points: kept the existing ones");

        ConfigurePlayerSettings(report);
        EnsureTeleportInteractionLayer(report);
        return report.ToString();
    }

    // =====================================================================================
    // 1. Colliders
    // =====================================================================================

    static void AddMissingColliders(Scene scene, List<Transform> excluded, StringBuilder report)
    {
        // Renderers that are only lower-detail copies (LOD1+) must not get colliders.
        var lowerLods = new HashSet<Renderer>();
        foreach (LODGroup group in Object.FindObjectsByType<LODGroup>(FindObjectsInactive.Include, FindObjectsSortMode.None))
        {
            LOD[] lods = group.GetLODs();
            for (int i = 1; i < lods.Length; i++)
                foreach (Renderer r in lods[i].renderers)
                    if (r) lowerLods.Add(r);
        }

        int added = 0, skippedTransparent = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (MeshRenderer renderer in root.GetComponentsInChildren<MeshRenderer>(false))
            {
                Transform t = renderer.transform;
                if (IsUnder(t, excluded) || lowerLods.Contains(renderer)) continue;
                if (t.GetComponent<Collider>() != null) continue;
                if (t.GetComponentInParent<SafeTeleportationAnchor>(true) != null) continue;
                MeshFilter filter = t.GetComponent<MeshFilter>();
                if (filter == null || filter.sharedMesh == null) continue;
                Bounds b = renderer.bounds;
                if (b.size.magnitude < 0.08f) continue; // tiny decorations
                if (CoveredByParentCollider(t, b)) continue;
                if (IsTransparentEffect(renderer)) { skippedTransparent++; continue; }

                var mc = Undo.AddComponent<MeshCollider>(t.gameObject);
                mc.sharedMesh = filter.sharedMesh;
                added++;
            }
        }
        report.AppendLine($"• Colliders: added {added} mesh colliders" +
            (skippedTransparent > 0 ? $" (skipped {skippedTransparent} light beams/effects)" : ""));
    }

    static bool CoveredByParentCollider(Transform t, Bounds b)
    {
        for (Transform p = t.parent; p != null; p = p.parent)
        {
            foreach (Collider c in p.GetComponents<Collider>())
            {
                if (!c.enabled || c.isTrigger) continue;
                Bounds cb = c.bounds;
                if (cb.Contains(b.center) && cb.size.x * cb.size.y * cb.size.z >= 0.5f * b.size.x * b.size.y * b.size.z)
                    return true;
            }
        }
        return false;
    }

    static bool IsTransparentEffect(Renderer renderer)
    {
        Material m = renderer.sharedMaterial;
        if (m == null || m.renderQueue < 3000) return false;
        string n = renderer.name.ToLowerInvariant();
        // Glass walls/windows should still block lasers and teleports.
        return !(n.Contains("glass") || n.Contains("window") || n.Contains("panel"));
    }

    // =====================================================================================
    // 2. Teleportation
    // =====================================================================================

    static SafeTeleportationArea SetupTeleportArea(Scene scene, Transform setupRoot, List<Transform> excluded, StringBuilder report)
    {
        SafeTeleportationArea area = Object.FindAnyObjectByType<SafeTeleportationArea>(FindObjectsInactive.Include);
        if (area == null)
        {
            var go = new GameObject("Teleport Area");
            Undo.RegisterCreatedObjectUndo(go, "Teleport Area");
            go.transform.SetParent(setupRoot, false);
            area = go.AddComponent<SafeTeleportationArea>();
        }

        area.colliders.Clear();
        foreach (GameObject root in scene.GetRootGameObjects())
        {
            foreach (Collider c in root.GetComponentsInChildren<Collider>(false))
            {
                if (c.isTrigger || !c.enabled || IsUnder(c.transform, excluded)) continue;
                if (c.GetComponentInParent<SafeTeleportationAnchor>(true) != null) continue;
                if (!LooksLikeFloor(c.transform)) continue;
                if (!area.colliders.Contains(c)) area.colliders.Add(c);
            }
        }

        area.interactionLayers = InteractionLayerMask.GetMask("Teleport");
        area.filterSelectionByHitNormal = true;
        area.upNormalToleranceDegrees = 30f;
        area.matchOrientation = MatchOrientation.WorldSpaceUp;
        area.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
        EditorUtility.SetDirty(area);
        report.AppendLine($"• Teleport area: {area.colliders.Count} floor colliders (wall clearance {area.wallClearance} m, " +
                          $"player clearance {area.playerClearance} m)");
        if (area.colliders.Count == 0)
            report.AppendLine("  ! No floors found. Add floor colliders to Laser Tag/Teleport Area > Colliders by hand.");
        return area;
    }

    static bool LooksLikeFloor(Transform t)
    {
        // Check the object and its prefab root names, e.g. "floor_2_LOD0" under "floor_2 (3)".
        for (Transform p = t; p != null; p = p.parent)
        {
            string n = p.name.ToLowerInvariant();
            if (NotFloorKeywords.Any(n.Contains)) return false;
            if (FloorKeywords.Any(n.Contains)) return true;
            if (PrefabUtility.IsOutermostPrefabInstanceRoot(p.gameObject)) break;
        }
        return false;
    }

    static void SetupStairAnchors(Scene scene, Transform setupRoot, List<Transform> excluded, StringBuilder report)
    {
        Transform anchorsRoot = setupRoot.Find("Teleport Anchors");
        if (anchorsRoot != null && anchorsRoot.GetComponentsInChildren<SafeTeleportationAnchor>(true).Length > 0)
        {
            report.AppendLine("• Stairs anchors: kept the existing ones");
            return;
        }
        if (anchorsRoot == null)
        {
            anchorsRoot = new GameObject("Teleport Anchors").transform;
            Undo.RegisterCreatedObjectUndo(anchorsRoot.gameObject, "Teleport Anchors");
            anchorsRoot.SetParent(setupRoot, false);
        }

        // One entry per staircase (prefab instance root or object whose name contains "stair").
        var stairs = new HashSet<Transform>();
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Renderer r in root.GetComponentsInChildren<Renderer>(false))
            {
                if (IsUnder(r.transform, excluded)) continue;
                for (Transform p = r.transform; p != null; p = p.parent)
                    if (p.name.ToLowerInvariant().Contains("stair")) { stairs.Add(p); break; }
            }
        // Keep only the top-most stairs object of each hierarchy.
        stairs.RemoveWhere(s => stairs.Any(o => o != s && s.IsChildOf(o)));

        Material padMaterial = AnchorMaterial();
        int index = 0;
        foreach (Transform stair in stairs)
        {
            if (!TryMeasureStairs(stair, out Vector3 bottom, out Vector3 top, out Vector3 upDirection))
            {
                report.AppendLine($"  ! Could not measure stairs '{stair.name}', place its anchors by hand.");
                continue;
            }
            index++;
            CreateAnchor(anchorsRoot, $"Stairs {index} Bottom", bottom, upDirection, padMaterial);
            CreateAnchor(anchorsRoot, $"Stairs {index} Top", top, -upDirection, padMaterial);
        }
        report.AppendLine(index > 0
            ? $"• Stairs anchors: {index * 2} anchors on {index} staircase(s) (move them in Laser Tag/Teleport Anchors if needed)"
            : "• Stairs anchors: no stairs found");
    }

    /// <summary>Finds where a staircase starts and ends by probing its collider from above.</summary>
    static bool TryMeasureStairs(Transform stair, out Vector3 bottom, out Vector3 top, out Vector3 upDirection)
    {
        bottom = top = upDirection = Vector3.zero;
        Collider[] stairColliders = stair.GetComponentsInChildren<Collider>(false);
        Renderer[] renderers = stair.GetComponentsInChildren<Renderer>(false);
        if (renderers.Length == 0) return false;
        Bounds b = renderers[0].bounds;
        foreach (Renderer r in renderers) b.Encapsulate(r.bounds);

        // Climb direction = horizontal axis with the biggest height change.
        Vector3 bestAxis = Vector3.zero;
        float bestRise = 0f, lowY = 0f, highY = 0f;
        foreach (Vector3 axis in new[] { Vector3.right, Vector3.forward })
        {
            float half = Vector3.Scale(b.extents, axis).magnitude * 0.85f;
            float a = StairHeightAt(stairColliders, b, b.center - axis * half);
            float c = StairHeightAt(stairColliders, b, b.center + axis * half);
            if (float.IsNegativeInfinity(a) || float.IsNegativeInfinity(c)) continue;
            float rise = Mathf.Abs(c - a);
            if (rise > bestRise)
            {
                bestRise = rise;
                bestAxis = c > a ? axis : -axis;
                lowY = Mathf.Min(a, c);
                highY = Mathf.Max(a, c);
            }
        }
        if (bestRise < 0.3f) return false;
        upDirection = bestAxis;

        float halfLength = Vector3.Scale(b.extents, bestAxis).magnitude;
        Vector3 center = new Vector3(b.center.x, 0f, b.center.z);
        Vector3 lowEdge = center - bestAxis * halfLength;
        Vector3 highEdge = center + bestAxis * halfLength;

        // Bottom: on the floor just in front of the first step.
        Vector3 bottomXZ = lowEdge - bestAxis * 0.9f;
        bottom = DropToFloor(bottomXZ, lowY + 1f, stairColliders, out float floorY)
            ? new Vector3(bottomXZ.x, floorY, bottomXZ.z)
            : new Vector3(bottomXZ.x, b.min.y, bottomXZ.z);

        // Top: on the landing past the last step if there is one, otherwise on the top step.
        Vector3 topXZ = highEdge + bestAxis * 0.9f;
        if (DropToFloor(topXZ, highY + 1.5f, stairColliders, out float landingY) && Mathf.Abs(landingY - highY) < 0.6f)
            top = new Vector3(topXZ.x, landingY, topXZ.z);
        else
        {
            Vector3 onStep = highEdge - bestAxis * 0.35f;
            float stepY = StairHeightAt(stairColliders, b, onStep);
            top = new Vector3(onStep.x, float.IsNegativeInfinity(stepY) ? highY : stepY, onStep.z);
        }
        return true;
    }

    /// <summary>Highest point of the stairs at a horizontal position, or -infinity if there is no step there.</summary>
    static float StairHeightAt(Collider[] stairColliders, Bounds b, Vector3 xz)
    {
        var ray = new Ray(new Vector3(xz.x, b.max.y + 1f, xz.z), Vector3.down);
        float best = float.NegativeInfinity;
        foreach (Collider c in stairColliders)
            if (c.Raycast(ray, out RaycastHit hit, b.size.y + 2f)) best = Mathf.Max(best, hit.point.y);
        return best;
    }

    static bool DropToFloor(Vector3 xz, float fromY, Collider[] ignore, out float y)
    {
        y = 0f;
        RaycastHit[] hits = Physics.RaycastAll(new Vector3(xz.x, fromY, xz.z), Vector3.down, 10f, Physics.AllLayers,
            QueryTriggerInteraction.Ignore);
        float best = float.NegativeInfinity;
        foreach (RaycastHit h in hits)
        {
            if (ignore.Contains(h.collider) || h.normal.y < 0.8f) continue;
            if (h.point.y > best) best = h.point.y;
        }
        if (float.IsNegativeInfinity(best)) return false;
        y = best;
        return true;
    }

    static void CreateAnchor(Transform parent, string name, Vector3 position, Vector3 facing, Material material)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, name);
        go.transform.SetParent(parent, false);
        go.transform.SetPositionAndRotation(position + Vector3.up * 0.01f,
            Quaternion.LookRotation(Vector3.ProjectOnPlane(facing, Vector3.up).normalized, Vector3.up));

        GameObject pad = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        pad.name = "Pad";
        Object.DestroyImmediate(pad.GetComponent<Collider>());
        pad.transform.SetParent(go.transform, false);
        pad.transform.localScale = new Vector3(0.9f, 0.01f, 0.9f);
        pad.GetComponent<MeshRenderer>().sharedMaterial = material;
        pad.GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        var box = pad.AddComponent<BoxCollider>();
        box.size = new Vector3(1f, 2f, 1f);

        var anchor = go.AddComponent<SafeTeleportationAnchor>();
        anchor.teleportAnchorTransform = go.transform;
        anchor.interactionLayers = InteractionLayerMask.GetMask("Teleport");
        anchor.matchOrientation = MatchOrientation.WorldSpaceUp;
        anchor.teleportTrigger = BaseTeleportationInteractable.TeleportTrigger.OnSelectExited;
        anchor.filterSelectionByHitNormal = false;
        EditorUtility.SetDirty(anchor);
    }

    static Material AnchorMaterial()
    {
        var material = AssetDatabase.LoadAssetAtPath<Material>(AnchorMaterialPath);
        if (material != null) return material;
        EnsureFolder(MaterialFolder);
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
        material = new Material(shader) { name = "TeleportAnchorPad" };
        var cyan = new Color(0.1f, 0.85f, 1f, 1f);
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", cyan);
        if (material.HasProperty("_Color")) material.SetColor("_Color", cyan);
        AssetDatabase.CreateAsset(material, AnchorMaterialPath);
        return material;
    }

    // =====================================================================================
    // 3. Lighting
    // =====================================================================================

    static void DarkenLighting(Scene scene, Transform setupRoot, List<Transform> excluded, StringBuilder report)
    {
        if (setupRoot.Find(LightingMarkerName) != null)
        {
            report.AppendLine("• Lighting: already darkened (delete the marker object under 'Laser Tag' to darken again)");
            return;
        }
        int lights = 0;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Light light in root.GetComponentsInChildren<Light>(true))
            {
                if (IsUnder(light.transform, excluded)) continue;
                Undo.RecordObject(light, "Darken light");
                light.intensity *= LightScale;
                PrefabUtility.RecordPrefabInstancePropertyModifications(light);
                lights++;
            }

        RenderSettings.ambientIntensity *= AmbientScale;
        RenderSettings.ambientLight *= AmbientScale;
        RenderSettings.ambientSkyColor *= AmbientScale;
        RenderSettings.ambientEquatorColor *= AmbientScale;
        RenderSettings.ambientGroundColor *= AmbientScale;
        RenderSettings.reflectionIntensity *= 0.6f;

        var marker = new GameObject(LightingMarkerName);
        Undo.RegisterCreatedObjectUndo(marker, "Lighting marker");
        marker.transform.SetParent(setupRoot, false);
        report.AppendLine($"• Lighting: {lights} lights at {LightScale:P0}, ambient at {AmbientScale:P0}");
    }

    // =====================================================================================
    // 4. Rig
    // =====================================================================================

    static void SetupLocomotion(XROrigin rig, StringBuilder report)
    {
        if (Object.FindAnyObjectByType<XRInteractionManager>(FindObjectsInactive.Exclude) == null)
        {
            var manager = new GameObject("XR Interaction Manager");
            Undo.RegisterCreatedObjectUndo(manager, "XR Interaction Manager");
            manager.AddComponent<XRInteractionManager>();
        }

        Transform locomotion = rig.transform.Find("Locomotion");
        if (locomotion == null)
        {
            locomotion = new GameObject("Locomotion").transform;
            Undo.RegisterCreatedObjectUndo(locomotion.gameObject, "Locomotion");
            locomotion.SetParent(rig.transform, false);
        }
        GetOrAdd<LocomotionMediator>(locomotion.gameObject);
        GetOrAdd<XRBodyTransformer>(locomotion.gameObject);

        Transform teleportation = locomotion.Find("Teleportation") ?? CreateChild(locomotion, "Teleportation");
        GetOrAdd<TeleportationProvider>(teleportation.gameObject);

        Transform turn = locomotion.Find("Turn") ?? CreateChild(locomotion, "Turn");
        SnapTurnProvider snap = GetOrAdd<SnapTurnProvider>(turn.gameObject);
        SetValueReader(snap.leftHandTurnInput, FindAction("XRI Left Locomotion", "Snap Turn"));
        SetValueReader(snap.rightHandTurnInput, FindAction("XRI Right Locomotion", "Snap Turn"));
        EditorUtility.SetDirty(snap);

        report.AppendLine("• Locomotion: teleport (push thumbstick forward, aim, release) and snap turn (thumbstick left/right)");
    }

    static LaserRay SetupController(Transform controller, Transform gun, bool left, StringBuilder report)
    {
        string side = left ? "Left" : "Right";
        if (gun == null) gun = FindDeep(controller, side + " Gun");

        // Teleport ray from the XRI Starter Assets.
        XRRayInteractor teleportRay = controller.GetComponentsInChildren<XRRayInteractor>(true)
            .FirstOrDefault(r => r.name.Contains("Teleport"));
        if (teleportRay == null)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(TeleportInteractorPrefabPath);
            if (prefab == null)
            {
                report.AppendLine("  ! Teleport Interactor prefab missing: " + TeleportInteractorPrefabPath);
            }
            else
            {
                var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, controller);
                Undo.RegisterCreatedObjectUndo(instance, "Teleport Interactor");
                instance.name = "Teleport Interactor";
                instance.SetActive(true);
                teleportRay = instance.GetComponent<XRRayInteractor>();
            }
        }
        if (teleportRay != null)
        {
            InputActionReference teleportMode = FindAction($"XRI {side} Locomotion", "Teleport Mode");
            teleportRay.selectInput.inputSourceMode = XRInputButtonReader.InputSourceMode.InputActionReference;
            teleportRay.selectInput.inputActionReferencePerformed = teleportMode;
            teleportRay.selectInput.inputActionReferenceValue = teleportMode;
            teleportRay.interactionLayers = InteractionLayerMask.GetMask("Teleport");
            GetOrAdd<TeleportRayVisibility>(teleportRay.gameObject);
            DisableHoverHaptics(teleportRay.gameObject);
            EditorUtility.SetDirty(teleportRay);
            PrefabUtility.RecordPrefabInstancePropertyModifications(teleportRay);
        }

        // Only the gun is visible, no controller model.
        GunOnlyControllerVisuals visuals = GetOrAdd<GunOnlyControllerVisuals>(controller.gameObject);
        visuals.gun = gun;
        EditorUtility.SetDirty(visuals);

        // Laser on the gun.
        if (gun == null)
        {
            report.AppendLine($"  ! No gun found on the {side.ToLowerInvariant()} controller.");
            return null;
        }
        LaserRay laser = gun.GetComponentInChildren<LaserRay>(true);
        if (laser == null)
        {
            GetOrAdd<LineRenderer>(gun.gameObject);
            laser = Undo.AddComponent<LaserRay>(gun.gameObject);
            laser.muzzle = FindDeep(gun, "Cylinder") ?? gun;
        }
        if (laser.fireAction.reference == null)
        {
            InputActionReference activate = FindAction($"XRI {side} Interaction", "Activate");
            if (activate != null) laser.fireAction = new InputActionProperty(activate);
        }
        EditorUtility.SetDirty(laser);
        report.AppendLine($"• {side} hand: gun with laser (trigger), teleport ray, controller model hidden");
        return laser;
    }

    static void DisableHoverHaptics(GameObject go)
    {
        foreach (MonoBehaviour mb in go.GetComponents<MonoBehaviour>())
        {
            if (mb == null || mb.GetType().Name != "SimpleHapticFeedback") continue;
            var so = new SerializedObject(mb);
            SerializedProperty hover = so.FindProperty("m_PlayHoverEntered");
            if (hover != null) { hover.boolValue = false; so.ApplyModifiedProperties(); }
        }
    }

    // =====================================================================================
    // 5. Local player, network, game
    // =====================================================================================

    static void SetupLocalPlayer(Transform setupRoot, XROrigin rig, ScoutAvatar avatar, Transform left, Transform right,
        LaserRay[] guns, StringBuilder report)
    {
        LocalPlayerRig local = Object.FindAnyObjectByType<LocalPlayerRig>(FindObjectsInactive.Include);
        if (local == null)
        {
            var go = new GameObject("Local Player");
            Undo.RegisterCreatedObjectUndo(go, "Local Player");
            go.transform.SetParent(setupRoot, false);
            local = go.AddComponent<LocalPlayerRig>();
        }
        local.xrOrigin = rig;
        local.playerCamera = rig.Camera;
        local.avatar = avatar;
        local.teleportationProvider = rig.GetComponentInChildren<TeleportationProvider>(true);
        local.leftController = left;
        local.rightController = right;
        local.guns = guns;
        GetOrAdd<PlayerHud>(local.gameObject).targetCamera = rig.Camera;
        EditorUtility.SetDirty(local);
        report.AppendLine($"• Local player: {local.maxAmmo} shots, refill after {local.refillDelay} s at {local.refillPerSecond}/s; HUD");
    }

    static void SetupNetworkManager(Transform setupRoot, GameObject playerPrefab, StringBuilder report)
    {
        NetworkManager manager = Object.FindAnyObjectByType<NetworkManager>(FindObjectsInactive.Include);
        if (manager == null)
        {
            var go = new GameObject("Network Manager");
            Undo.RegisterCreatedObjectUndo(go, "Network Manager");
            go.transform.SetParent(null); // NetworkManager must be a root object
            manager = go.AddComponent<NetworkManager>();
        }
        UnityTransport transport = GetOrAdd<UnityTransport>(manager.gameObject);
        LanSessionBootstrap bootstrap = GetOrAdd<LanSessionBootstrap>(manager.gameObject);
        if (manager.NetworkConfig == null) manager.NetworkConfig = new NetworkConfig();
        manager.NetworkConfig.NetworkTransport = transport;
        manager.NetworkConfig.PlayerPrefab = playerPrefab;
        manager.NetworkConfig.EnableSceneManagement = true;
        manager.NetworkConfig.ConnectionApproval = false;
        EditorUtility.SetDirty(manager);
        EditorUtility.SetDirty(transport);
        report.AppendLine($"• Network: LAN auto-join (game port {bootstrap.gamePort}, discovery port {bootstrap.discoveryPort})");
    }

    static SpawnManager SetupGameManager(Transform setupRoot, SafeTeleportationArea area, StringBuilder report)
    {
        SpawnManager spawns = Object.FindAnyObjectByType<SpawnManager>(FindObjectsInactive.Include);
        if (spawns == null)
        {
            var go = new GameObject("Spawn Points");
            Undo.RegisterCreatedObjectUndo(go, "Spawn Points");
            go.transform.SetParent(setupRoot, false);
            spawns = go.AddComponent<SpawnManager>();
        }
        spawns.fallbackArea = area;
        EditorUtility.SetDirty(spawns);

        LaserTagGameManager game = Object.FindAnyObjectByType<LaserTagGameManager>(FindObjectsInactive.Include);
        if (game == null)
        {
            var go = new GameObject("Game Manager");
            Undo.RegisterCreatedObjectUndo(go, "Game Manager");
            go.transform.SetParent(null); // network objects in the scene are simplest as roots
            go.AddComponent<NetworkObject>();
            game = go.AddComponent<LaserTagGameManager>();
        }
        game.spawnManager = spawns;
        EditorUtility.SetDirty(game);
        report.AppendLine($"• Game: {game.livesPerPlayer} lives each, {game.respawnDelay} s respawn, " +
                          $"{game.minPlayersToStart}+ players to start (Game Manager > Min Players To Start)");
        return spawns;
    }

    static void CreateSpawnPoints(SpawnManager spawns, SafeTeleportationArea area, XROrigin rig, StringBuilder report)
    {
        Physics.SyncTransforms();
        var ignore = new List<Transform>();
        if (rig != null) ignore.Add(rig.transform);
        ScoutLocalRigBinding binding = Object.FindAnyObjectByType<ScoutLocalRigBinding>();
        if (binding != null && binding.avatar != null) ignore.Add(binding.avatar.transform);

        // Sample every floor on a grid and keep spots with room for a player.
        var candidates = new List<Vector3>();
        foreach (Collider floor in area.colliders)
        {
            if (floor == null) continue;
            Bounds b = floor.bounds;
            for (float x = b.min.x + 0.5f; x <= b.max.x - 0.5f; x += 1f)
            for (float z = b.min.z + 0.5f; z <= b.max.z - 0.5f; z += 1f)
            {
                var ray = new Ray(new Vector3(x, b.max.y + 0.5f, z), Vector3.down);
                if (!floor.Raycast(ray, out RaycastHit hit, b.size.y + 1f) || hit.normal.y < 0.9f) continue;
                if (TeleportClearance.IsBodySpaceFree(hit.point, spawns.bodyRadius + 0.1f, spawns.bodyHeight, floor, ignore))
                    candidates.Add(hit.point);
            }
        }

        if (candidates.Count == 0)
        {
            report.AppendLine("  ! No free floor found for spawn points. Add LaserTagSpawnPoint objects under Laser Tag/Spawn Points by hand.");
            return;
        }

        // Spread them out: repeatedly take the candidate farthest from the ones already chosen.
        Vector3 centroid = candidates.Aggregate(Vector3.zero, (s, p) => s + p) / candidates.Count;
        var chosen = new List<Vector3> { candidates.OrderBy(p => (p - centroid).sqrMagnitude).First() };
        while (chosen.Count < MaxSpawnPoints)
        {
            Vector3 best = Vector3.zero;
            float bestDistance = 0f;
            foreach (Vector3 c in candidates)
            {
                float d = chosen.Min(p => Vector3.Distance(p, c));
                if (d > bestDistance) { bestDistance = d; best = c; }
            }
            if (bestDistance < SpawnSpacing) break;
            chosen.Add(best);
        }

        for (int i = 0; i < chosen.Count; i++)
        {
            var go = new GameObject($"Spawn {i + 1:00}");
            Undo.RegisterCreatedObjectUndo(go, "Spawn point");
            go.transform.SetParent(spawns.transform, false);
            Vector3 look = Vector3.ProjectOnPlane(centroid - chosen[i], Vector3.up);
            go.transform.SetPositionAndRotation(chosen[i],
                look.sqrMagnitude > 0.01f ? Quaternion.LookRotation(look, Vector3.up) : Quaternion.identity);
            go.AddComponent<LaserTagSpawnPoint>();
        }
        spawns.spawnPoints = spawns.GetComponentsInChildren<LaserTagSpawnPoint>(true).ToList();
        EditorUtility.SetDirty(spawns);
        report.AppendLine($"• Spawn points: {chosen.Count} generated at least {SpawnSpacing} m apart (move/add them freely)");
    }

    // =====================================================================================
    // 6. Network player prefab
    // =====================================================================================

    /// <summary>Builds the prefab spawned for every player: full-body Scout avatar + colliders + guns.</summary>
    public static GameObject BuildPlayerPrefab(ScoutLocalRigBinding binding)
    {
        EnsureFolder(PrefabFolder);
        var avatarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(AvatarPrefabPath);
        if (avatarPrefab == null) throw new InvalidOperationException("Avatar prefab missing: " + AvatarPrefabPath);
        var pistolPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PistolPrefabPath);

        var root = new GameObject("NetworkPlayer");
        try
        {
            root.AddComponent<NetworkObject>();
            NetworkPlayer player = root.AddComponent<NetworkPlayer>();

            var avatarObject = (GameObject)PrefabUtility.InstantiatePrefab(avatarPrefab);
            avatarObject.transform.SetParent(root.transform, false);
            avatarObject.name = "Avatar";
            ScoutAvatar avatar = avatarObject.GetComponent<ScoutAvatar>();
            avatar.localCamera = null;

            // Hit colliders: body capsule on the root (follows the body), sphere on the helmet (follows the head).
            var body = avatarObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            body.interpolation = RigidbodyInterpolation.None;
            var capsule = avatarObject.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.85f, 0f);
            capsule.height = 1.7f;
            capsule.radius = 0.26f;
            var colliders = new List<Collider> { capsule };
            if (avatar.head != null)
            {
                var headCollider = avatar.head.gameObject.AddComponent<SphereCollider>();
                headCollider.radius = 0.16f;
                colliders.Add(headCollider);
            }
            player.avatar = avatar;
            player.hitColliders = colliders.ToArray();

            // Guns in the avatar's hands, using the same grip the local avatar uses.
            if (pistolPrefab != null)
            {
                AttachGun(pistolPrefab, avatar.leftHand, "Left Gun",
                    binding ? binding.leftGunGripPosition : ScoutLocalRigBinding.DefaultLeftGripPosition,
                    binding ? binding.leftGunGripRotation : ScoutLocalRigBinding.DefaultGripRotation);
                AttachGun(pistolPrefab, avatar.rightHand, "Right Gun",
                    binding ? binding.rightGunGripPosition : ScoutLocalRigBinding.DefaultRightGripPosition,
                    binding ? binding.rightGunGripRotation : ScoutLocalRigBinding.DefaultGripRotation);
            }

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(root, PlayerPrefabPath);
            return saved;
        }
        finally
        {
            Object.DestroyImmediate(root);
        }
    }

    static void AttachGun(GameObject pistolPrefab, Transform hand, string name, Vector3 gripPosition, Vector3 gripRotation)
    {
        if (hand == null) return;
        // ScoutLocalRigBinding places the hand at gun.TransformPoint(grip) with rotation gun * Euler(grip).
        // Invert that to place the gun relative to the hand.
        Quaternion local = Quaternion.Inverse(Quaternion.Euler(gripRotation));
        var gun = (GameObject)PrefabUtility.InstantiatePrefab(pistolPrefab);
        gun.name = name;
        gun.transform.SetParent(hand, false);
        gun.transform.localRotation = local;
        gun.transform.localPosition = -(local * gripPosition);
        foreach (Collider c in gun.GetComponentsInChildren<Collider>(true)) Object.DestroyImmediate(c);
    }

    // =====================================================================================
    // Project settings
    // =====================================================================================

    static void ConfigurePlayerSettings(StringBuilder report)
    {
        PlayerSettings.runInBackground = true;
        PlayerSettings.Android.forceInternetPermission = true;
        report.AppendLine("• Player settings: run in background, Android internet permission");
    }

    static void EnsureTeleportInteractionLayer(StringBuilder report)
    {
        if (InteractionLayerMask.GetMask("Teleport") == 0)
            report.AppendLine("  ! The XRI interaction layer 'Teleport' is missing (Edit > Project Settings > XR Plug-in Management > XR Interaction Toolkit). Add it as layer 31.");
    }

    /// <summary>
    /// In-scene network objects identify themselves by an id derived from the saved scene. New objects only get
    /// a valid id once the scene has been saved, so recompute it and save again.
    /// </summary>
    static void RefreshNetworkObjectIds(Scene scene)
    {
        var onValidate = typeof(NetworkObject).GetMethod("OnValidate",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (onValidate == null) return;
        foreach (GameObject root in scene.GetRootGameObjects())
            foreach (NetworkObject networkObject in root.GetComponentsInChildren<NetworkObject>(true))
            {
                onValidate.Invoke(networkObject, null);
                EditorUtility.SetDirty(networkObject);
            }
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
    }

    static void AddSceneToBuildSettings(string path)
    {
        var scenes = EditorBuildSettings.scenes.ToList();
        scenes.RemoveAll(s => s.path == path);
        scenes.Insert(0, new EditorBuildSettingsScene(path, true));
        EditorBuildSettings.scenes = scenes.ToArray();
    }

    // =====================================================================================
    // Helpers
    // =====================================================================================

    static XROrigin FindRig()
    {
        return Object.FindObjectsByType<XROrigin>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
            .FirstOrDefault(o => o.isActiveAndEnabled && o.Camera != null);
    }

    static InputActionReference FindAction(string map, string action)
    {
        foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(InputActionsPath))
            if (asset is InputActionReference reference && reference.action != null &&
                reference.action.name == action && reference.action.actionMap != null &&
                reference.action.actionMap.name == map)
                return reference;
        Debug.LogWarning($"Laser Tag: input action '{map}/{action}' not found in {InputActionsPath}");
        return null;
    }

    static void SetValueReader(XRInputValueReader<Vector2> reader, InputActionReference reference)
    {
        if (reader == null || reference == null) return;
        reader.inputSourceMode = XRInputValueReader.InputSourceMode.InputActionReference;
        reader.inputActionReference = reference;
    }

    static T GetOrAdd<T>(GameObject go) where T : Component
    {
        T c = go.GetComponent<T>();
        return c != null ? c : Undo.AddComponent<T>(go);
    }

    static Transform CreateChild(Transform parent, string name)
    {
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, name);
        go.transform.SetParent(parent, false);
        return go.transform;
    }

    static GameObject FindOrCreateRoot(Scene scene, string name)
    {
        GameObject existing = scene.GetRootGameObjects().FirstOrDefault(g => g.name == name);
        if (existing != null) return existing;
        var go = new GameObject(name);
        Undo.RegisterCreatedObjectUndo(go, name);
        SceneManager.MoveGameObjectToScene(go, scene);
        return go;
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root == null) return null;
        foreach (Transform t in root.GetComponentsInChildren<Transform>(true))
            if (t.name == name) return t;
        return null;
    }

    static bool IsUnder(Transform t, List<Transform> roots)
    {
        foreach (Transform r in roots)
            if (r != null && t.IsChildOf(r)) return true;
        return false;
    }

    static void EnsureFolder(string path)
    {
        if (AssetDatabase.IsValidFolder(path)) return;
        string parent = Path.GetDirectoryName(path).Replace('\\', '/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
    }
}
