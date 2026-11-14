# Laptop testing only

These controls are deliberately separate from the game's VR-only How to Play.
Use `Assets/Scenes/MainMenu.unity` in the final worktree. Select Game view for input.
Host/join/ready with the mouse; two players are needed for an actual round.

- Right mouse button toggles simulator mouse control; mouse/arrow keys rotate.
- Q/E lower/raise the simulated head in FPS/HMD mode, within the current-floor
  bounds. They do not accumulate hidden translation at a limit. WASD head/body
  travel is blocked in the teleport-only arena.
- `[` selects left controller pose manipulation; `]` selects right. Controller
  adjustment retains the existing simulator translation/rotation controls. This
  changes a simulated tracked device, not the gun's authored controller offset.
- Controller input mode: `9` selects the primary stick. Hold **Shift + I** for the
  left stick's forward teleport aim. Release **I before Shift** to send left-stick
  neutral and request landing. Left pose selection alone does not route stick
  input to the left controller. I/J/K/L without Shift target the right stick.
- **T** fires the right gun in controller input mode. Shift+T must not fire a left
  gun. The left hand is reserved for teleporting.
- **Escape** opens/resumes local options; firing/teleporting are blocked while
  options/help/settings are open. The network match continues.
- **1** represents the right controller's primary/A button; it requests a new
  round after game over (two connected players required).
- **F8** is an existing Editor-only arc diagnostic: hold to aim, release to
  request teleport. It is not a headset control or a substitute for stick tests.

The simulator's X/Y menus show its actual current mode and bindings. HMD-only
manipulation is not a gameplay movement mode. Verify that changing simulator modes
never detaches the gun from its controller or the hand from its gun grip.

Regression checklist: move/aim the right controller with Scene view closed;
confirm grip alignment before rendering. Look down for torso/hands, never helmet.
Hold Q/E at both limits and reverse immediately. Teleport between clear floor
areas; reject cover, edges and occupied landings. Check feet reset instantly.
Compare both processes' health/ammo after shots, cover, respawn, final elimination,
restart, leave and rejoin. Temporary automatic pose/input fixtures are not shipped.

Headset tests remain separate: five simultaneous devices, mixed Quest 2/3,
physical controls, boundaries, haptics, sound, readability and measured performance.
