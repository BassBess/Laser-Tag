using UnityEngine;
using UnityEngine.UI;

namespace LaserTag.Menu
{
    // One selectable card; controller input remains owned by MenuAnalogNavigation.
    public sealed class HelpInstructionViewer : Selectable
    {
        public Button Back { get; private set; }
        public int PageIndex { get; private set; }
        public int PageCount => Instructions.Length;
        public Text Heading { get; private set; }
        public Text Body { get; private set; }
        public Text Indicator { get; private set; }
        Button previous, next;
        public void Configure(Button back, Text heading, Text body, Text indicator, Button previousButton, Button nextButton)
        {
            Back=back;Heading=heading;Body=body;Indicator=indicator;previous=previousButton;next=nextButton;
            previous.onClick.AddListener(()=>ChangeInstruction(-1));
            next.onClick.AddListener(()=>ChangeInstruction(1));
            ShowInstruction();
        }
        public void ChangeInstruction(int direction)
        {
            PageIndex=Mathf.Clamp(PageIndex+direction,0,PageCount-1);
            ShowInstruction();
        }
        void ShowInstruction()
        {
            Heading.text=Instructions[PageIndex].Title;
            Body.text=Instructions[PageIndex].Body;
            Indicator.text=(PageIndex+1)+" / "+PageCount;
            previous.interactable=PageIndex>0;next.interactable=PageIndex<PageCount-1;
        }
        public readonly struct Instruction
        {
            public readonly string Title, Body;
            public Instruction(string title,string body){Title=title;Body=body;}
        }
        public static readonly Instruction[] Instructions =
        {
            new Instruction("HOW TO PLAY","VR controls and default match rules. The host's configured rules apply to everyone."),
            new Instruction("JOIN THE MATCH","One player chooses PLAY > HOST GAME and shares the six-character room code. Other players choose JOIN GAME and enter it, using the room-code keypad if needed. Choose five slots for the five-player session. Everyone, including the host, must toggle READY."),
            new Instruction("JOIN THE MATCH","Only the host starts the match; at least two players are needed for a round. Wait for loading and the three-second countdown before shooting."),
            new Instruction("TELEPORT, LOOK AND AIM","Use the LEFT hand for teleporting. Push the left thumbstick forward to display the arc, aim at a clear floor area, then release the stick to request the teleport. You have 15 teleports per burst; 2.5 seconds after the last successful teleport restores the burst, including when exhausted."),
            new Instruction("TELEPORT, LOOK AND AIM","Cyan marks a valid preview; red means it is blocked. A cyan preview is not approval: the server can reject the request if the destination becomes occupied before landing. Walls, cover, prop tops, unsupported edges, steep slopes, excessive height changes and other players block landings."),
            new Instruction("TELEPORT, LOOK AND AIM","Teleporting is unavailable while dead or while options are open."),
            new Instruction("TELEPORT, LOOK AND AIM","Look naturally with your headset. Push the RIGHT thumbstick left/right to turn smoothly. Adjust Turn Sensitivity in Settings. Aim with your RIGHT controller. Stay inside your own cleared physical play area with Quest boundaries enabled. Use teleporting to travel through the virtual arena."),
            new Instruction("TELEPORT, LOOK AND AIM","Do not physically walk through virtual cover or rely on boundary warnings to clear your room."),
            new Instruction("TELEPORT, LOOK AND AIM","Every player has a different LED color on their suit. A small matching name tag above each opponent identifies them; walls and cover can hide it. Your own name tag is hidden from your view."),
            new Instruction("SHOOTING AND COVER","Aim the right-hand gun and pull the RIGHT TRIGGER for one shot. Release and pull again; the default cooldown is 0.25 seconds. The left hand is reserved for teleporting. Shots reach up to 50 metres. Solid cover stops lasers. You cannot hit yourself."),
            new Instruction("SHOOTING AND COVER","Both head and body hits deal 10 damage; head hits have no extra damage bonus. A confirmed hit marker and sound mean damage was accepted. A beam alone does not guarantee a hit."),
            new Instruction("AMMUNITION AND HEALTH","Start with 100 ammunition and 100 health. Each accepted shot costs one ammo. After two seconds without an accepted shot, ammo regenerates at eight shots per second, up to 100. There is no manual reload. Low ammo is highlighted; an empty gun clicks and cannot shoot."),
            new Instruction("AMMUNITION AND HEALTH","Empty trigger pulls do not postpone regeneration. Watch your health and ammo HUD, and move behind cover when under fire."),
            new Instruction("LIVES AND RESPAWNING","Each player has two lives. At zero health you are tagged out and lose one life. After the first tag-out, wait three seconds to respawn at a clear, unoccupied spawn point with full health and ammo. If all spawns are blocked, respawning waits for space instead of placing you inside cover."),
            new Instruction("LIVES AND RESPAWNING","You have two seconds of spawn protection; landing a validated hit ends your protection early. Shooting and teleporting are blocked while waiting to respawn. Losing your final life eliminates you until the next round."),
            new Instruction("SCORING, WINNING AND RESTART","The last remaining player wins. The scoreboard shows finishing rank, kills, hits and deaths. Surviving longer ranks first; ties use more kills, then more hits, then fewer deaths. Tagging a player out counts as a kill, including a tag-out followed by a respawn."),
            new Instruction("SCORING, WINNING AND RESTART","At game over, press the RIGHT controller A button to request a restart. A new round resets health, ammo, lives and round statistics. At least two connected players are required to restart."),
            new Instruction("OPTIONS AND LEAVING","Use the left controller menu button to open session options. Use the LEFT stick to navigate and the RIGHT trigger to select. Pointing a controller ray and using its trigger also works. Settings control master volume, music/ambience, sound effects, mute and graphics."),
            new Instruction("OPTIONS AND LEAVING","The match keeps running while your options or help are open, but your firing and teleport inputs are blocked. Choose RESUME to return. RETURN TO MENU disconnects you. If the host leaves, the room closes for everyone; host or join a new room to play again."),
            new Instruction("OPTIONS AND LEAVING","New players cannot join a match after loading starts."),
        };
    }
}
