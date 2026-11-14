using System;
using System.Linq;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.UI;

namespace LaserTag.Menu
{
    // Desktop uGUI and the headset world-space view share the same session actions.
    public sealed class MainMenuController : MonoBehaviour
    {
        public static MainMenuController Instance { get; private set; }
        public static bool GameplayInputBlocked => Instance && Instance.session && Instance.session.InGame &&
            (Instance.panel != "Game" || (Instance.vrPresentation && Instance.vrPresentation.OpenPressedThisFrame) ||
             (Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame));
        MenuVrPresentation vrPresentation;
        public bool OptionsOpen => session && session.InGame && panel != "Game";
        string settingsReturnPage = "Home", pendingJoinCode = "";
        string helpReturnPage = "Home";
        void Awake() { if (GetComponent<MenuSessionController>() == MenuSessionController.Instance) Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics() { Instance = null; }
        public void ToggleOptions() { if (session && session.InGame) Show(panel == "Game" ? "Pause" : "Game"); }
        public Camera MenuCamera;
        MenuSessionController session;
        Canvas canvas;
        RectTransform content;
        CanvasGroup group;
        Text status, lobbyRows, roomCode, count;
        Button start, ready;
        InputField joinCode;
        Dropdown capacity;
        string panel = "Home";
        float fade;
        string uiNotice;
        float noticeUntil;
        readonly Color cyan = new Color(.12f, .88f, 1f);
        Font font;

        void Start()
        {
            session = GetComponent<MenuSessionController>();
            if (MenuSessionController.Instance != session) return;
            Instance = this; // All session Awakes have completed before Start.
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            var root = new GameObject("Menu Canvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            root.transform.SetParent(transform, false); canvas = root.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay; canvas.sortingOrder = 500;
            var scaler = root.GetComponent<CanvasScaler>(); scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1280, 900); scaler.matchWidthOrHeight = .5f;
            var bg = Image("Backdrop", root.transform, new Color(.018f,.035f,.065f,.98f)); Stretch(bg.rectTransform);
            var strip = Image("Signal rail", root.transform, cyan); strip.rectTransform.anchorMin = new Vector2(.08f,.08f); strip.rectTransform.anchorMax = new Vector2(.083f,.92f); strip.rectTransform.offsetMin = strip.rectTransform.offsetMax = Vector2.zero;
            var heading = Label(root.transform, "LASER TAG", 54, cyan); Place(heading.rectTransform, .14f,.83f,.9f,.94f);
            var sub = Label(root.transform, "MULTIPLAYER  /  RELAY LINK", 16, new Color(.5f,.66f,.77f)); Place(sub.rectTransform,.145f,.80f,.9f,.84f);
            var viewport = new GameObject("Panel Viewport", typeof(RectTransform), typeof(Image), typeof(Mask), typeof(ScrollRect)); viewport.transform.SetParent(root.transform, false);
            Place((RectTransform)viewport.transform,.14f,.16f,.88f,.78f); viewport.GetComponent<Image>().color = new Color(.04f,.075f,.12f); viewport.GetComponent<Mask>().showMaskGraphic = true;
            var body = new GameObject("Panel Content",typeof(RectTransform),typeof(VerticalLayoutGroup),typeof(ContentSizeFitter),typeof(CanvasGroup)); body.transform.SetParent(viewport.transform,false);
            content = (RectTransform)body.transform; content.anchorMin = new Vector2(0,1); content.anchorMax = Vector2.one; content.pivot = new Vector2(.5f,1); content.sizeDelta = Vector2.zero;
            var layout = body.GetComponent<VerticalLayoutGroup>(); layout.padding = new RectOffset(28,28,22,22); layout.spacing = 12; layout.childControlHeight = true; layout.childForceExpandHeight = false;
            body.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize; group = body.GetComponent<CanvasGroup>();
            var scroll = viewport.GetComponent<ScrollRect>(); scroll.viewport = (RectTransform)viewport.transform; scroll.content = content; scroll.horizontal = false; scroll.scrollSensitivity = 32;
            var barImage=Image("Panel scrollbar",root.transform,new Color(.04f,.09f,.13f));Place(barImage.rectTransform,.89f,.16f,.90f,.78f);
            var bar=barImage.gameObject.AddComponent<Scrollbar>();var handle=Image("Handle",barImage.transform,cyan);Stretch(handle.rectTransform);bar.handleRect=handle.rectTransform;bar.targetGraphic=handle;bar.direction=Scrollbar.Direction.BottomToTop;
            scroll.verticalScrollbar=bar;scroll.verticalScrollbarVisibility=ScrollRect.ScrollbarVisibility.AutoHide;
            status = Label(root.transform, "",18,new Color(.62f,.79f,.87f)); Place(status.rectTransform,.145f,.035f,.88f,.15f); status.alignment = TextAnchor.UpperLeft;
            var events = new GameObject("Menu EventSystem",typeof(EventSystem),typeof(InputSystemUIInputModule)); events.transform.SetParent(transform,false); events.GetComponent<InputSystemUIInputModule>().AssignDefaultActions();
            vrPresentation=gameObject.AddComponent<MenuVrPresentation>();
            vrPresentation.Configure(this,canvas,events.GetComponent<EventSystem>(),MenuCamera);
            Show("Home");
        }
        void Update()
        {
            if (!canvas || !session) return;
            bool playing = session.InGame;
            if (MenuCamera) MenuCamera.gameObject.SetActive(UnityEngine.SceneManagement.SceneManager.GetActiveScene().name == MenuSessionController.MenuScene);
            if (playing && panel != "Game" && panel != "Pause" && panel != "GameSettings" && panel != "GameHelp") Show("Game");
            if (playing && Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame) ToggleOptions();
            if (panel == "Game" && playing) { canvas.enabled = false; return; }
            canvas.enabled = true;
            if (session.Leaving && panel != "Disconnecting") Show("Disconnecting");
            else if (!session.Leaving && session.Lobby && session.Lobby.Phase.Value != SessionPhase.Lobby && !playing && panel != "Loading") Show("Loading");
            else if (!session.Leaving && session.Lobby && session.Lobby.Phase.Value == SessionPhase.Lobby && panel != "Lobby") Show("Lobby");
            else if (!session.Leaving && !session.Lobby && (panel == "Lobby" || panel == "Loading" || panel == "Disconnecting" || panel == "Pause" || panel == "Game" || panel == "GameSettings" || panel == "GameHelp")) Show("Play");
            fade = Mathf.MoveTowards(fade,1,Time.unscaledDeltaTime*5); group.alpha = fade;
            status.text = Time.unscaledTime<noticeUntil?uiNotice:session.Status;
            if (session.Connecting) status.text = session.Status + new string('.', (int)(Time.unscaledTime*2)%4);
            foreach(var button in content.GetComponentsInChildren<Button>())
                if (button.name != "Cancel connection" && button.name != "Leave room" && button.name != "Return to menu") button.interactable = !session.Connecting && !session.Leaving;
            if (session.Lobby && panel == "Lobby")
            {
                var state = session.Lobby; roomCode.text = "ROOM  " + state.Code.Value;
                count.text = state.Members.Count + " / " + state.Capacity.Value + " connected";
                var rows = new System.Text.StringBuilder();
                foreach(var row in state.Members) rows.AppendLine(row.Name + (row.Id == session.Manager.LocalClientId ? "  (you)" : "") + "    /    " + (row.Ready ? "READY" : "not ready"));
                lobbyRows.text = rows.ToString();
                ready.interactable = !session.Leaving; start.interactable = session.CanStart;
                start.gameObject.SetActive(session.Manager.IsHost);
            }
            if (panel == "Loading") status.text = session.Status + (session.RealLoadProgress >= 0 ? "  " + Mathf.RoundToInt(session.RealLoadProgress*100) + "%" : new string('.', (int)(Time.unscaledTime*3)%4));
        }
        void Show(string page)
        {
            panel = page; fade = 0;
            foreach (Transform child in content) { child.gameObject.SetActive(false); Destroy(child.gameObject); }
            content.anchoredPosition = Vector2.zero;
            if (page == "Home")
            { Title("ENTER THE ARENA"); Note("Create a private room. Share the code. Ready up together."); Action("PLAY",()=>Show("Play")); Action("SETTINGS",()=>OpenSettings("Home")); Action("HOW TO PLAY",()=>OpenHelp("Home")); Action("QUIT",Quit); }
            else if (page == "Play")
            {
                Title("MULTIPLAYER"); Note("Host a Relay room or join with a six-character code.");
                capacity = Choice("Maximum players (including host)",Enumerable.Range(2,7).Select(x=>x.ToString()).ToArray(),3);
                Action("HOST GAME",()=>session.Host(capacity.value+2));
                joinCode = Field("Room code", "ABC123"); joinCode.text=pendingJoinCode;
                Action("ROOM CODE KEYPAD",()=>{pendingJoinCode=joinCode.text;Show("CodePad");});
                Action("JOIN GAME",()=>session.Join(joinCode.text));
                Action("Cancel connection",()=>session.Leave("Connection cancelled.")); Action("BACK",()=>Show("Home"));
            }
            else if (page == "Lobby")
            {
                Title("CREW LOBBY"); roomCode = Note(""); count = Note("");
                Action("COPY ROOM CODE",()=>{ GUIUtility.systemCopyBuffer = session.Lobby.Code.Value.ToString(); Notify("Room code copied."); });
                lobbyRows = Note(""); lobbyRows.GetComponent<LayoutElement>().minHeight = 110;
                ready = Action("TOGGLE READY",()=>session.Lobby.ToggleLocalReady());
                start = Action("START GAME",session.StartMatch);
                Note("Everyone, including the host, must be ready. The host starts the match."); Action("Leave room",()=>session.Leave());
            }
            else if (page == "Loading") { Title("DEPLOYING"); Note("Loading arena and synchronizing players."); Note("Your connection stays active. Please wait..."); Action("Return to menu",()=>session.Leave("Loading cancelled.")); }
            else if (page == "Disconnecting") { Title("DISCONNECTING"); Note("Closing the current connection..."); }
            else if (page == "Pause") { Title("SESSION"); Note("The match continues. Shooting and teleportation are blocked locally while options are open."); Action("RESUME",()=>Show("Game")); Action("SETTINGS",()=>OpenSettings("Pause")); Action("HOW TO PLAY",()=>OpenHelp("Pause")); Action("Return to menu",()=>session.Leave()); }
            else if (page == "CodePad") BuildCodePad();
            else if (page == "Help" || page == "GameHelp") BuildHelp();
            else if (page == "Settings" || page == "GameSettings") BuildSettings();
        }
        void OpenSettings(string back) { settingsReturnPage=back;Show(back=="Pause"?"GameSettings":"Settings"); }
        void OpenHelp(string back) { helpReturnPage=back;Show(back=="Pause"?"GameHelp":"Help"); }
        void BuildHelp()
        {
            Title("HOW TO PLAY");Action("BACK",()=>Show(helpReturnPage));
            Note("VR controls and default match rules. Scroll for all topics. The host's configured rules apply to everyone.");
            Title("1 / JOIN THE MATCH");
            Note("One player chooses PLAY > HOST GAME and shares the six-character room code. Other players choose JOIN GAME and enter it, using the room-code keypad if needed. Choose five slots for the five-player session. Everyone, including the host, must toggle READY. Only the host starts the match; at least two players are needed for a round. Wait for loading and the three-second countdown before shooting.");
            Title("2 / TELEPORT, LOOK AND AIM");
            Note("Use the LEFT hand for teleporting. Push the left thumbstick forward to display the arc, aim at a clear floor area, then release the stick to request the teleport. Cyan marks a valid preview; red means it is blocked. A cyan preview is not approval: the server can reject the request if the destination becomes occupied before landing. Walls, cover, prop tops, unsupported edges, steep slopes, excessive height changes and other players block landings. Teleporting is unavailable while dead or while options are open.");
            Note("Look and turn naturally with your headset, and aim with your RIGHT controller. Stay inside your own cleared physical play area with Quest boundaries enabled. Use teleporting to travel through the virtual arena. Do not physically walk through virtual cover or rely on boundary warnings to clear your room.");
            Note("Every player has a different LED color on their suit. A small matching name tag above each opponent identifies them; walls and cover can hide it. Your own name tag is hidden from your view.");
            Title("3 / SHOOTING AND COVER");
            Note("Aim the right-hand gun and pull the RIGHT TRIGGER for one shot. Release and pull again; the default cooldown is 0.25 seconds. The left hand is reserved for teleporting. Shots reach up to 50 metres. Solid cover stops lasers. You cannot hit yourself. Both head and body hits deal 10 damage; head hits have no extra damage bonus. A confirmed hit marker and sound mean damage was accepted. A beam alone does not guarantee a hit.");
            Title("4 / AMMUNITION AND HEALTH");
            Note("Start with 100 ammunition and 100 health. Each accepted shot costs one ammo. After two seconds without an accepted shot, ammo regenerates at eight shots per second, up to 100. There is no manual reload. Low ammo is highlighted; an empty gun clicks and cannot shoot. Empty trigger pulls do not postpone regeneration. Watch your health and ammo HUD, and move behind cover when under fire.");
            Title("5 / LIVES AND RESPAWNING");
            Note("Each player has two lives. At zero health you are tagged out and lose one life. After the first tag-out, wait three seconds to respawn at a clear, unoccupied spawn point with full health and ammo. If all spawns are blocked, respawning waits for space instead of placing you inside cover. You have two seconds of spawn protection; landing a validated hit ends your protection early. Shooting and teleporting are blocked while waiting to respawn. Losing your final life eliminates you until the next round.");
            Title("6 / SCORING, WINNING AND RESTART");
            Note("The last remaining player wins. The scoreboard shows finishing rank, kills, hits and deaths. Surviving longer ranks first; ties use more kills, then more hits, then fewer deaths. Tagging a player out counts as a kill, including a tag-out followed by a respawn. At game over, press the RIGHT controller A button to request a restart. A new round resets health, ammo, lives and round statistics. At least two connected players are required to restart.");
            Title("7 / OPTIONS AND LEAVING");
            Note("Use the left controller menu button to open session options. Point a controller ray at a button and use its trigger to select; use the thumbstick to scroll. Settings control master volume, music/ambience, sound effects, mute and graphics. The match keeps running while your options or help are open, but your firing and teleport inputs are blocked. Choose RESUME to return. RETURN TO MENU disconnects you. If the host leaves, the room closes for everyone; host or join a new room to play again. New players cannot join a match after loading starts.");
            Action("BACK",()=>Show(helpReturnPage));
        }
        void BuildCodePad()
        {
            Title("ROOM CODE");var display=Note(pendingJoinCode);Note("Select six letters or digits, then DONE.");
            var grid=new GameObject("Code keys",typeof(RectTransform),typeof(GridLayoutGroup),typeof(LayoutElement));grid.transform.SetParent(content,false);
            var layout=grid.GetComponent<GridLayoutGroup>();layout.cellSize=new Vector2(70,44);layout.spacing=new Vector2(8,8);layout.constraint=GridLayoutGroup.Constraint.FixedColumnCount;layout.constraintCount=6;
            grid.GetComponent<LayoutElement>().minHeight=304;
            foreach(char c in "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789") { char key=c;var button=Action(key.ToString(),()=>{if(pendingJoinCode.Length<6)pendingJoinCode+=key;display.text=pendingJoinCode;});button.transform.SetParent(grid.transform,false); }
            Action("BACKSPACE",()=>{if(pendingJoinCode.Length>0)pendingJoinCode=pendingJoinCode.Substring(0,pendingJoinCode.Length-1);display.text=pendingJoinCode;});Action("DONE",()=>Show("Play"));
        }
        void BuildSettings()
        {
            var settings = GetComponent<SettingsController>(); var draft = settings.Copy(); Title("SETTINGS");
            Slider master = Range("Master volume",draft.Master,0,1), fx = Range("Sound effects",draft.Effects,0,1), music = Range("Music / ambience",draft.Music,0,1);
            Toggle mute = Check("Mute all audio",draft.Mute);
            Slider snap = settings.SupportsSnapTurn ? Range("Snap turn angle",draft.SnapAngle,15,90) : null;
            Dropdown quality = Choice("Graphics quality",QualitySettings.names,draft.Quality);
            Slider sensitivity = SettingsController.SimulatorSettingsSupported ? Range("Simulator look sensitivity (mouse + keyboard)",draft.Sensitivity,.05f,.5f) : null;
            if(sensitivity)Note("Sensitivity applies only to the XR Interaction Simulator; it never changes headset tracking.");
            Dropdown mode = null, resolution = null;
            if (SettingsController.DesktopDisplaySupported)
            { mode = Choice("Display mode",new[]{"Windowed","Fullscreen"},draft.Fullscreen?1:0); resolution = Choice("Resolution",settings.ResolutionNames,settings.ResolutionIndex(draft.Width,draft.Height)); }
            Action("APPLY",()=>{ draft.Master=master.value;draft.Effects=fx.value;draft.Music=music.value;draft.Mute=mute.isOn;draft.Quality=quality.value;if(sensitivity)draft.Sensitivity=sensitivity.value;if(snap)draft.SnapAngle=snap.value;if(mode){draft.Fullscreen=mode.value==1;var size=settings.ResolutionSizes[resolution.value];draft.Width=size.x;draft.Height=size.y;}settings.Apply(draft);Notify("Settings saved."); });
            Action("RESET TO DEFAULTS",()=>{settings.Apply(settings.Defaults());Show(settingsReturnPage=="Pause"?"GameSettings":"Settings");Notify("Default settings restored.");}); Action("BACK",()=>Show(settingsReturnPage));
        }
        void Notify(string message){uiNotice=message;noticeUntil=Time.unscaledTime+3;}
        void Quit() {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }
        Text Title(string value) => RowLabel(value,28,cyan,46);
        Text Note(string value) => RowLabel(value,18,new Color(.72f,.83f,.9f),38);
        Text RowLabel(string value,int size,Color color,float height)
        { var text=Label(content,value,size,color);var e=text.gameObject.AddComponent<LayoutElement>();e.minHeight=height; text.gameObject.AddComponent<ContentSizeFitter>().verticalFit=ContentSizeFitter.FitMode.PreferredSize;return text; }
        Button Action(string value,Action action)
        {
            var image=Image(value,content,new Color(.06f,.17f,.24f));var e=image.gameObject.AddComponent<LayoutElement>();e.minHeight=52;
            var button=image.gameObject.AddComponent<Button>();var colors=button.colors;colors.normalColor=Color.white;colors.highlightedColor=new Color(.3f,.93f,1);colors.selectedColor=new Color(.4f,.82f,1);colors.pressedColor=new Color(.12f,.65f,.8f);button.colors=colors;
            var text=Label(image.transform,value,20,Color.white);Stretch(text.rectTransform);text.alignment=TextAnchor.MiddleCenter;
            button.onClick.AddListener(()=>{GetComponent<MenuAudio>().Click();action();});return button;
        }
        InputField Field(string title,string placeholder)
        {
            Note(title);var image=Image(title,content,new Color(.02f,.04f,.07f));image.gameObject.AddComponent<LayoutElement>().minHeight=50;
            var input=image.gameObject.AddComponent<InputField>();input.characterLimit=6;input.contentType=InputField.ContentType.Alphanumeric;
            var text=Label(image.transform,"",24,Color.white);Stretch(text.rectTransform);text.rectTransform.offsetMin=new Vector2(16,4);input.textComponent=text;
            var hint=Label(image.transform,placeholder,24,new Color(.3f,.45f,.55f));Stretch(hint.rectTransform);hint.rectTransform.offsetMin=new Vector2(16,4);input.placeholder=hint;return input;
        }
        Dropdown Choice(string title,string[] options,int selected)
        {
            Note(title);var image=Image(title,content,new Color(.07f,.16f,.22f));image.gameObject.AddComponent<LayoutElement>().minHeight=46;
            var dd=image.gameObject.AddComponent<Dropdown>();var caption=Label(image.transform,"",20,Color.white);Stretch(caption.rectTransform);caption.rectTransform.offsetMin=new Vector2(12,3);dd.captionText=caption;
            var arrow=Label(image.transform,"v",20,cyan);Place(arrow.rectTransform,.93f,0,.99f,1);arrow.alignment=TextAnchor.MiddleCenter;
            var template=Image("Options",image.transform,new Color(.04f,.10f,.16f));var tr=template.rectTransform;tr.anchorMin=new Vector2(0,0);tr.anchorMax=new Vector2(1,0);tr.pivot=new Vector2(.5f,1);tr.sizeDelta=new Vector2(0,180);
            var scroll=template.gameObject.AddComponent<ScrollRect>();scroll.horizontal=false;
            var view=Image("Viewport",template.transform,new Color(.04f,.10f,.16f));Stretch(view.rectTransform);view.gameObject.AddComponent<Mask>().showMaskGraphic=false;
            var items=new GameObject("Content",typeof(RectTransform));items.transform.SetParent(view.transform,false);var ir=(RectTransform)items.transform;ir.anchorMin=new Vector2(0,1);ir.anchorMax=Vector2.one;ir.pivot=new Vector2(.5f,1);ir.sizeDelta=new Vector2(0,36);
            var item=Image("Item",items.transform,new Color(.10f,.30f,.39f));Stretch(item.rectTransform);var toggle=item.gameObject.AddComponent<Toggle>();toggle.targetGraphic=item;toggle.graphic=item;
            var itemText=Label(item.transform,"Option",20,Color.white);Stretch(itemText.rectTransform);itemText.rectTransform.offsetMin=new Vector2(12,2);
            scroll.content=ir;scroll.viewport=view.rectTransform;dd.template=tr;dd.itemText=itemText;dd.AddOptions(options.ToList());dd.value=Mathf.Clamp(selected,0,options.Length-1);dd.RefreshShownValue();template.gameObject.SetActive(false);return dd;
        }
        Slider Range(string title,float value,float min,float max)
        {
            var label=Note(title+"  "+value.ToString("0.00"));var image=Image(title,content,new Color(.07f,.17f,.23f));image.gameObject.AddComponent<LayoutElement>().minHeight=30;
            var slider=image.gameObject.AddComponent<Slider>();slider.minValue=min;slider.maxValue=max;
            var fill=Image("Fill",image.transform,cyan);Stretch(fill.rectTransform);slider.fillRect=fill.rectTransform;
            var handle=Image("Handle",image.transform,Color.white);handle.rectTransform.anchorMin=handle.rectTransform.anchorMax=new Vector2(.5f,.5f);handle.rectTransform.sizeDelta=new Vector2(16,30);slider.handleRect=handle.rectTransform;slider.targetGraphic=handle;slider.value=value;slider.onValueChanged.AddListener(v=>label.text=title+"  "+v.ToString("0.00"));return slider;
        }
        Toggle Check(string title,bool value)
        {var image=Image(title,content,new Color(.06f,.17f,.23f));image.gameObject.AddComponent<LayoutElement>().minHeight=44;var toggle=image.gameObject.AddComponent<Toggle>();toggle.targetGraphic=image;var tick=Image("Selected",image.transform,cyan);Place(tick.rectTransform,.02f,.25f,.06f,.75f);toggle.graphic=tick;var label=Label(image.transform,title+(value?"  [ON]":"  [OFF]"),20,Color.white);Place(label.rectTransform,.09f,0,1,1);toggle.isOn=value;toggle.onValueChanged.AddListener(on=>label.text=title+(on?"  [ON]":"  [OFF]"));return toggle;}
        Text Label(Transform parent,string value,int size,Color color)
        {var go=new GameObject("Text",typeof(RectTransform),typeof(Text));go.transform.SetParent(parent,false);var t=go.GetComponent<Text>();t.font=font;t.fontSize=size;t.color=color;t.text=value;t.supportRichText=false;t.raycastTarget=false;return t;}
        Image Image(string name,Transform parent,Color color)
        {var go=new GameObject(name,typeof(RectTransform),typeof(Image));go.transform.SetParent(parent,false);var im=go.GetComponent<Image>();im.color=color;return im;}
        static void Stretch(RectTransform rt){rt.anchorMin=Vector2.zero;rt.anchorMax=Vector2.one;rt.offsetMin=rt.offsetMax=Vector2.zero;}
        static void Place(RectTransform rt,float x,float y,float right,float top){rt.anchorMin=new Vector2(x,y);rt.anchorMax=new Vector2(right,top);rt.offsetMin=rt.offsetMax=Vector2.zero;}
    }
}





