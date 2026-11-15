using System.Collections.Generic;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using UnityEngine.UI;
using UnityEngine.XR;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace LaserTag.Menu
{
    // Adds XRI's existing tracked UI interaction to the menu. The gameplay rig,
    // controller input assets, tracked local poses and weapons are never replaced.
    [DefaultExecutionOrder(-180)]
    public sealed class MenuVrPresentation : MonoBehaviour
    {
        MainMenuController controller;
        Canvas canvas;
        Camera menuCamera;
        EventSystem events;
        XROrigin menuOrigin;
        UnityEngine.XR.Interaction.Toolkit.XRInteractionManager uiManager;
        LocalPlayerRig gameRig;
        readonly List<GameObject> gameRays = new List<GameObject>();
        readonly List<Material> materials = new List<Material>();
        readonly List<Material> gameMaterials = new List<Material>();
        readonly List<InputAction> menuActions = new List<InputAction>(), gameActions = new List<InputAction>();
        readonly Dictionary<XRRayInteractor,GameObject> pointerDots = new Dictionary<XRRayInteractor,GameObject>();
        readonly List<XRRayInteractor> expiredPointers = new List<XRRayInteractor>();
        MaterialPropertyBlock pointerColor;
        MenuAnalogNavigation navigation;
        void Awake() { pointerColor = new MaterialPropertyBlock(); }
        InputAction options;
        bool initialized, wasVisible, nativeDisplayWasActive;
        bool placementPending;
        int placementFrames;
        public bool OpenPressedThisFrame => options != null && options.WasPressedThisFrame();

        public void Configure(MainMenuController owner, Canvas view, EventSystem eventSystem, Camera camera)
        {
            controller=owner;canvas=view;events=eventSystem;menuCamera=camera;
            navigation=gameObject.AddComponent<MenuAnalogNavigation>();
            navigation.Configure(view,eventSystem);
            options=new InputAction("Session options",InputActionType.Button,"<XRController>{LeftHand}/menuButton");
            options.Enable();
            RenderPipelineManager.beginCameraRendering+=BeforeCamera;
        }
        bool XRAvailable()
        {
            // A simulator supplies input devices without a native display subsystem.
            // XRSettings is safe before the OpenXR loader has completed startup.
            if (XRSettings.isDeviceActive) return true;
            foreach (var device in InputSystem.devices)
                if (device is UnityEngine.InputSystem.XR.XRController) return true;
            return false;
        }
        void Update()
        {
            if(!controller || !canvas)return;
            if(OpenPressedThisFrame)controller.ToggleOptions();
            if(!initialized)
            {
                if(!XRAvailable())return;
                Initialize();
            }
            bool nativeDisplay=XRSettings.isDeviceActive;
            if(nativeDisplay && !nativeDisplayWasActive){placementPending=true;placementFrames=3;}
            nativeDisplayWasActive=nativeDisplay;
            bool menu=SceneManager.GetActiveScene().name==MenuSessionController.MenuScene;
            if(menuOrigin)menuOrigin.gameObject.SetActive(menu);
            // Unity treats destroyed objects as null; CLR identity also detects
            // the transition from a destroyed rig to no rig on session exit.
            if(!ReferenceEquals(gameRig,LocalPlayerRig.Instance))
            {
                foreach(var ray in gameRays)if(ray){ray.SetActive(false);Destroy(ray);}
                DisposeActions(gameActions);
                gameRays.Clear();gameRig=LocalPlayerRig.Instance;
                foreach(var material in gameMaterials)if(material)Destroy(material);
                gameMaterials.Clear();
                if(gameRig)
                {
                    AddGameRay(gameRig.leftController,"LeftHand");
                    AddGameRay(gameRig.rightController,"RightHand");
                }
            }
            Camera camera=menu?menuCamera:(gameRig?gameRig.playerCamera:null);
            bool visible=canvas.enabled && camera;
            foreach(var ray in gameRays)if(ray)ray.SetActive(!menu && visible);
            if(visible && (!wasVisible || canvas.worldCamera!=camera))
            {
                canvas.worldCamera=camera;
                placementPending=true;placementFrames=3;
            }
            wasVisible=visible;
            UpdateRayInput();
            UpdatePointerDots();
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(!isActiveAndEnabled || !initialized || !placementPending || !canvas || !canvas.enabled || canvas.worldCamera!=camera)return;
            if(XRSettings.isDeviceActive && menuOrigin && camera==menuCamera && menuOrigin.CurrentTrackingOriginMode==TrackingOriginModeFlags.Unknown)return;
            // The camera's tracked pose is applied after Update. Place the panel
            // at rendering time, and wait for actual tracking on the menu origin.
            var driver=camera.GetComponent<TrackedPoseDriver>();
            if(driver && driver.trackingStateInput.action!=null &&
                (driver.trackingStateInput.action.ReadValue<int>()&3)!=3)return;
            PlaceCanvas(camera);
        }
        void LateUpdate()
        {
            // Simulated controllers do not imply an XR display (or a rendered Game
            // view in batch tests). Give their menu a valid camera-relative pose
            // after tracked Update, while native XR keeps render-time placement.
            if(!XRSettings.isDeviceActive && initialized && placementPending && canvas && canvas.enabled && canvas.worldCamera)
                PlaceCanvas(canvas.worldCamera);
        }
        void PlaceCanvas(Camera camera)
        {
            canvas.transform.SetPositionAndRotation(camera.transform.position+camera.transform.forward*1.8f,camera.transform.rotation);
            placementPending=--placementFrames>0;
        }
        void Initialize()
        {
            initialized=true;
            uiManager=gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
            if(events.TryGetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>(out var old))
            {old.enabled=false;Destroy(old);}
            var module=events.gameObject.AddComponent<XRUIInputModule>();
            module.enableBuiltinActionsAsFallback=false;module.enableGamepadInput=false;module.enableJoystickInput=false;
            canvas.renderMode=RenderMode.WorldSpace;
            // Only the tracked raycaster should hit this world-space VR canvas.
            canvas.GetComponent<GraphicRaycaster>().enabled=false;
            canvas.gameObject.AddComponent<TrackedDeviceGraphicRaycaster>();
            var rect=(RectTransform)canvas.transform;
            rect.sizeDelta=new Vector2(1280,900);rect.localScale=Vector3.one*.0012f;
            var root=new GameObject("Menu XR UI Origin");root.SetActive(false);root.transform.SetParent(transform,false);
            var offset=new GameObject("Camera Offset");offset.transform.SetParent(root.transform,false);
            menuCamera.transform.SetParent(offset.transform,false);
            menuOrigin=root.AddComponent<XROrigin>();menuOrigin.Camera=menuCamera;menuOrigin.CameraFloorOffsetObject=offset;
            menuOrigin.RequestedTrackingOriginMode=XROrigin.TrackingOriginMode.Floor;menuOrigin.CameraYOffset=1.65f;
            Pose(menuCamera.gameObject,"<XRHMD>","centerEyePosition","centerEyeRotation");
            foreach(string hand in new[]{"LeftHand","RightHand"})
            {
                var go=new GameObject(hand+" Menu Pointer");go.transform.SetParent(offset.transform,false);
                Pose(go,"<XRController>{"+hand+"}","pointerPosition","pointerRotation");
                AddRay(go,hand);
            }
            root.SetActive(true);wasVisible=false;
        }
        InputAction Action(string name,InputActionType type,string binding,bool game=false)
        {var action=new InputAction(name,type,binding);(game?gameActions:menuActions).Add(action);action.Enable();return action;}
        void Pose(GameObject go,string device,string position,string rotation)
        {
            var driver=go.AddComponent<TrackedPoseDriver>();
            driver.positionInput=new InputActionProperty(PoseAction(device,position,false));
            driver.rotationInput=new InputActionProperty(PoseAction(device,rotation,true));
            driver.trackingStateInput=new InputActionProperty(Action("UI tracking",InputActionType.Value,device+"/trackingState"));
        }
        InputAction PoseAction(string device,string control,bool rotation)
        {
            var action=new InputAction("UI "+control,InputActionType.Value);
            if(control=="pointerPosition" || control=="pointerRotation")
            {
                // Same XRI fallback composite as the Starter Assets: prefer OpenXR
                // aim pose, fall back to the simulated controller's device pose.
                action.AddCompositeBinding(rotation?"QuaternionFallback":"Vector3Fallback")
                    .With("first",device+"/"+control)
                    .With("second",device+(rotation?"/deviceRotation":"/devicePosition"));
            }
            else action.AddBinding(device+"/"+control);
            menuActions.Add(action);action.Enable();return action;
        }
        void AddGameRay(Transform parent,string hand)
        {
            if(!parent)return;
            var go=new GameObject(hand+" Session UI Pointer");go.SetActive(false);go.transform.SetParent(parent,false);
            AddRay(go,hand,true);gameRays.Add(go);
        }
        void AddRay(GameObject go,string hand,bool game=false)
        {
            var ray=go.AddComponent<XRRayInteractor>();ray.enableUIInteraction=true;ray.maxRaycastDistance=6;
            ray.interactionManager=uiManager;
            ray.lineType=XRRayInteractor.LineType.StraightLine;
            ray.rayOriginTransform=go.transform;
            ray.interactionLayers=0;
            // Navigation and the right ray share one physical trigger and one owner.
            ray.uiPressInput.inputSourceMode=XRInputButtonReader.InputSourceMode.ManualValue;
            var line=go.AddComponent<LineRenderer>();line.widthMultiplier=.003f;line.positionCount=2;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
            var material=new Material(Shader.Find("Sprites/Default"));material.color=Color.cyan;(game?gameMaterials:materials).Add(material);line.sharedMaterial=material;
            var visual=go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
            visual.overrideInteractorLineOrigin=false;visual.lineOriginTransform=go.transform;
            visual.overrideInteractorLineLength=false;visual.lineBendRatio=1f;
            visual.snapEndpointIfAvailable=false;visual.smoothMovement=false;
            visual.lineWidth=.004f;visual.setLineColorGradient=true;
            visual.validColorGradient=PointerGradient(Color.cyan);
            visual.invalidColorGradient=PointerGradient(new Color(.15f,.7f,1));
            var dot=GameObject.CreatePrimitive(PrimitiveType.Sphere);dot.name="Menu Aim Cursor";dot.layer=2;
            dot.transform.SetParent(go.transform,false);dot.transform.localScale=Vector3.one*.014f;
            var collider=dot.GetComponent<Collider>();collider.enabled=false;Destroy(collider);
            var renderer=dot.GetComponent<Renderer>();renderer.sharedMaterial=material;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
            dot.SetActive(false);pointerDots.Add(ray,dot);
        }
        readonly Dictionary<XRRayInteractor, bool> rayPressed = new Dictionary<XRRayInteractor, bool>();
        void UpdateRayInput()
        {
            foreach(var pair in pointerDots)
            {
                var ray=pair.Key;
                if(!ray)continue;
                bool right=ray.name.StartsWith("RightHand");
                if(ray.isActiveAndEnabled && canvas.enabled)
                    navigation.ObserveRay(ray);
                bool pressed=ray.isActiveAndEnabled && canvas.enabled &&
                    !navigation.AnalogOwnsInput && (right?navigation.TriggerHeld:navigation.LeftRayHeld);
                bool previous=rayPressed.TryGetValue(ray,out var value) && value;
                ray.uiPressInput.manualPerformed=pressed;
                ray.uiPressInput.manualValue=pressed?1:0;
                if(pressed && !previous)ray.uiPressInput.manualFramePerformed=Time.frameCount;
                if(!pressed && previous)ray.uiPressInput.manualFrameCompleted=Time.frameCount;
                rayPressed[ray]=pressed;
            }
        }
        static Gradient PointerGradient(Color color)
        {
            var gradient=new Gradient();gradient.SetKeys(new[]{new GradientColorKey(color,0),new GradientColorKey(color,1)},new[]{new GradientAlphaKey(1,0),new GradientAlphaKey(1,1)});return gradient;
        }
        void UpdatePointerDots()
        {
            expiredPointers.Clear();
            foreach(var pair in pointerDots)
            {
                var ray=pair.Key;var dot=pair.Value;
                if(!ray || !dot){expiredPointers.Add(ray);continue;}
                bool visible=canvas.enabled && ray.isActiveAndEnabled;
                if(!visible || !ray.TryGetCurrentUIRaycastResult(out var hit)){dot.SetActive(false);continue;}
                dot.SetActive(true);dot.transform.position=hit.worldPosition-ray.transform.forward*.008f;
                var selectable=hit.gameObject?hit.gameObject.GetComponentInParent<Selectable>():null;
                pointerColor.SetColor("_Color",selectable && selectable.IsInteractable()?Color.white:Color.cyan);
                dot.GetComponent<Renderer>().SetPropertyBlock(pointerColor);
            }
            foreach(var ray in expiredPointers){pointerDots.Remove(ray);rayPressed.Remove(ray);}
        }
        static void DisposeActions(List<InputAction> actions){foreach(var action in actions){action.Disable();action.Dispose();}actions.Clear();}
        void OnDestroy()
        {
            RenderPipelineManager.beginCameraRendering-=BeforeCamera;
            options?.Dispose();foreach(var material in materials)if(material)Destroy(material);
            foreach(var material in gameMaterials)if(material)Destroy(material);
            foreach(var ray in gameRays)if(ray){ray.SetActive(false);Destroy(ray);}
            if(menuOrigin)menuOrigin.gameObject.SetActive(false);
            DisposeActions(menuActions);DisposeActions(gameActions);
        }
    }
}
