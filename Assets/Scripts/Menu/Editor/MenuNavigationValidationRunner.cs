using System;
using System.Collections;
using System.Reflection;
using LaserTag.Menu;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

public sealed class MenuNavigationValidationRunner
{
    static MenuNavigationValidationRunner active;
    IEnumerator routine;
    double resumeAt;
    public static void Begin()
    {
        active=new MenuNavigationValidationRunner();active.routine=active.Start();
        EditorApplication.update+=active.Tick;
    }
    void Tick()
    {
        if(EditorApplication.timeSinceStartup<resumeAt)return;
        if(!routine.MoveNext()){EditorApplication.update-=Tick;return;}
        resumeAt=EditorApplication.timeSinceStartup+(routine.Current is WaitForSecondsRealtime wait?wait.waitTime:0);
    }
    const BindingFlags Private=BindingFlags.Instance|BindingFlags.NonPublic;
    MenuAnalogNavigation nav;
    MainMenuController menu;
    XRSimulatedController left,right;
    XRSimulatedControllerState rightState=new XRSimulatedControllerState{trackingState=3,isTracked=true,deviceRotation=Quaternion.identity};
    int checks,errors;
    string savedPreferences;
    bool hadPreferences;
    const string PreferencesKey="LaserTag.Menu.Settings.v1";
    void Check(bool condition,string label)
    {
        if(!condition)throw new Exception("FAIL menu navigation: "+label);
        checks++;Debug.Log("PASS menu navigation: "+label);
    }
    void Logs(string message,string trace,LogType type)
    {
        if(type==LogType.Exception || type==LogType.Error || type==LogType.Assert)errors++;
    }
    IEnumerator Start()
    {
        Application.logMessageReceived+=Logs;
        hadPreferences=PlayerPrefs.HasKey(PreferencesKey);savedPreferences=PlayerPrefs.GetString(PreferencesKey);
        var tests=Tests();bool success=true;
        while(true)
        {
            object wait=null;bool more=false;
            try {more=tests.MoveNext();if(more)wait=tests.Current;}
            catch(Exception e){Debug.LogException(e);success=false;}
            if(!success || !more)break;
            yield return wait;
        }
        Application.logMessageReceived-=Logs;
        if(hadPreferences)PlayerPrefs.SetString(PreferencesKey,savedPreferences);else PlayerPrefs.DeleteKey(PreferencesKey);
        PlayerPrefs.Save();
        Debug.Log("Menu navigation validation: "+checks+" passed, "+errors+" console errors");
        MenuNavigationValidation.Finish(success && errors==0);
    }
    Selectable Focus => (Selectable)typeof(MenuAnalogNavigation).GetField("focus",Private).GetValue(nav);
    string Page => (string)typeof(MainMenuController).GetField("panel",Private).GetValue(menu);
    T Control<T>(string name) where T:Selectable
    {
        foreach(var c in menu.GetComponentsInChildren<T>())if(c.name==name && c.gameObject.activeInHierarchy)return c;
        throw new Exception("Missing control "+name);
    }
    void FocusOn(Selectable control) => typeof(MenuAnalogNavigation).GetMethod("SetFocus",Private).Invoke(nav,new object[]{control});
    void Show(string page) => typeof(MainMenuController).GetMethod("Show",Private).Invoke(menu,new object[]{page});
    void Stick(Vector2 value) => InputSystem.QueueStateEvent(left,new XRSimulatedControllerState{primary2DAxis=value,trackingState=3,deviceRotation=Quaternion.identity});
    void Trigger(bool down)
    {
        rightState.trigger=down?1:0;rightState=rightState.WithButton(ControllerButton.TriggerButton,down);
        InputSystem.QueueStateEvent(right,rightState);
    }
    IEnumerator Tests()
    {
        yield return new WaitForSecondsRealtime(2);
        menu=MainMenuController.Instance;Check(menu,"MainMenu started");
        nav=menu.GetComponent<MenuAnalogNavigation>();Check(nav,"XR navigation configured");
        Check(typeof(MenuVrPresentation).GetField("pointerColor",Private).GetValue(menu.GetComponent<MenuVrPresentation>())!=null,"MaterialPropertyBlock initialized in lifecycle");
        foreach(var simulator in UnityEngine.Object.FindObjectsByType<XRInteractionSimulator>(FindObjectsSortMode.None))simulator.gameObject.SetActive(false);
        InputSystem.RegisterLayout<XRSimulatedController>();
        left=InputSystem.AddDevice<XRSimulatedController>();InputSystem.SetDeviceUsage(left,CommonUsages.LeftHand);
        right=InputSystem.AddDevice<XRSimulatedController>();InputSystem.SetDeviceUsage(right,CommonUsages.RightHand);
        Stick(Vector2.zero);Trigger(false);yield return new WaitForSecondsRealtime(.2f);
        Check(Focus && Focus.name=="PLAY","automatic initial focus");
        Trigger(true);yield return new WaitForSecondsRealtime(.7f);
        Check(Page=="Play","XR trigger runs real PLAY callback once while held");
        Check(Focus is Dropdown,"new panel selects first logical control");
        Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        var capacity=(Dropdown)Focus;int oldCapacity=capacity.value;
        Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        Check(Focus is Toggle,"dropdown opens and focuses actual options");
        Stick(Vector2.down);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.25f);
        Check(capacity.value==oldCapacity+1 && Focus==capacity,"dropdown confirms real value and restores focus");
        Stick(Vector2.down);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(Focus.name=="HOST GAME","down selects host");
        Stick(Vector2.down);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(Focus is InputField,"down selects code field");
        Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        Check(Page=="CodePad" && Focus.name=="A","code field opens connected keypad");
        Stick(Vector2.right);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(Focus.name=="B","keypad right");
        Stick(Vector2.down);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(Focus.name=="H","keypad down");
        Stick(Vector2.up);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(Focus.name=="B","keypad up");
        Stick(Vector2.left);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(Focus.name=="A","keypad left");
        for(int i=0;i<6;i++)
        {Trigger(true);yield return new WaitForSecondsRealtime(.08f);Trigger(false);yield return new WaitForSecondsRealtime(.08f);}
        Check((string)typeof(MainMenuController).GetField("pendingJoinCode",Private).GetValue(menu)=="AAAAAA","six-character entry through real key callbacks");
        FocusOn(Control<Button>("BACKSPACE"));Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        Check((string)typeof(MainMenuController).GetField("pendingJoinCode",Private).GetValue(menu)=="AAAAA","backspace edits actual pending code");
        FocusOn(Control<Button>("JOIN"));Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        Check(menu.GetComponent<MenuSessionController>().Status.Contains("six-character"),"keypad JOIN retains existing validation");
        FocusOn(Control<Button>("DONE"));Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        Check(Page=="Play" && Focus is InputField,"back restores previous code-field focus");
        FocusOn(Control<Button>("BACK"));Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        FocusOn(Control<Button>("SETTINGS"));Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        Check(Page=="Settings" && Focus is Slider,"settings first focus");
        var turn=(Slider)Focus;float oldTurn=turn.value;
        Stick(Vector2.right);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(turn.value>oldTurn && SettingsController.TurnDegreesPerSecond==turn.value,"slider updates real smooth-turn sensitivity");
        var mute=Control<Toggle>("Mute all audio");bool oldMute=mute.isOn;
        FocusOn(mute);Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        Check(mute.isOn!=oldMute,"trigger toggles actual setting");
        FocusOn(Control<Button>("APPLY"));yield return new WaitForSecondsRealtime(.1f);
        var scroll=Focus.GetComponentInParent<ScrollRect>();var viewport=scroll.viewport;
        var bounds=RectTransformUtility.CalculateRelativeRectTransformBounds(viewport,Focus.transform);
        Check(bounds.min.y>=viewport.rect.yMin-.5f && bounds.max.y<=viewport.rect.yMax+.5f,"focus automatically scrolls fully into view");
        Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        Check(menu.GetComponent<SettingsController>().Current.Mute==mute.isOn && PlayerPrefs.HasKey(PreferencesKey),"APPLY saves actual settings");
        Show("Home");yield return new WaitForSecondsRealtime(.1f);Show("Settings");yield return new WaitForSecondsRealtime(.1f);
        Check(Control<Toggle>("Mute all audio").isOn!=oldMute,"settings persist after reopening");
        // Ray uses the same trigger action as stick submit, with simulated pose input.
        Show("Home");yield return new WaitForSecondsRealtime(.2f);
        var help=Control<Button>("HOW TO PLAY");FocusOn(help);
        XRRayInteractor pointer=null;
        foreach(var ray in UnityEngine.Object.FindObjectsByType<XRRayInteractor>(FindObjectsSortMode.None))
            if(ray.name=="RightHand Menu Pointer")pointer=ray;
        Check(pointer,"tracked right ray retained");
        Vector3 origin=menu.MenuCamera.transform.position+menu.MenuCamera.transform.right*.2f;
        var rect=(RectTransform)help.transform;
        Quaternion aim=Quaternion.LookRotation(rect.TransformPoint(rect.rect.center)-origin,menu.MenuCamera.transform.up);
        rightState.devicePosition=pointer.transform.parent.InverseTransformPoint(origin);
        rightState.deviceRotation=Quaternion.Inverse(pointer.transform.parent.rotation)*aim;
        InputSystem.QueueStateEvent(right,rightState);yield return new WaitForSecondsRealtime(.5f);
        bool hitUI=pointer.TryGetCurrentUIRaycastResult(out var rayHit);
        Debug.Log("Ray diagnostic: active="+pointer.isActiveAndEnabled+" position="+pointer.transform.position+" forward="+pointer.transform.forward+" expected origin="+origin+" target="+rect.TransformPoint(rect.rect.center)+" UI hit="+hitUI+" hit="+(rayHit.gameObject?rayHit.gameObject.name:"none")+" module="+EventSystem.current.currentInputModule);
        Check(!nav.AnalogOwnsInput,"simulated ray motion takes ownership");
        Trigger(true);yield return new WaitForSecondsRealtime(.15f);Trigger(false);yield return new WaitForSecondsRealtime(.2f);
        Check(Page=="Help","ray trigger calls real callback exactly once");
        Check(Focus is HelpInstructionViewer && ((HelpInstructionViewer)Focus).PageIndex==0,"Help opening immediately focuses the first instruction");
        Stick(Vector2.down);yield return new WaitForSecondsRealtime(.1f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.1f);
        Check(nav.AnalogOwnsInput,"stick retakes ownership after ray");
        var viewer=menu.GetComponentInChildren<HelpInstructionViewer>();
        Check(viewer && Focus==viewer && viewer.PageIndex==0,"Help defaults to instruction card on page one");
        Stick(Vector2.left);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(viewer.PageIndex==0,"Help left boundary does not wrap");
        Stick(Vector2.right);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(viewer.PageIndex==1,"Help right flick advances exactly one page");
        Stick(Vector2.left);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(viewer.PageIndex==0,"Help left flick returns exactly one page");
        Stick(Vector2.up);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(Focus==viewer.Back,"Help up selects Back");
        Stick(Vector2.down);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(Focus==viewer,"Help down returns to instruction card");
        var helpViewport=viewer.GetComponentInParent<ScrollRect>().viewport;
        var cardBounds=RectTransformUtility.CalculateRelativeRectTransformBounds(helpViewport,viewer.transform);
        var backBounds=RectTransformUtility.CalculateRelativeRectTransformBounds(helpViewport,viewer.Back.transform);
        Check(cardBounds.min.y>=helpViewport.rect.yMin-.5f && backBounds.max.y<=helpViewport.rect.yMax+.5f,"Help card and Back fully visible together without scrolling");
        for(int i=0;i<viewer.PageCount;i++)
        {
            Check(viewer.PageIndex==i && viewer.Indicator.text==(i+1)+" / "+viewer.PageCount &&
                viewer.Body.text==HelpInstructionViewer.Instructions[i].Body &&
                viewer.Heading.text==HelpInstructionViewer.Instructions[i].Title &&
                viewer.Body.preferredHeight<=viewer.Body.rectTransform.rect.height+.5f &&
                viewer.Heading.preferredHeight<=viewer.Heading.rectTransform.rect.height+.5f,
                "Help page "+(i+1)+" displays correct title/text/indicator without clipping");
            Stick(Vector2.right);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        }
        Check(viewer.PageIndex==viewer.PageCount-1,"Help right boundary does not wrap");
        Stick(Vector2.up);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Trigger(true);yield return new WaitForSecondsRealtime(.1f);Trigger(false);yield return new WaitForSecondsRealtime(.1f);
        Check(Page=="Home" && Focus.name=="HOW TO PLAY","Help trigger on Back restores previous menu focus");
        Stick(Vector2.up);yield return new WaitForSecondsRealtime(.08f);Stick(Vector2.zero);yield return new WaitForSecondsRealtime(.08f);
        Check(Focus.name=="SETTINGS","ordinary menu navigation restored after Help");

        Show("Settings");yield return new WaitForSecondsRealtime(.2f);
        var hidden=Control<Button>("APPLY");FocusOn(hidden);hidden.gameObject.SetActive(false);yield return new WaitForSecondsRealtime(.1f);
        Check(Focus && Focus!=hidden,"hidden control repairs focus");
        Focus.enabled=false;yield return new WaitForSecondsRealtime(.1f);
        Check(Focus && Focus.isActiveAndEnabled,"disabled component repairs focus");
        Check(errors==0,"no recurring Unity Console errors during test");
    }
}
