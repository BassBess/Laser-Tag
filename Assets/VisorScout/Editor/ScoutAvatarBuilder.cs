using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

// Procedural fitted-suit study, not a replacement for an authored/skinned character.
// +Z is forward. Dimensions are metres except the normalized tracked-arm meshes.
public static class ScoutAvatarBuilder
{
    const int Sectors = 40;
    const int Steps = 4;
    static string folder;
    static Material titanium, gunmetal, fabric, glass, violet, rubber, abdominal;
    static Shader lit;
    public const float ReferenceHeight = 1.78f;
    public enum HolsterSide { None, Left, Right }
    [Serializable]
    public sealed class Options
    {
        public float height = 1.86f;
        [HideInInspector] public bool showSuitColor;
        [HideInInspector] public Color suitColor;
        public bool showAccentLights = true;
        public Color accentColor = new Color(.42f, .10f, .85f);
        public float ledIntensity = 2.2f;
        public HolsterSide holster = HolsterSide.Right;
    }

    // A deliberately asymmetric anatomical section: independent front/back depth,
    // lateral centre and fore/aft centre. Radii are half dimensions.
    struct Section
    {
        public float y, width, front, back, x, z;
        public Section(float height, float radius, float anterior, float posterior, float lateral = 0, float sagittal = 0)
        { y = height; width = radius; front = anterior; back = posterior; x = lateral; z = sagittal; }
    }
    static Section S(float y, float w, float f, float b, float x = 0, float z = 0)
    { return new Section(y, w, f, b, x, z); }

    [MenuItem("Tools/Laser Tag/Create Visor Scout Prefab")]
    public static void Build()
    {
        Build(new Options());
    }

    public static string Build(Options options)
    {
        if (options == null) throw new ArgumentNullException(nameof(options));
        if (float.IsNaN(options.height) || float.IsInfinity(options.height) || options.height < 1.6f || options.height > 2.1f)
            throw new ArgumentOutOfRangeException(nameof(options.height), "Choose a height between 1.60 and 2.10 metres.");
        if (EditorApplication.isPlayingOrWillChangePlaymode)
        { Debug.LogWarning("Stop Play mode before generating the Scout."); return null; }
        // Unity refuses additive scene creation alongside an untitled scene.
        // Do not save or replace a user scene to work around that restriction.
        for (int i = 0; i < SceneManager.sceneCount; i++)
            if (string.IsNullOrEmpty(SceneManager.GetSceneAt(i).path))
            { Debug.LogWarning("Save any untitled scene before generating the Scout studio; your scenes were left untouched."); return null; }
        // Inspect the active pipeline, including QualitySettings overrides.
        RenderPipelineAsset pipeline = GraphicsSettings.currentRenderPipeline;
        if (pipeline != null && pipeline.GetType().Name != "UniversalRenderPipelineAsset")
            throw new InvalidOperationException("Scout generation supports URP and Built-in; active pipeline: " + pipeline.GetType().Name);
        lit = Shader.Find(pipeline == null ? "Standard" : "Universal Render Pipeline/Lit");
        if (!lit) throw new InvalidOperationException("The active render pipeline's Lit shader was not found.");
        folder = AssetDatabase.GenerateUniqueAssetPath("Assets/VisorScout/GeneratedNeutralSuit");
        AssetDatabase.CreateFolder("Assets/VisorScout", System.IO.Path.GetFileName(folder));
        MakeMaterials();

        // Construct in an isolated temporary scene, never in the arena/active scene.
        Scene original = SceneManager.GetActiveScene();
        Scene staging = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        GameObject root = null;
        try
        {
            SceneManager.SetActiveScene(staging);
            root = new GameObject("PlayerAvatar_VisorScout_ColorSuit");
            Transform body = root.transform;
            BuildBody(body);
            Transform head = BuildHead(body);
            Transform left = BuildHand(body, -1), right = BuildHand(body, 1);
            Transform lu = BuildArm(body, "LeftUpperArm", false);
            Transform lf = BuildArm(body, "LeftForearm", true);
            Transform ru = BuildArm(body, "RightUpperArm", false);
            Transform rf = BuildArm(body, "RightForearm", true);
            ScoutAvatar avatar = root.AddComponent<ScoutAvatar>();
            avatar.head = head; avatar.leftHand = left; avatar.rightHand = right;
            avatar.leftUpperArm = lu; avatar.leftForearm = lf;
            avatar.rightUpperArm = ru; avatar.rightForearm = rf;
            avatar.leftWristOffset = new Vector3(-.022f, -.045f, 0);
            avatar.rightWristOffset = new Vector3(.022f, -.045f, 0);
            avatar.shoulderOffset.x = .205f;
            avatar.upperArmWidth = .1085f;
            avatar.forearmWidth = .1035f;
            if (options.holster != HolsterSide.None)
                avatar.waistGunRest = BuildHolster(body, options.holster == HolsterSide.Left ? -1 : 1);
            ResizeBody(avatar, options.height / ReferenceHeight);
            ScoutLegRigSetup.Configure(avatar);
            ConfigureColors(root, options);
            // Preserve the existing pose solver and local-camera head hiding.
            avatar.enabled = false; avatar.enabled = true;
            avatar.SendMessage("LateUpdate");
            string path = folder + "/PlayerAvatar_VisorScout_ColorSuit.prefab";
            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            if (!prefab) throw new InvalidOperationException("Scout prefab could not be saved.");
            BuildStudio(staging, root, options.height);
            string preview = folder + "/ScoutSuit_Studio.unity";
            if (!EditorSceneManager.SaveScene(staging, preview))
                throw new InvalidOperationException("Scout studio scene could not be saved.");
            AssetDatabase.SaveAssets();
            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
            Debug.Log("Created procedural Scout suit: " + path + "\nSeparate studio: " + preview +
                "\nInspect front/side/back, tracked-arm clearance and camera head hiding in Unity. " +
                "This is a procedural design study; the supplied reference requires authored modeling/texturing for comparable realism.");
        }
        finally
        {
            if (original.IsValid() && original.isLoaded) SceneManager.SetActiveScene(original);
            if (staging.IsValid() && staging.isLoaded) EditorSceneManager.CloseScene(staging, true);
        }
        return folder;
    }

    static void ResizeBody(ScoutAvatar avatar, float factor)
    {
        // Bake size into the body meshes and pivot positions. The tracked root
        // stays at scale 1; normalized arm meshes use the solver's measurements.
        Transform[] arms = { avatar.leftUpperArm, avatar.leftForearm, avatar.rightUpperArm, avatar.rightForearm };
        var resized = new HashSet<Mesh>();
        foreach (Transform part in avatar.GetComponentsInChildren<Transform>())
        {
            bool normalizedArm = false;
            foreach (Transform arm in arms) if (part == arm || part.IsChildOf(arm)) { normalizedArm = true; break; }
            if (part == avatar.transform || normalizedArm) continue;
            part.localPosition *= factor;
            MeshFilter filter = part.GetComponent<MeshFilter>();
            if (!filter || !filter.sharedMesh || !resized.Add(filter.sharedMesh)) continue;
            Mesh mesh = filter.sharedMesh;
            Vector3[] vertices = mesh.vertices;
            for (int i = 0; i < vertices.Length; i++) vertices[i] *= factor;
            mesh.vertices = vertices; mesh.RecalculateBounds(); EditorUtility.SetDirty(mesh);
        }
        avatar.shoulderOffset *= factor;
        avatar.armSegmentLength *= factor;
        avatar.upperArmWidth *= factor;
        avatar.forearmWidth *= factor;
        avatar.leftWristOffset *= factor;
        avatar.rightWristOffset *= factor;
        avatar.standingHeadHeight = avatar.head.localPosition.y;
    }

    static void ConfigureColors(GameObject root, Options options)
    {
        var accents = new List<Renderer>();
        foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>())
        {
            if (renderer.sharedMaterial == violet) accents.Add(renderer);
        }
        ScoutSuitAppearance appearance = root.AddComponent<ScoutSuitAppearance>();
        appearance.accentStrips = accents.ToArray();
        appearance.showAccentLights = options.showAccentLights;
        appearance.accentColor = options.accentColor; appearance.ledIntensity = options.ledIntensity;
        appearance.ApplyColors();
    }

    static Transform BuildHolster(Transform root, int side)
    {
        string name = side < 0 ? "LeftWaistHolster" : "RightWaistHolster";
        Transform holster = Empty(root, name, V(side * .203f, 1.0f, -.015f));
        Transform mount = Loft(holster, "HolsterBeltMount", new[] {
            S(-.035f,.027f,.022f,.023f), S(.012f,.034f,.026f,.027f), S(.045f,.026f,.021f,.022f) }, gunmetal);
        mount.localPosition = V(-side * .047f, .005f, -.008f);
        // A thick U-shaped cradle leaves its front and mouth open for the weapon.
        Shell(holster, "HolsterShell", new[] { S(-.17f,.033f,.045f,.034f),
            S(-.13f,.044f,.059f,.044f), S(-.035f,.048f,.063f,.047f),
            S(.028f,.047f,.062f,.046f) }, titanium, 55, 305, .006f, .012f, .008f);
        Loft(holster, "HolsterHeel", new[] { S(-.18f,.027f,.040f,.028f),
            S(-.172f,.039f,.053f,.039f), S(-.16f,.039f,.053f,.039f) }, gunmetal);
        Strip(holster, "HolsterMountBridge", V(-side * .080f,.026f,-.018f), V(0,.012f,-.018f), .014f, gunmetal);
        Transform socket = Empty(holster, "GunRest", V(0,.008f,.004f));
        // Conventional weapon axes: muzzle +Z, grip/up +Y. No gun or gameplay
        // component is created; this is an attachment socket for the existing gun.
        socket.localRotation = Quaternion.LookRotation(Vector3.down, Vector3.forward);
        return socket;
    }

    static void BuildBody(Transform root)
    {
        // Helmet crown 1.78; chin 1.55 -> 7.74 helmet lengths overall.
        // A visibly slim silhouette: fitted ribcage, narrow charcoal waist and
        // compact hips. Retain the established panel layout and helmet design.
        Section[] torso = {
            S(.94f,.121f,.101f,.107f), S(1.02f,.124f,.100f,.109f),
            S(1.10f,.098f,.087f,.093f,0,-.004f), S(1.18f,.116f,.102f,.102f),
            S(1.28f,.163f,.125f,.115f), S(1.37f,.185f,.126f,.112f),
            S(1.425f,.171f,.100f,.098f), S(1.465f,.084f,.074f,.073f) };
        Loft(root, "ContinuousTorsoUndersuit", torso, fabric);
        Section[] chest = { S(1.215f,.142f,.116f,.109f), S(1.265f,.174f,.133f,.122f),
            S(1.34f,.189f,.138f,.121f), S(1.395f,.186f,.118f,.110f),
            S(1.43f,.144f,.096f,.098f) };
        Shell(root, "LeftRibcagePlate", chest, titanium, -88, -2.5f, .006f, .025f, .020f);
        Shell(root, "RightRibcagePlate", chest, titanium, 2.5f, 88, .006f, .025f, .020f);
        Shell(root, "LeftScapularPlate", chest, titanium, -176, -94, .006f, .018f, .012f);
        Shell(root, "RightScapularPlate", chest, titanium, 94, 176, .006f, .018f, .012f);
        // Preserve the segmented construction, but keep its joins shallow and dark.
        for (int i = 0; i < 3; i++)
        {
            float y = 1.105f + i * .045f;
            float w = .103f + i * .006f;
            Shell(root, "AbdominalLame" + i, new[] {
                S(y-.020f,w-.008f,.090f+i*.004f,.096f),
                S(y,w,.094f+i*.004f,.098f),
                S(y+.027f,w+.004f,.098f+i*.004f,.100f) }, abdominal, -70, 70, .004f, .012f, .004f);
        }
        Shell(root, "LumbarGuard", new[] { S(1.07f,.111f,.096f,.103f),
            S(1.14f,.108f,.098f,.105f), S(1.23f,.139f,.115f,.119f) }, abdominal, 120, 240, .004f);
        Loft(root, "PelvicUndersuit", new[] { S(.86f,.083f,.075f,.093f),
            S(.925f,.139f,.107f,.122f), S(.985f,.145f,.108f,.122f),
            S(1.045f,.125f,.100f,.109f) }, fabric);
        Shell(root, "PelvicFrontGuard", new[] { S(.89f,.054f,.095f,.101f),
            S(.94f,.086f,.115f,.118f), S(1.015f,.119f,.110f,.118f) }, titanium, -52, 52, .006f, .025f, .01f);
        Section[] belt = { S(1.018f,.131f,.106f,.115f), S(1.039f,.135f,.109f,.117f),
            S(1.061f,.123f,.102f,.109f) };
        Shell(root, "LeftIliacGuard", belt, titanium, -112, -12, .006f);
        Shell(root, "RightIliacGuard", belt, titanium, 12, 112, .006f);
        Shell(root, "SacralGuard", belt, gunmetal, 116, 244, .005f);
        for(int side=-1;side<=1;side+=2)
        {
            string n=side<0?"Left":"Right";
            Shell(root,n+"IliacFlange",new[] { S(.92f,.143f,.117f,.129f),
                S(.977f,.151f,.120f,.135f),S(1.03f,.137f,.111f,.122f) },
                titanium,side<0?-133:47,side<0?-47:133,.005f,.025f,.01f);
            Shell(root,n+"ClavicleLight",new[] { S(1.367f,.194f,.138f,.125f),
                S(1.371f,.193f,.137f,.124f) },violet,side<0?-51:20,side<0?-20:51,.001f);
            Section[] led = { RaisedSection(chest,1.275f,.008f), RaisedSection(chest,1.315f,.008f), RaisedSection(chest,1.350f,.008f) };
            Shell(root,n+"RibLED",led,violet,side<0?-82.2f:81,side<0?-81:82.2f,.001f);
        }
        Loft(root, "NeckSeal", new[] { S(1.443f,.063f,.061f,.063f),
            S(1.49f,.059f,.058f,.061f), S(1.565f,.056f,.054f,.059f) }, fabric);
        Shell(root, "Collar", new[] { S(1.448f,.093f,.080f,.083f),
            S(1.474f,.077f,.072f,.075f), S(1.496f,.066f,.066f,.069f) }, gunmetal, -180, 180, .005f);
        Strip(root, "SternumViolet", V(0,1.288f,.142f), V(0,1.338f,.146f), .0023f, violet);
        Strip(root, "SpineViolet", V(0,1.30f,-.138f), V(0,1.37f,-.130f), .003f, violet);
        for (int side = -1; side <= 1; side += 2) BuildLeg(root, side);
    }

    static Section RaisedSection(Section[] profile, float height, float offset)
    {
        int i = 0;
        while (i < profile.Length - 2 && profile[i+1].y < height) i++;
        Section s = Sample(profile, i, Mathf.InverseLerp(profile[i].y, profile[i+1].y, height));
        s.width += offset; s.front += offset; s.back += offset;
        return s;
    }

    static void BuildLeg(Transform root, int side)
    {
        string n = side < 0 ? "Left" : "Right";
        Transform leg = Empty(root, n + "Leg", V(side*.090f,0,0));
        // Upper thigh carries volume into the hip; calf peaks high, ankle tapers.
        Section[] thigh = { S(.51f,.061f,.066f,.066f), S(.57f,.074f,.083f,.078f),
            S(.69f,.087f,.105f,.097f), S(.81f,.097f,.111f,.108f),
            S(.91f,.092f,.099f,.105f), S(.955f,.078f,.088f,.095f) };
        SlimLegProfile(thigh);
        Loft(leg, n + "ThighUndersuit", thigh, fabric);
        Section[] thighPlate = { S(.55f,.061f,.077f,.073f), S(.63f,.086f,.104f,.095f),
            S(.77f,.098f,.120f,.116f), S(.875f,.094f,.109f,.112f) };
        SlimLegProfile(thighPlate);
        Shell(leg,n+"QuadricepsArmor",thighPlate,titanium,-67,67,.006f,.055f,.025f);
        Shell(leg,n+"OuterThighArmor",thighPlate,gunmetal,side<0 ? -157 : 77,side<0 ? -77 : 157,.005f);
        Shell(leg,n+"HamstringArmor",thighPlate,gunmetal,151,209,.005f);
        Section[] calf = { S(.12f,.047f,.052f,.054f,0,-.012f),
            S(.20f,.053f,.060f,.064f,0,-.012f), S(.32f,.073f,.074f,.095f,0,-.018f),
            S(.40f,.074f,.078f,.087f,0,-.008f), S(.49f,.060f,.065f,.064f) };
        SlimLegProfile(calf);
        Loft(leg,n+"CalfUndersuit",calf,fabric);
        Section[] shin = { S(.145f,.045f,.060f,.059f),
            S(.25f,.061f,.077f,.080f), S(.365f,.079f,.086f,.099f),
            S(.435f,.068f,.075f,.081f) };
        SlimLegProfile(shin);
        Shell(leg,n+"ShinArmor",shin,titanium,-78,78,.006f,.055f,.025f);
        Shell(leg,n+"CalfGuard",calf,gunmetal,105,255,.005f);
        Section[] knee = { S(.455f,.058f,.059f,.06f),
            S(.495f,.066f,.070f,.067f), S(.535f,.06f,.066f,.066f) };
        SlimLegProfile(knee);
        Loft(leg,n+"KneeFlexSeal",knee,fabric);
        Section[] patella = { S(.46f,.039f,.073f,.06f),
            S(.492f,.061f,.094f,.066f), S(.531f,.054f,.083f,.063f),
            S(.549f,.040f,.074f,.062f) };
        SlimLegProfile(patella);
        Shell(leg,n+"Patella",patella,titanium,-68,68,.006f,.018f,.013f);
        Strip(leg,n+"ShinViolet",V(side*.036f,.31f,.065f),V(side*.0396f,.382f,.064f),.0025f,violet);
        // Asymmetric fore/aft sections form a heel, instep and toe box.
        Section[] boot = { S(.014f,.062f,.179f,.082f), S(.03f,.072f,.188f,.089f),
            S(.065f,.070f,.184f,.087f), S(.098f,.061f,.149f,.078f),
            S(.137f,.049f,.072f,.063f), S(.17f,.046f,.053f,.057f) };
        SlimBootProfile(boot);
        Loft(leg,n+"Boot",boot,rubber);
        Section[] instep = { S(.040f,.068f,.188f,.08f),
            S(.066f,.071f,.187f,.08f), S(.10f,.060f,.151f,.075f),
            S(.131f,.048f,.082f,.061f) };
        SlimBootProfile(instep);
        Shell(leg,n+"ToeAndInstep",instep,titanium,-83,83,.005f);
        Shell(leg,n+"HeelCounter",boot,gunmetal,112,248,.005f);
        Section[] cuff = { S(.135f,.051f,.070f,.066f),
            S(.16f,.052f,.061f,.065f), S(.18f,.048f,.056f,.060f) };
        SlimBootProfile(cuff);
        Shell(leg,n+"AnkleCuff",cuff,gunmetal,-180,180,.004f);
        Section[] sole = { S(.005f,.061f,.179f,.082f),
            S(.012f,.073f,.190f,.091f), S(.028f,.073f,.190f,.091f),
            S(.033f,.069f,.185f,.087f) };
        SlimBootProfile(sole);
        Loft(leg,n+"Outsole",sole,rubber);
    }

    static void SlimLegProfile(Section[] profile)
    {
        // A substantial width reduction, rather than another subtle adjustment.
        // Keep depth and blend the upper thigh/ankle into the fitted hip/boot.
        for (int i = 0; i < profile.Length; i++)
        {
            Section section = profile[i];
            float blend = Mathf.InverseLerp(.16f, .27f, section.y);
            float hip = Mathf.InverseLerp(.875f, .955f, section.y);
            section.width *= Mathf.Lerp(1, Mathf.Lerp(.72f, .82f, hip), blend);
            float depth = Mathf.Lerp(1, Mathf.Lerp(.86f, .90f, hip), blend);
            section.front *= depth;
            section.back *= depth;
            profile[i] = section;
        }
    }

    static void SlimBootProfile(Section[] profile)
    {
        for (int i = 0; i < profile.Length; i++)
        {
            Section section = profile[i];
            section.width *= .89f; section.front *= .95f; section.back *= .95f;
            profile[i] = section;
        }
    }

    static Transform BuildHead(Transform root)
    {
        Transform head = Empty(root,"Head",V(0,1.65f,0));
        Section[] skull = { S(-.10f,.061f,.086f,.075f,0,.006f),
            S(-.075f,.080f,.108f,.094f), S(-.025f,.096f,.118f,.116f),
            S(.040f,.101f,.115f,.128f), S(.086f,.088f,.092f,.116f),
            S(.118f,.052f,.051f,.076f,0,-.008f), S(.13f,.008f,.009f,.014f,0,-.014f) };
        Loft(head,"HelmetSubstructure",skull,gunmetal);
        Shell(head,"CrownAndOccipitalShell",new[] {
            S(.035f,.105f,.120f,.132f), S(.075f,.098f,.109f,.128f),
            S(.106f,.074f,.077f,.102f), S(.126f,.037f,.038f,.056f,0,-.011f),
            S(.13f,.009f,.011f,.015f,0,-.014f) },titanium,-180,180,.004f);
        // Visor is a curved, thickened surface following the skull, including temples.
        Shell(head,"WraparoundVisor",new[] { S(-.046f,.079f,.114f,.105f),
            S(-.021f,.098f,.124f,.119f), S(.018f,.104f,.123f,.131f),
            S(.039f,.103f,.120f,.131f) },glass,-111,111,.003f,.010f,.008f);
        Shell(head,"JawAndChinGuard",new[] { S(-.10f,.059f,.092f,.077f,0,.006f),
            S(-.087f,.074f,.110f,.089f), S(-.062f,.088f,.118f,.103f),
            S(-.047f,.085f,.108f,.103f) },titanium,-103,103,.005f,.025f,-.008f);
        Shell(head,"RearHelmetGuard",new[] { S(-.074f,.079f,.101f,.100f),
            S(-.025f,.102f,.120f,.124f), S(.032f,.105f,.122f,.135f) },titanium,116,244,.004f);
        for(int side=-1;side<=1;side+=2)
        {
            string n=side<0?"Left":"Right";
            Shell(head,n+"TempleInset",new[] { S(-.043f,.099f,.115f,.121f),
                S(-.009f,.109f,.124f,.128f), S(.026f,.109f,.125f,.135f) },gunmetal,
                side<0?-113:78,side<0?-78:113,.003f);
            Strip(head,n+"TempleViolet",V(side*.104f,-.025f,.055f),V(side*.108f,.009f,.052f),.0018f,violet);
            // A dark seam reveals the substructure between crown panels.
            Strip(head,n+"CrownSeam",V(side*.054f,.102f,.076f),V(side*.034f,.127f,-.040f),.001f,gunmetal);
        }
        for(int i=0;i<3;i++)
        {
            float y=-.145f+i*.017f;
            Shell(head,"NeckFlexRib"+i,new[] { S(y,.062f,.061f,.063f),
                S(y+.006f,.064f,.063f,.065f),S(y+.010f,.061f,.060f,.062f) },gunmetal,-180,180,.002f);
        }
        return head;
    }

    static Transform BuildArm(Transform root,string name,bool forearm)
    {
        // ScoutAvatar.Segment scales X/Z by .13 (upper) / .115 (forearm),
        // and Y by half segment length. Runtime +Y is distal, -Y proximal.
        // These normalized radii intentionally produce adult-sized limbs.
        Section[] core = forearm ? new[] { S(-1,.32f,.34f,.32f),S(-.72f,.38f,.40f,.37f),
            S(-.15f,.50f,.51f,.46f),S(.48f,.55f,.54f,.50f),S(.85f,.46f,.46f,.44f),S(1,.40f,.40f,.39f) }
            : new[] { S(-1,.38f,.39f,.38f),S(-.65f,.46f,.48f,.46f),S(0,.53f,.55f,.51f),
                S(.60f,.59f,.60f,.57f),S(.9f,.56f,.55f,.54f),S(1,.43f,.43f,.43f) };
        // Profiles below are authored proximal-up. Reverse the core Y profile
        // and rotate the armor frame so the shoulder lands at runtime -Y.
        var runtimeCore=new Section[core.Length];
        for(int i=0;i<core.Length;i++) { Section s=core[core.Length-1-i]; s.y=-s.y; runtimeCore[i]=s; }
        Transform arm = Loft(root,name,runtimeCore,fabric);
        Transform armorFrame=Empty(arm,name+"ArmorFrame",Vector3.zero);
        armorFrame.localRotation=Quaternion.Euler(0,0,180);
        Section[] armor = forearm ? new[] { S(-.84f,.35f,.40f,.36f), S(-.60f,.47f,.51f,.43f),
            S(.15f,.62f,.64f,.53f),S(.63f,.59f,.59f,.51f), S(.80f,.46f,.48f,.44f) }
            : new[] { S(-.70f,.44f,.49f,.46f),S(-.38f,.55f,.58f,.54f),
                S(.22f,.59f,.62f,.58f), S(.52f,.56f,.58f,.55f) };
        Shell(armorFrame,name+"AnteriorArmor",armor,titanium,-78,78,.035f,.18f,.12f);
        Shell(armorFrame,name+"PosteriorArmor",armor,gunmetal,110,250,.03f);
        if(forearm)
        {
            Strip(armorFrame,name+"Violet",V(0,-.35f,.586f),V(0,.37f,.663f),.018f,violet);
            Shell(armorFrame,name+"WristCuff",new[] { S(-.99f,.35f,.38f,.35f),S(-.87f,.39f,.43f,.39f),
                S(-.76f,.39f,.43f,.39f) },gunmetal,-180,180,.025f);
        }
        else
        {
            // Cap follows the upper arm instead of remaining fixed to the torso.
            Shell(armorFrame,name+"DeltoidCap",new[] { S(.45f,.49f,.55f,.53f),
                S(.68f,.57f,.59f,.56f),S(.88f,.55f,.57f,.54f),S(1.04f,.40f,.43f,.41f),S(1.13f,.16f,.20f,.18f) },
                titanium,-115,115,.035f,.20f,.07f);
            Shell(armorFrame,name+"RearDeltoid",new[] { S(.50f,.49f,.54f,.54f),
                S(.74f,.56f,.57f,.57f),S(.94f,.48f,.49f,.49f),S(1.08f,.29f,.32f,.32f) },gunmetal,119,241,.03f);
        }
        return arm;
    }

    static Transform BuildHand(Transform root,int side)
    {
        string n=side<0?"LeftHand":"RightHand";
        Transform hand=Empty(root,n,V(side*.35f,1.15f,.35f));
        // Grip frame starts at X=90, then the binding tilts it to the handle.
        // Local +Y points along the grip's front/back axis. The palm sits beside it;
        // four fingers curl around its front and the thumb opposes them.
        Transform palm=Loft(hand,n+"Palm",new[] {
            S(-.045f,.016f,.023f,.025f,side*.022f),
            S(-.025f,.018f,.041f,.040f,side*.026f),
            S(.004f,.017f,.040f,.042f,side*.027f),
            S(.022f,.012f,.033f,.035f,side*.028f) },fabric);
        Shell(palm,n+"MetacarpalGuard",new[] {
            S(-.027f,.019f,.039f,.038f,side*.026f),
            S(-.003f,.019f,.041f,.041f,side*.027f),
            S(.016f,.014f,.034f,.035f,side*.028f) },titanium,
            side<0?-151:29,side<0?-29:151,.0025f);
        for(int i=0;i<4;i++)
        {
            float row=-.029f+i*.019f;
            float reach=i==0?.010f:0; // index reaches toward the trigger
            Vector3[] path={ V(side*.034f,.004f,row), V(side*.029f,.022f+reach,row),
                V(side*.010f,.032f+reach,row+.001f), V(-side*.013f,.028f+reach,row+.003f),
                V(-side*.022f,.010f+reach,row+.004f) };
            Transform finger=GripDigit(hand,n+"Finger"+i,path,new[]{.0087f,.009f,.008f,.0068f,.0045f},titanium);
            GripDigit(finger,n+"Knuckle"+i,new[]{path[0],Vector3.Lerp(path[0],path[1],.65f),path[1]},
                new[]{.0095f,.010f,.0095f},titanium);
        }
        GripDigit(hand,n+"Thumb",new[]{ V(side*.036f,-.024f,-.012f),
            V(side*.040f,-.009f,-.030f), V(side*.027f,.008f,-.043f),
            V(side*.008f,.018f,-.041f), V(-side*.011f,.015f,-.034f) },new[]{.011f,.0105f,.0095f,.008f,.005f},titanium);
        return hand;
    }

    static Transform GripDigit(Transform parent,string name,Vector3[] path,float[] radius,Material material)
    {
        // A continuous bent glove mesh rather than straight primitive fingers.
        const int sides=12, subdivisions=4;
        int rows=(path.Length-1)*subdivisions+1;
        var centers=new Vector3[rows];var radii=new float[rows];
        for(int row=0;row<rows;row++)
        {
            int segment=Mathf.Min(row/subdivisions,path.Length-2);
            float t=(row-segment*subdivisions)/(float)subdivisions;
            Vector3 a=path[Mathf.Max(0,segment-1)],b=path[segment],c=path[segment+1],d=path[Mathf.Min(path.Length-1,segment+2)];
            centers[row]=.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
            radii[row]=Mathf.Lerp(radius[segment],radius[segment+1],t);
        }
        var vertices=new List<Vector3>();var uv=new List<Vector2>();var triangles=new List<int>();
        for(int row=0;row<rows;row++)
        {
            Vector3 tangent=(centers[Mathf.Min(rows-1,row+1)]-centers[Mathf.Max(0,row-1)]).normalized;
            Vector3 across=Vector3.Cross(tangent,Vector3.forward).normalized;
            Vector3 depth=Vector3.Cross(across,tangent).normalized;
            for(int col=0;col<=sides;col++)
            {
                float angle=col*2*Mathf.PI/sides;
                vertices.Add(centers[row]+radii[row]*(across*Mathf.Sin(angle)+depth*Mathf.Cos(angle)));
                uv.Add(new Vector2(col/(float)sides,row/(float)(rows-1)));
            }
        }
        Grid(triangles,rows,sides+1,0,false);
        for(int end=0;end<2;end++)
        {
            int rim=end==0?0:(rows-1)*(sides+1),center=vertices.Count;
            vertices.Add(centers[end==0?0:rows-1]);uv.Add(new Vector2(.5f,.5f));
            for(int col=0;col<sides;col++)
            {
                vertices.Add(vertices[rim+col]);uv.Add(new Vector2(col/(float)sides,end));
            }
            for(int col=0;col<sides;col++)Tri(triangles,center,center+1+col,center+1+(col+1)%sides,end==0);
        }
        Mesh mesh=Finish(name,vertices,uv,triangles);
        Vector3[] normals=mesh.normals;
        for(int row=0;row<rows;row++)
        {
            int first=row*(sides+1),last=first+sides;
            normals[first]=normals[last]=(normals[first]+normals[last]).normalized;
        }
        mesh.normals=normals;mesh.RecalculateTangents();
        return Store(parent,name,mesh,material);
    }

    // Interpolated lofts use a superellipse section rather than scaled primitives.
    // Smooth shared normals on curved surfaces; disconnected shell bevels retain a
    // deliberate panel boundary. UVs run around the body and along its long axis.
    static Section Sample(Section[] keys,int segment,float t)
    {
        Section a=keys[Mathf.Max(0,segment-1)], b=keys[segment];
        Section c=keys[segment+1], d=keys[Mathf.Min(keys.Length-1,segment+2)];
        return S(Mathf.Lerp(b.y,c.y,t),Curve(a.width,b.width,c.width,d.width,t),
            Curve(a.front,b.front,c.front,d.front,t),Curve(a.back,b.back,c.back,d.back,t),
            Curve(a.x,b.x,c.x,d.x,t),Curve(a.z,b.z,c.z,d.z,t));
    }
    static float Curve(float a,float b,float c,float d,float t)
    {
        float value=.5f*((2*b)+(-a+c)*t+(2*a-5*b+4*c-d)*t*t+(-a+3*b-3*c+d)*t*t*t);
        // Prevent spline overshoot from inflating anatomy or creating inverted radii.
        return Mathf.Clamp(value,Mathf.Min(b,c),Mathf.Max(b,c));
    }
    static Section[] Resample(Section[] keys)
    {
        if(keys.Length<2) throw new ArgumentException("Loft requires two sections.");
        for(int i=0;i<keys.Length;i++)
            if(keys[i].width<=0 || keys[i].front<=0 || keys[i].back<=0 || (i>0 && keys[i].y<=keys[i-1].y))
                throw new ArgumentException("Loft sections need positive radii and increasing heights.");
        var rings=new Section[(keys.Length-1)*Steps+1];
        for(int i=0;i<rings.Length-1;i++) rings[i]=Sample(keys,i/Steps,(i%Steps)/(float)Steps);
        rings[rings.Length-1]=keys[keys.Length-1];
        return rings;
    }
    static Vector3 Point(Section s,float angle,float radialOffset=0)
    {
        float a=angle*Mathf.Deg2Rad;
        float sin=Mathf.Sin(a), cos=Mathf.Cos(a);
        // Exponent .88 gently flattens panel faces without square corners.
        return V(s.x+SignedPow(sin,.88f)*(s.width+radialOffset),s.y,
            s.z+SignedPow(cos,.88f)*((cos>=0?s.front:s.back)+radialOffset));
    }
    static float SignedPow(float value,float power) { return Mathf.Sign(value)*Mathf.Pow(Mathf.Abs(value),power); }

    static Transform Loft(Transform parent,string name,Section[] keys,Material material)
    {
        Section[] rings=Resample(keys);
        var v=new List<Vector3>(); var uv=new List<Vector2>(); var t=new List<int>();
        for(int row=0;row<rings.Length;row++)
            for(int col=0;col<=Sectors;col++)
            {
                v.Add(Point(rings[row],col*360f/Sectors));
                uv.Add(new Vector2(col/(float)Sectors,row/(float)(rings.Length-1)));
            }
        Grid(t,rings.Length,Sectors+1,0,false);
        // Duplicate rim vertices so end caps do not soften longitudinal normals.
        Cap(v,uv,t,rings[0],false);
        Cap(v,uv,t,rings[rings.Length-1],true);
        Mesh mesh=Finish(name,v,uv,t);
        // Weld the UV seam's shading normals, while leaving UV coordinates split.
        Vector3[] normals=mesh.normals;
        for(int row=0;row<rings.Length;row++)
        {
            int a=row*(Sectors+1), b=a+Sectors;
            Vector3 n=(normals[a]+normals[b]).normalized; normals[a]=n; normals[b]=n;
        }
        mesh.normals=normals; mesh.RecalculateTangents();
        return Store(parent,name,mesh,material);
    }
    static void Cap(List<Vector3> v,List<Vector2> uv,List<int> t,Section ring,bool top)
    {
        int center=v.Count; v.Add(V(ring.x,ring.y,ring.z)); uv.Add(new Vector2(.5f,.5f));
        for(int i=0;i<Sectors;i++) { v.Add(Point(ring,i*360f/Sectors)); uv.Add(new Vector2(.5f+.5f*Mathf.Sin(i*2*Mathf.PI/Sectors),.5f+.5f*Mathf.Cos(i*2*Mathf.PI/Sectors))); }
        for(int i=0;i<Sectors;i++)
        {
            int a=center+1+i,b=center+1+(i+1)%Sectors;
            t.Add(center); t.Add(top?a:b); t.Add(top?b:a);
        }
    }

    static Transform Shell(Transform parent,string name,Section[] keys,Material material,float from,float to,float thickness,float lowerRise=0,float upperDrop=0)
    {
        Section[] rings=Resample(keys);
        int columns=Mathf.Max(5,Mathf.CeilToInt((to-from)/360f*Sectors));
        bool closed=Mathf.Abs(to-from-360)<.01f;
        var v=new List<Vector3>(); var uv=new List<Vector2>(); var t=new List<int>();
        int stride=columns+1, count=rings.Length*stride;
        // Outer surface rolls into a bevel along the entire patch perimeter.
        // A second inner surface and edge walls give every panel real thickness.
        for(int layer=0;layer<2;layer++)
            for(int row=0;row<rings.Length;row++)
                for(int col=0;col<=columns;col++)
                {
                    float edge=Mathf.Min(row,rings.Length-1-row);
                    if(!closed) edge=Mathf.Min(edge,Mathf.Min(col,columns-col));
                    float offset=layer==0?thickness*(.25f+.75f*Mathf.Clamp01(edge)):0;
                    float angle=Mathf.Lerp(from,to,col/(float)columns);
                    Vector3 point=Point(rings[row],angle,offset);
                    float along=row/(float)(rings.Length-1);
                    float flank=Mathf.Pow(Mathf.Sin(angle*Mathf.Deg2Rad),2);
                    // Lift the lower flanks and trim the upper flanks: an outlined
                    // armor panel, rather than a horizontal slice of a cylinder.
                    point.y+=flank*(lowerRise*Mathf.Pow(1-along,3)-upperDrop*Mathf.Pow(along,3));
                    v.Add(point);
                    uv.Add(new Vector2(col/(float)columns,row/(float)(rings.Length-1)));
                }
        Grid(t,rings.Length,stride,0,false); Grid(t,rings.Length,stride,count,true);
        for(int col=0;col<columns;col++)
        {
            Edge(t,col,col+1,count);
            int a=(rings.Length-1)*stride+col;
            Edge(t,a+1,a,count);
        }
        if(!closed)
            for(int row=0;row<rings.Length-1;row++)
            {
                Edge(t,(row+1)*stride,row*stride,count);
                Edge(t,row*stride+columns,(row+1)*stride+columns,count);
            }
        Mesh mesh=Finish(name,v,uv,t);
        if(closed)
        {
            Vector3[] normals=mesh.normals;
            for(int layer=0;layer<2;layer++) for(int row=0;row<rings.Length;row++)
            {
                int a=layer*count+row*stride,b=a+columns;
                Vector3 n=(normals[a]+normals[b]).normalized; normals[a]=n; normals[b]=n;
            }
            mesh.normals=normals; mesh.RecalculateTangents();
        }
        return Store(parent,name,mesh,material);
    }
    static void Grid(List<int> t,int rows,int stride,int offset,bool reverse)
    {
        for(int r=0;r<rows-1;r++) for(int c=0;c<stride-1;c++)
        {
            int a=offset+r*stride+c,b=a+stride;
            Tri(t,a,a+1,b+1,reverse); Tri(t,a,b+1,b,reverse);
        }
    }
    static void Edge(List<int> t,int a,int b,int offset)
    { Tri(t,a,a+offset,b+offset,false); Tri(t,a,b+offset,b,false); }
    static void Tri(List<int> t,int a,int b,int c,bool reverse)
    { t.Add(a); t.Add(reverse?c:b); t.Add(reverse?b:c); }
    static Mesh Finish(string name,List<Vector3> v,List<Vector2> uv,List<int> t)
    {
        // Fail at generation time rather than silently saving corrupt geometry.
        for(int i=0;i<v.Count;i++)
            if(float.IsNaN(v[i].sqrMagnitude)||float.IsInfinity(v[i].sqrMagnitude))
                throw new InvalidOperationException(name+": non-finite vertex.");
        for(int i=0;i<t.Count;i+=3)
            if(Vector3.Cross(v[t[i+1]]-v[t[i]],v[t[i+2]]-v[t[i]]).sqrMagnitude<1e-20f)
                throw new InvalidOperationException(name+": degenerate triangle.");
        Mesh mesh=new Mesh { name=name };
        if(v.Count>65535) mesh.indexFormat=IndexFormat.UInt32;
        mesh.SetVertices(v); mesh.SetUVs(0,uv); mesh.SetTriangles(t,0);
        mesh.RecalculateNormals(); mesh.RecalculateTangents(); mesh.RecalculateBounds();
        return mesh;
    }
    static Transform Store(Transform parent,string name,Mesh mesh,Material material)
    {
        AssetDatabase.CreateAsset(mesh,AssetDatabase.GenerateUniqueAssetPath(folder+"/"+name+".asset"));
        Transform part=Empty(parent,name,Vector3.zero);
        part.gameObject.AddComponent<MeshFilter>().sharedMesh=mesh;
        MeshRenderer renderer=part.gameObject.AddComponent<MeshRenderer>();
        renderer.sharedMaterial=material;
        renderer.reflectionProbeUsage=ReflectionProbeUsage.Simple;
        return part;
    }
    static void Strip(Transform parent,string name,Vector3 a,Vector3 b,float radius,Material material)
    {
        float length=Vector3.Distance(a,b);
        Transform strip=Loft(parent,name,new[] { S(0,radius*.60f,radius*.60f,radius*.60f),
            S(length*.08f,radius,radius,radius),S(length*.92f,radius,radius,radius),
            S(length,radius*.60f,radius*.60f,radius*.60f) },material);
        strip.localPosition=a; strip.localRotation=Quaternion.FromToRotation(Vector3.up,b-a);
    }

    static void MakeMaterials()
    {
        titanium=Mat("IvorySilverArmor",new Color(.82f,.84f,.87f),.18f,.34f);
        gunmetal=Mat("DarkGunmetal",new Color(.080f,.090f,.105f),.40f,.28f);
        fabric=Mat("MatteCharcoalUndersuit",new Color(.027f,.032f,.040f),0,.19f);
        abdominal=Mat("AbdominalComposite",new Color(.035f,.040f,.048f),.04f,.23f);
        rubber=Mat("RubberAndGloves",new Color(.017f,.021f,.027f),0,.24f);
        glass=Mat("VioletBlackVisor",new Color(.017f,.012f,.029f),.28f,.94f);
        violet=Mat("SuitLED",new Color(.42f,.10f,.85f),.1f,.4f);
        violet.EnableKeyword("_EMISSION"); violet.SetColor("_EmissionColor",new Color(.42f,.10f,.85f)*2.2f);
        // URP derives the emission keyword from AnyEmissive during import.
        // None would silently remove _EMISSION despite a nonzero HDR color.
        violet.globalIlluminationFlags=MaterialGlobalIlluminationFlags.RealtimeEmissive;
        // Fine directional smoothness variation, not a painted gray metal effect.
        // Metallic remains constant; alpha supplies a restrained brushed response.
        Texture2D brushed=new Texture2D(128,128,TextureFormat.RGBA32,true,true) { name="TitaniumMicroRoughness",wrapMode=TextureWrapMode.Repeat };
        Color[] pixels=new Color[128*128];
        for(int y=0;y<128;y++) for(int x=0;x<128;x++)
        {
            float grain=Mathf.Sin(y*2.31f+x*.13f)*.018f+Mathf.Sin(y*7.11f+x*.037f)*.012f;
            pixels[y*128+x]=new Color(.18f,.18f,.18f,.34f+grain);
        }
        brushed.SetPixels(pixels); brushed.Apply(true,false);
        AssetDatabase.CreateAsset(brushed,folder+"/TitaniumMicroRoughness.asset");
        titanium.SetTexture("_MetallicGlossMap",brushed);
        titanium.SetTextureScale("_MetallicGlossMap",new Vector2(2,3));
        titanium.EnableKeyword(lit.name == "Standard" ? "_METALLICGLOSSMAP" : "_METALLICSPECGLOSSMAP");
        // Both Lit shaders multiply map alpha by the smoothness property.
        if(titanium.HasProperty("_Smoothness")) titanium.SetFloat("_Smoothness",1f);
        if(titanium.HasProperty("_GlossMapScale")) titanium.SetFloat("_GlossMapScale",1f);
    }
    static Material Mat(string name,Color color,float metallic,float smoothness)
    {
        Material m=new Material(lit) { name=name };
        m.color=color;
        if(m.HasProperty("_BaseColor")) m.SetColor("_BaseColor",color);
        m.SetFloat("_Metallic",metallic);
        if(m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness",smoothness);
        if(m.HasProperty("_Glossiness")) m.SetFloat("_Glossiness",smoothness);
        m.enableInstancing=true;
        AssetDatabase.CreateAsset(m,folder+"/"+name+".mat"); return m;
    }

    static void BuildStudio(Scene scene,GameObject avatar,float height)
    {
        // Scene-local settings only. No pipeline asset, quality setting or open
        // scene is edited. Reflections work even with URP additional lights off.
        RenderSettings.ambientMode=AmbientMode.Trilight;
        RenderSettings.ambientSkyColor=new Color(.60f,.63f,.68f);
        RenderSettings.ambientEquatorColor=new Color(.40f,.42f,.46f);
        RenderSettings.ambientGroundColor=new Color(.23f,.24f,.26f);
        RenderSettings.skybox=null;
        RenderSettings.fog=false;
        Cubemap environment=StudioEnvironment();
        RenderSettings.defaultReflectionMode=DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture=environment;
        RenderSettings.reflectionIntensity=1f;
        var studio=new GameObject("Scout Studio - isolated preview");
        Light key=new GameObject("Neutral Key").AddComponent<Light>();
        key.transform.SetParent(studio.transform,false);
        key.type=LightType.Directional; key.color=new Color(1,.97f,.93f); key.intensity=1.25f;
        key.transform.rotation=Quaternion.Euler(35,-35,0);
        key.shadows=LightShadows.Soft; key.shadowBias=.025f;
        RenderSettings.sun=key;
        ReflectionProbe probe=new GameObject("Studio Reflections").AddComponent<ReflectionProbe>();
        probe.transform.SetParent(studio.transform,false); probe.transform.position=V(0,1,0);
        probe.mode=ReflectionProbeMode.Custom; probe.customBakedTexture=environment;
        probe.size=V(12,8,12); probe.intensity=1; probe.boxProjection=false;
        probe.importance=10; probe.blendDistance=0;
        Material floor=Mat("StudioFloor",new Color(.19f,.20f,.22f),.05f,.26f);
        var vertices=new List<Vector3>{V(-20,0,-20),V(-20,0,20),V(20,0,20),V(20,0,-20)};
        var uv=new List<Vector2>{Vector2.zero,Vector2.up,Vector2.one,Vector2.right};
        Store(studio.transform,"StudioGround",Finish("StudioGround",vertices,uv,new List<int>{0,1,2,0,2,3}),floor);
        float size = height / ReferenceHeight;
        StudioCamera(studio.transform,"Front Three Quarter",V(1.1f,1.24f,1.75f)*size,true,height);
        StudioCamera(studio.transform,"Side Inspection",V(2.15f,1.05f,0)*size,false,height);
        StudioCamera(studio.transform,"Back Inspection",V(0,1.1f,-2.15f)*size,false,height);
        // Avatar remains the same GameObject hierarchy and ScoutAvatar pose setup.
        // No pose driver or local camera is assigned in this standalone inspection scene.
        avatar.name="Scout Suit - " + height.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) + "m";
        ScoutAvatar study=avatar.GetComponent<ScoutAvatar>();
        study.leftHand.localPosition=V(-.34f,.79f,.04f)*size;
        study.rightHand.localPosition=V(.34f,.79f,.04f)*size;
        study.leftHand.localRotation=study.rightHand.localRotation=Quaternion.Euler(90,0,0);
        study.SendMessage("LateUpdate");
    }
    static void StudioCamera(Transform parent,string name,Vector3 position,bool active,float height)
    {
        Camera camera=new GameObject(name).AddComponent<Camera>();
        camera.transform.SetParent(parent,false); camera.transform.position=position;
        camera.transform.LookAt(V(0,height*.5056f,0)); camera.fieldOfView=50;
        camera.nearClipPlane=.05f; camera.farClipPlane=60;
        camera.clearFlags=CameraClearFlags.SolidColor; camera.backgroundColor=new Color(.24f,.25f,.27f);
        camera.allowHDR=true; camera.enabled=active;
        if(active) camera.tag="MainCamera";
    }
    static Cubemap StudioEnvironment()
    {
        const int size=128;
        Cubemap cube=new Cubemap(size,TextureFormat.RGBAHalf,true) { name="NeutralStudioReflections" };
        for(int face=0;face<6;face++)
        {
            Color[] pixels=new Color[size*size];
            for(int y=0;y<size;y++) for(int x=0;x<size;x++)
            {
                float u=2*(x+.5f)/size-1,v=2*(y+.5f)/size-1;
                Vector3 d=CubeDirection(face,u,v).normalized;
                Color c=Color.Lerp(new Color(.25f,.27f,.30f),new Color(.65f,.68f,.73f),d.y*.5f+.5f);
                // Broad overhead softbox and two vertical strips create readable
                // specular highlights without a downloaded HDRI or a baked probe.
                float top=SoftBox(d,V(-.35f,.88f,.32f),.88f,24);
                float front=SoftBox(d,V(.65f,.2f,.72f),.94f,35);
                float rim=SoftBox(d,V(-.8f,.12f,-.55f),.96f,45);
                c+=new Color(1,.97f,.93f)*(top*1.2f+front*.8f)+new Color(.86f,.92f,1)*(rim*1.0f);
                pixels[y*size+x]=c;
            }
            cube.SetPixels(pixels,(CubemapFace)face);
        }
        cube.Apply(true,false);
        AssetDatabase.CreateAsset(cube,folder+"/NeutralStudioReflections.asset");
        return cube;
    }
    static float SoftBox(Vector3 d,Vector3 axis,float threshold,float sharpness)
    { return Mathf.SmoothStep(0,1,Mathf.Clamp01((Vector3.Dot(d,axis.normalized)-threshold)*sharpness)); }
    static Vector3 CubeDirection(int face,float u,float v)
    {
        switch(face)
        {
            case 0:return V(1,-v,-u); case 1:return V(-1,-v,u);
            case 2:return V(u,1,v); case 3:return V(u,-1,-v);
            case 4:return V(u,-v,1); default:return V(-u,-v,-1);
        }
    }
    static Vector3 V(float x,float y,float z) { return new Vector3(x,y,z); }
    static Transform Empty(Transform parent,string name,Vector3 position)
    {
        Transform t=new GameObject(name).transform; t.SetParent(parent,false); t.localPosition=position; return t;
    }
}
