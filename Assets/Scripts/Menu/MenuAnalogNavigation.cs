using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace LaserTag.Menu
{
    // One owner for XR submit. Pointer hover never replaces the stored analog focus.
    [DefaultExecutionOrder(-200)]
    public sealed class MenuAnalogNavigation : MonoBehaviour
    {
        [Range(.1f,.95f)] public float Deadzone = .5f;
        public float InitialRepeat = .35f, RepeatInterval = .15f;
        Canvas canvas;
        EventSystem events;
        InputAction move, submit, leftRaySubmit;
        Selectable focus;
        HelpInstructionViewer helpViewer;
        Dropdown openDropdown, closingDropdown;
        readonly List<Selectable> controls = new List<Selectable>();
        readonly Dictionary<string,string> remembered = new Dictionary<string,string>();
        string page;
        readonly StickRepeat repeat = new StickRepeat();
        bool releaseRequired, wasVisible, layoutPending;
        public bool GameplayReleasePending { get; private set; }
        RectTransform border;
        readonly Dictionary<XRRayInteractor,Pose> observedRays = new Dictionary<XRRayInteractor,Pose>();
        bool InputsHeld => submit.IsPressed() || leftRaySubmit.IsPressed();
        public bool AnalogOwnsInput { get; private set; } = true;
        public bool LeftRayHeld => leftRaySubmit != null && leftRaySubmit.IsPressed() && !releaseRequired;
        public bool TriggerHeld => submit != null && submit.IsPressed() && !releaseRequired;

        public void Configure(Canvas view, EventSystem eventSystem)
        {
            canvas=view;events=eventSystem;
            move=new InputAction("Menu navigation",InputActionType.Value,"<XRController>{LeftHand}/primary2DAxis");
            submit=new InputAction("Menu confirm",InputActionType.Button,"<XRController>{RightHand}/triggerPressed");
            leftRaySubmit=new InputAction("Left menu ray",InputActionType.Button,"<XRController>{LeftHand}/triggerPressed");
            move.Enable();submit.Enable();leftRaySubmit.Enable();
            var go=new GameObject("Analog focus",typeof(RectTransform));
            border=(RectTransform)go.transform;border.SetParent(canvas.transform,false);
            var overlay=go.AddComponent<Canvas>();overlay.overrideSorting=true;overlay.sortingOrder=30001;
            for(int i=0;i<4;i++)
            {
                var edge=new GameObject("Cyan border",typeof(RectTransform),typeof(Image));edge.transform.SetParent(border,false);
                var image=edge.GetComponent<Image>();image.color=new Color(.15f,1,1);image.raycastTarget=false;
                var r=image.rectTransform;
                r.anchorMin=i<2?new Vector2(0,i):new Vector2(i-2,0);
                r.anchorMax=i<2?new Vector2(1,i):new Vector2(i-2,1);
                r.sizeDelta=i<2?new Vector2(0,4):new Vector2(4,0);r.anchoredPosition=Vector2.zero;
            }
            border.gameObject.SetActive(false);
        }
        public void ChangePage(string next)
        {
            if(focus && page!=null)remembered[page]=focus.name;
            page=next;focus=null;helpViewer=null;openDropdown=null;closingDropdown=null;layoutPending=true;
            if(next=="Game")GameplayReleasePending=true;
            releaseRequired=submit!=null && InputsHeld;repeat.Reset();
            AnalogOwnsInput=true;observedRays.Clear();
        }
        public void SetHelpViewer(HelpInstructionViewer viewer)
        {
            helpViewer=viewer;
            SetFocus(viewer);
        }
        public static Vector2Int Direction(Vector2 value,float deadzone)
        {
            if(Mathf.Max(Mathf.Abs(value.x),Mathf.Abs(value.y))<deadzone)return Vector2Int.zero;
            return Mathf.Abs(value.x)>Mathf.Abs(value.y)?new Vector2Int(value.x>0?1:-1,0):new Vector2Int(0,value.y>0?1:-1);
        }
        bool Valid(Selectable control) => control && control.isActiveAndEnabled && control.gameObject.activeInHierarchy && control.IsInteractable() &&
            control.navigation.mode!=Navigation.Mode.None && !(control is Scrollbar);
        void Collect()
        {
            controls.Clear();
            if(closingDropdown && !closingDropdown.transform.Find("Dropdown List"))closingDropdown=null;
            // uGUI keeps the fading list alive briefly after Hide. Do not focus it again.
            if(openDropdown && events.currentSelectedGameObject==openDropdown.gameObject)
            {closingDropdown=openDropdown;openDropdown=null;SetFocus(closingDropdown);}
            if(!openDropdown)
                foreach(var dropdown in canvas.GetComponentsInChildren<Dropdown>())
                {
                    var list=dropdown.transform.Find("Dropdown List");
                    if(dropdown!=closingDropdown && list && list.gameObject.activeInHierarchy){openDropdown=dropdown;break;}
                }
            if(openDropdown)
            {
                var list=openDropdown.transform.Find("Dropdown List");
                if(list)
                {
                    foreach(var item in list.GetComponentsInChildren<Toggle>())if(Valid(item))controls.Add(item);
                    return;
                }
                var previous=openDropdown;openDropdown=null;SetFocus(previous);
            }
            foreach(var item in canvas.GetComponentsInChildren<Selectable>())
                if(Valid(item) && (!item.GetComponentInParent<Dropdown>() || item is Dropdown))
                    controls.Add(item);
        }
        void Update()
        {
            if(!canvas || !events)return;
            if(GameplayReleasePending && !InputsHeld && Direction(move.ReadValue<Vector2>(),Deadzone)==Vector2Int.zero)GameplayReleasePending=false;
            bool visible=canvas.enabled && canvas.gameObject.activeInHierarchy;
            if(!visible){border.gameObject.SetActive(false);wasVisible=false;return;}
            if(!wasVisible){releaseRequired=InputsHeld;repeat.Reset();AnalogOwnsInput=true;}
            wasVisible=true;
            if(!InputsHeld)releaseRequired=false;
            if(layoutPending){Canvas.ForceUpdateCanvases();layoutPending=false;}Collect();
            if(!Valid(focus) || !controls.Contains(focus))
            {
                Selectable first=Valid(helpViewer)?helpViewer:null;
                if(!first && page!=null && remembered.TryGetValue(page,out var name))first=controls.Find(c=>c.name==name);
                SetFocus(first?first:controls.Count>0?controls[0]:null);
            }
            if(repeat.Step(move.ReadValue<Vector2>(),Time.unscaledTime,Deadzone,InitialRepeat,RepeatInterval,out var direction))
            {
                AnalogOwnsInput=true;observedRays.Clear();Navigate(direction);
            }
            if(AnalogOwnsInput && !releaseRequired && submit.WasPressedThisFrame())
            {
                releaseRequired=true;Activate();
            }
        }
        void Navigate(Vector2Int direction)
        {
            if(!focus)return;
            if(Valid(helpViewer))
            {
                if(direction.y>0)SetFocus(helpViewer.Back);
                else if(direction.y<0)SetFocus(helpViewer);
                else if(focus!=helpViewer.Back)
                {helpViewer.ChangeInstruction(direction.x);SetFocus(helpViewer);}
                return;
            }
            if(focus is Slider slider && direction.x!=0)
            {
                slider.value+=direction.x*(slider.wholeNumbers?1:(slider.maxValue-slider.minValue)*.05f);return;
            }
            if(openDropdown)
            {
                int index=controls.IndexOf(focus), delta=direction.y>0 || direction.x<0?-1:1;
                SetFocus(controls[Mathf.Clamp(index+delta,0,controls.Count-1)]);return;
            }
            // Spatial navigation supports both vertical rows and the six-column keypad.
            var origin=((RectTransform)focus.transform).TransformPoint(((RectTransform)focus.transform).rect.center);
            Vector3 axis=canvas.transform.TransformDirection(new Vector3(direction.x,direction.y,0));
            Selectable best=null;float score=float.MaxValue;
            foreach(var candidate in controls)
            {
                if(candidate==focus)continue;
                var rect=(RectTransform)candidate.transform;
                Vector3 offset=rect.TransformPoint(rect.rect.center)-origin;
                float forward=Vector3.Dot(offset,axis);
                if(forward<=.001f)continue;
                float side=(offset-axis*forward).magnitude;
                float cost=forward+side*4;
                if(cost<score){score=cost;best=candidate;}
            }
            if(best)SetFocus(best);
        }
        void Activate()
        {
            if(!Valid(focus))return;
            if(focus is InputField){GetComponent<MainMenuController>().OpenCodePad();return;}
            if(openDropdown && focus is Toggle item)
            {
                var dropdown=openDropdown;int index=controls.IndexOf(item);
                // Assign the actual dropdown value; generated Toggle submit does not reliably confirm.
                dropdown.value=index;closingDropdown=dropdown;dropdown.Hide();openDropdown=null;SetFocus(dropdown);return;
            }
            if(focus is Dropdown choice)
            {
                openDropdown=choice;choice.Show();Canvas.ForceUpdateCanvases();Collect();
                if(controls.Count>0)SetFocus(controls[Mathf.Clamp(choice.value,0,controls.Count-1)]);
                return;
            }
            ExecuteEvents.Execute(focus.gameObject,new BaseEventData(events),ExecuteEvents.submitHandler);
        }
        void SetFocus(Selectable next)
        {
            focus=next;
            if(focus){events.SetSelectedGameObject(focus.gameObject);Reveal(focus.transform as RectTransform);}
            else events.SetSelectedGameObject(null);
        }
        static void Reveal(RectTransform target)
        {
            Canvas.ForceUpdateCanvases();
            foreach(var scroll in target.GetComponentsInParent<ScrollRect>())
            {
                if(!scroll.content || !scroll.viewport || !target.IsChildOf(scroll.content))continue;
                var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(scroll.viewport,target);
                var view=scroll.viewport.rect;Vector2 shift=Vector2.zero;
                if(scroll.vertical)
                {
                    if(bounds.max.y>view.yMax)shift.y=view.yMax-bounds.max.y;
                    else if(bounds.min.y<view.yMin)shift.y=view.yMin-bounds.min.y;
                }
                if(scroll.horizontal)
                {
                    if(bounds.min.x<view.xMin)shift.x=view.xMin-bounds.min.x;
                    else if(bounds.max.x>view.xMax)shift.x=view.xMax-bounds.max.x;
                }
                scroll.StopMovement();
                scroll.content.position+=scroll.viewport.TransformVector(shift);
            }
        }
        void LateUpdate()
        {
            if(!border)return;
            border.gameObject.SetActive(canvas.enabled && Valid(focus));
            if(!border.gameObject.activeSelf)return;
            var rect=(RectTransform)focus.transform;
            border.position=rect.TransformPoint(rect.rect.center);border.rotation=rect.rotation;
            border.localScale=Vector3.one; border.sizeDelta=rect.rect.size+new Vector2(8,8);
            border.SetAsLastSibling();
        }
        public void ObserveRay(XRRayInteractor ray)
        {
            if(!observedRays.TryGetValue(ray,out var previous)){observedRays[ray]=new Pose(ray.transform.position,ray.transform.rotation);return;}
            bool moved=Vector3.Distance(previous.position,ray.transform.position)>.025f || Quaternion.Angle(previous.rotation,ray.transform.rotation)>4;
            if(moved && !submit.IsPressed() && !leftRaySubmit.IsPressed() && ray.TryGetCurrentUIRaycastResult(out var hit))
            {
                var control=hit.gameObject?hit.gameObject.GetComponentInParent<Selectable>():null;
                if(Valid(control))
                {
                    AnalogOwnsInput=false;SetFocus(control);
                    // Track ray-opened dropdowns as well as stick-opened dropdowns.
                    if(control is Toggle && control.GetComponentInParent<Dropdown>() is Dropdown dropdown)openDropdown=dropdown;
                }
                observedRays[ray]=new Pose(ray.transform.position,ray.transform.rotation);
            }
        }
        void OnDestroy(){move?.Dispose();submit?.Dispose();leftRaySubmit?.Dispose();}

        public sealed class StickRepeat
        {
            Vector2Int held;
            float next;
            public void Reset(){held=Vector2Int.zero;next=0;}
            public bool Step(Vector2 value,float now,float deadzone,float delay,float interval,out Vector2Int direction)
            {
                direction=Direction(value,deadzone);
                if(direction==Vector2Int.zero){Reset();return false;}
                bool fresh=direction!=held;
                if(!fresh && now<next)return false;
                held=direction;next=now+(fresh?delay:interval);return true;
            }
        }
    }
}
