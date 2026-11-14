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
        readonly List<XRDisplaySubsystem> displays = new List<XRDisplaySubsystem>();
        InputAction options;
        bool initialized, wasVisible;
        bool placementPending;
        int placementFrames;
        public bool OpenPressedThisFrame => options != null && options.WasPressedThisFrame();

        public void Configure(MainMenuController owner, Canvas view, EventSystem eventSystem, Camera camera)
        {
            controller=owner;canvas=view;events=eventSystem;menuCamera=camera;
            options=new InputAction("Session options",InputActionType.Button,"<XRController>{LeftHand}/menuButton");
            options.Enable();
            RenderPipelineManager.beginCameraRendering+=BeforeCamera;
        }
        bool DisplayRunning()
        {
            SubsystemManager.GetSubsystems(displays);
            foreach(var display in displays)if(display.running)return true;
            return false;
        }
        void Update()
        {
            if(!controller || !canvas)return;
            if(OpenPressedThisFrame)controller.ToggleOptions();
            if(!initialized)
            {
                if(!DisplayRunning())return;
                Initialize();
            }
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
        }
        void BeforeCamera(ScriptableRenderContext context,Camera camera)
        {
            if(!isActiveAndEnabled || !initialized || !placementPending || !canvas || !canvas.enabled || canvas.worldCamera!=camera)return;
            if(menuOrigin && camera==menuCamera && menuOrigin.CurrentTrackingOriginMode==TrackingOriginModeFlags.Unknown)return;
            // The camera's tracked pose is applied after Update. Place the panel
            // at rendering time, and wait for actual tracking on the menu origin.
            var driver=camera.GetComponent<TrackedPoseDriver>();
            if(driver && driver.trackingStateInput.action!=null &&
                (driver.trackingStateInput.action.ReadValue<int>()&3)!=3)return;
            canvas.transform.SetPositionAndRotation(camera.transform.position+camera.transform.forward*1.8f,camera.transform.rotation);
            placementPending=--placementFrames>0;
        }
        void Initialize()
        {
            initialized=true;
            uiManager=gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
            if(events.TryGetComponent<UnityEngine.InputSystem.UI.InputSystemUIInputModule>(out var old))
            {old.enabled=false;Destroy(old);}
            events.gameObject.AddComponent<XRUIInputModule>();
            canvas.renderMode=RenderMode.WorldSpace;
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
                Pose(go,"<XRController>{"+hand+"}","devicePosition","deviceRotation");
                AddRay(go,hand);
            }
            root.SetActive(true);wasVisible=false;
        }
        InputAction Action(string name,InputActionType type,string binding,bool game=false)
        {var action=new InputAction(name,type,binding);(game?gameActions:menuActions).Add(action);return action;}
        void Pose(GameObject go,string device,string position,string rotation)
        {
            var driver=go.AddComponent<TrackedPoseDriver>();
            driver.positionInput=new InputActionProperty(Action("UI position",InputActionType.Value,device+"/"+position));
            driver.rotationInput=new InputActionProperty(Action("UI rotation",InputActionType.Value,device+"/"+rotation));
            driver.trackingStateInput=new InputActionProperty(Action("UI tracking",InputActionType.Value,device+"/trackingState"));
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
            ray.interactionLayers=0;
            ray.uiPressInput.inputSourceMode=XRInputButtonReader.InputSourceMode.InputAction;
            ray.uiPressInput.inputActionPerformed=Action("UI press",InputActionType.Button,"<XRController>{"+hand+"}/triggerPressed",game);
            ray.uiPressInput.inputActionValue=Action("UI pressure",InputActionType.Value,"<XRController>{"+hand+"}/trigger",game);
            ray.uiScrollInput.inputSourceMode=XRInputValueReader.InputSourceMode.InputAction;
            ray.uiScrollInput.inputAction=Action("UI scroll",InputActionType.Value,"<XRController>{"+hand+"}/primary2DAxis",game);
            var line=go.AddComponent<LineRenderer>();line.widthMultiplier=.003f;line.positionCount=2;
            line.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;line.receiveShadows=false;
            var material=new Material(Shader.Find("Sprites/Default"));material.color=Color.cyan;(game?gameMaterials:materials).Add(material);line.sharedMaterial=material;
            go.AddComponent<UnityEngine.XR.Interaction.Toolkit.Interactors.Visuals.XRInteractorLineVisual>();
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
