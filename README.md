# Parry — 2.5D movement and melee prototype

Open `Assets/Scenes/SampleScene.unity` in Unity 6000.6.3f1 and press Play.
For an AI-versus-AI spectator level, open `Assets/Scenes/AIDuel.unity` and press Play. The crimson and azure enemies fight each other using the same combat AI, automatically respawn after defeat, and share a camera that keeps both in view. Their positions, stats and opponent references are saved in the scene and editable in the Inspector. This scene is also included in Build Settings.
The `Parry Gameplay Demo` scene object contains the saved arena, player, five training dummies, active melee enemy, fill lights and feedback volume. They are visible and editable before Play. Generated character/shadow sprites, materials and the feedback profile are saved in `Assets/Art/Generated`. Play initializes combat, weapons, indicators and spark emitters without recreating the arena.

Edit positions, obstacle sizes, lighting and fighter settings directly in the scene Inspector and save the scene. `Parry > Bake Arena Into Scene` is a one-time migration for an empty arena root; on an already populated scene it validates the saved content without rebuilding it. The editor utility also supports batch validation via `Parry.Editor.ArenaSceneBuilder.Run` and a Play Mode initialization check via `Parry.Editor.ArenaSceneBuilder.VerifyRuntime` (run without `-quit`).

The migrated scene passes Unity batch checks for saved asset references, one player, one enemy, five dummies, seven solid arena objects and Play Mode initialization without duplicate arena/feedback objects. This was checked in a temporary project copy because the main project was already open. Headless Unity also logged an unrelated internal SearchDatabase indexing exception; the scene and gameplay initialization checks passed. Visual appearance was not checked by the headless run.

## Special attack: lunge

Tap and release LMB / J / gamepad X for a normal slash. Hold the attack button for 0.28 seconds to open the special-attack menu, move the mouse upward at least 36 pixels (or tilt the gamepad right stick upward), then release attack to execute a lunge. The upper menu sector highlights the selection. Return to the center or press Escape to cancel. Aiming is locked when the button is pressed, so moving through the menu does not redirect the strike. Rolling, blocking, taking damage, losing focus or disabling the player cancels selection.

The lunge has a 0.32-second windup, a 0.16-second thrust with 1.4 units of collision-limited forward travel, and 0.5 seconds of recovery. The sword glows orange-red as its unblockable warning and thrusts straight instead of sweeping. Its narrow lane uses 85% of the ordinary weapon reach, remains directional even with circular slashes enabled, and hits each target once. Blocks and parries cannot stop it; enemy rolls retain invulnerability, and solid walls obstruct both movement and hits. Enemies attempt a sideways dodge when their roll state, cooldown and endurance permit. Tune these values under PlayerMotor → Special attack: lunge. Batch integration check: Parry.Editor.ArenaSceneBuilder.VerifyLunge (without -quit).

## Controls

| Action | Keyboard / mouse | Gamepad |
| --- | --- | --- |
| Move | WASD / arrow keys | Left stick |
| Aim | Mouse on the ground plane | Right stick |
| Attack | Left mouse button / J | West button (X / Square) |
| Block / timed parry | Hold / tap RMB or K | Hold / tap left shoulder (LB / L1) |
| Roll | Space | South button (A / Cross) |
| Revive / heal and return to center | R | — |

Player movement speed is 8 units/second (previously 5.5), acceleration is 48 and roll speed is 15. Enemy movement speed is 4.2 (previously 3.1).

Movement uses a 3D CharacterController on XZ, normalized diagonal input, acceleration and solid arena boundaries. Camera-relative input follows an orthographic camera at 39 degrees. Character and target graphics are original procedural placeholder sprites, facing the camera with depth-based sorting and ground shadows.

Player rolls cost 20 concentration load, like the enemy's escape rolls. A roll cannot start while attacking, disrupted, on cooldown, or if its cost would bring load to 100. Rejected attempts do not spend load. The cost restarts the two-second recovery delay. Tune the Player component's `rollPressure` in the scene Inspector.

Melee has windup, an active window and recovery. Attack direction is shown by the fighter’s body pose and sword in hand. The blade follows the projected ground-plane heading in every direction, winds back and sweeps across the locked attack heading while the body leans into the strike. Range indicators are hidden by default; enable Show Attack Range Indicators on ArenaDemo or DuelArena in the Inspector to show them. Movement slows during attacks; facing locks for each swing. Standard swords use a 70-degree sector (35 degrees to either side) for both the player and enemies. Targets within that sector receive one hit per swing, flash, lose health and are knocked back. Solid obstacles block hits. Dummies respawn after 2.5 seconds. A short input buffer accepts the next attack near the end of recovery. Rolls use a locked direction, duration and cooldown and collide with walls.

## Active enemy

Both the player's `PlayerMotor` and each enemy's `EnemyBrain` have an independent **Endurance** setting, with a default baseline of 100 points. A value of 200 doubles the baseline reserve and 50 halves it. Set these values in the scene Inspector before Play; changing player endurance does not rescale enemy settings. Blocks, incoming parries, hits, emergency defense and rolls consume actual points (28/45/22/45/20); concentration meters, screen effects, fatigue thresholds and AI decisions use the percentage of each fighter's own capacity. Passive recovery restores 18 points/second or 28 under guard before fatigue modifiers, so a larger reserve also takes longer to refill. Player revive and enemy respawn preserve their configured capacities. The HUD displays remaining/max STA plus percentage load. Health remains separate at two HP. Batch check: `Parry.Editor.ArenaSceneBuilder.VerifyEndurance` (without `-quit`) tests player 150 and enemy 200 independently, including player roll cost and revive.

Before Play Mode, use **ArenaDemo → Combat settings → Base Attack Radius** in the scene Inspector to set the shared attack radius (0.5–8 world units, default 2.5). It affects both fighters in directional and circular modes, including their indicators and the enemy's engagement distance (2.1 units at the default radius). Save the scene to preserve the setting.

Before entering Play Mode, select the scene object with the `ArenaDemo` component and enable **Combat settings → Circular Attacks** in the Inspector to replace both fighters' attack sectors with full circles. The option is saved with the scene, is off by default and has no in-game toggle. Indicators switch to rings; range, timing, one hit per swing and obstacle blocking remain unchanged. Blocks, parries and knockback use the direction from the attacker to the victim. Batch regression check: `Parry.Editor.ArenaSceneBuilder.VerifyCircularAttacks` (without `-quit`).

Enemy movement and target knockback sweep the full body capsule against solid objects, stop with a small clearance at contact and resolve existing overlaps. This prevents damage knockback from pushing the enemy through walls or scenery, including on long frames. Trigger hitboxes remain available for melee detection.

Defeated enemies leave a darkened, fallen body and ground shadow at the death position. The remains belong to the arena, have no collider, AI, weapon or health bars, and stay after the enemy respawns. Bodies remain until the scene is unloaded. Training dummies keep their existing disappearance/respawn behavior.

The red enemy steers around nearby solid obstacles and changes tactics based on concentration load:

- At high load it retreats at 9 units/second while facing its opponent and holding guard. The baseline threshold is 70%, lowered by accumulated fatigue and rapidly rising pressure. It also checks actual endurance: at 60% or more, insufficient reserve for a parried attack favors retreat. An imminent block that could exhaust concentration triggers urgent retreat, overriding the brief reengagement commitment.
- Recovery ends at 35% load (down to 29% with fatigue), after at least 0.8 seconds. There is no forced return before concentration recovers. The enemy seeks a gap of at least 5.2 units or two units beyond the opponent’s reach, holding guard while resting. Mere proximity does not cancel recovery: an early counterattack requires a disrupted opponent and sufficient reserve. At critical risk, the enemy cancels an unfinished attack windup to retreat. Reengagement has a short 0.6-second commitment, which urgent danger can override.
- At 55% load or lower (less when fatigued), it becomes aggressive: faster pursuit, proactive attacks and two-strike combinations. With moderate strain it fights cautiously. Approach rolls require enough reserve for both the roll and a subsequent parry penalty. A safe opening against a disrupted opponent can justify continuing combat instead of taking a recovery break.
- At moderate load it fights cautiously: 4.2 units/second chase speed, one strike at a time, 0.65-second guard phases and 0.8-second recovery. Unguarded hits still deal damage, and solid obstacles block attacks.

After each strike, the enemy waits at least **one second before starting another windup**, including combo follow-ups and attacks against a broken guard. Emergency defense cannot bypass this cooldown. For each incoming hit it makes one random choice: **50% ordinary block, 50% parry attempt**. The attempt still requires a valid parry window and respects the shared cooldown; otherwise it becomes a block. The choice is not rerolled every frame, and the player's parry is unchanged. Tune `attackInterval`, `recovery` and `parryChance` in `EnemyBrain.cs`.

The enemy HUD reports PRESSING, CAUTIOUS, RECOVERING or STAGGERED. The player can chase the retreating enemy to prevent recovery or use that time to restore their own concentration.

Batch aggression regression check: `Parry.Editor.ArenaSceneBuilder.VerifyAggression` (without `-quit`) verifies that an enemy at 80 load near an arena corner counterattacks without waiting to recover.

Only the player's windup and active strike count as an immediate attack threat. The enemy can initiate counterattacks during player recovery rather than holding guard for the entire attack animation; this also lets it detect fresh strikes during buffered attack chains. Defense against actual incoming hits remains available through emergency guard.

When the player traps the enemy near two differently oriented walls, it can escape with a roll. It probes nearby walls, checks 16 possible full-body capsule paths and favors a clear destination away from the player and surrounding obstacles. The roll travels at 14 units/second for 0.28 seconds, costs 20 concentration load and has a three-second cooldown. It cannot start during concentration break, recoil, windup or unfinished recovery, or if its cost would break concentration. During the roll, guard and attacks are disabled and melee hits are dodged; the visible sprite tumbles and the HUD shows ESCAPING. Movement still respects solid obstacles. Tune the Enemy component's Corner escape fields. Batch behavior check: `Parry.Editor.ArenaSceneBuilder.VerifyCornerRoll` (without `-quit`).

The enemy also rolls toward the player from 3.5–7 units away when it is not tactically retreating. It checks direct and angled capsule paths and stops about 1.7 units from the player without overshooting or crossing their collider. Shorter approaches cover less ground over the same 0.28-second animation. Approach and escape rolls share the 20-load cost and three-second cooldown; the HUD shows CLOSING during an approach. After closing, the enemy commits to fighting and can attack once the existing attack cooldown allows. Tune `approachRollRange` under the Enemy's Tactical rolls settings. Batch check: `Parry.Editor.ArenaSceneBuilder.VerifyApproachRoll` (without `-quit`) covers approach, follow-up attack, shared cooldown and subsequent corner escape.

During windup, the enemy commits until an incoming strike actually connects; simply starting a player attack no longer cancels its swing. With a 0.20-second enemy windup and a 0.09-second player windup, a reactive counterattack must start roughly within the first 0.11 seconds to force emergency defense before the enemy strikes. A parry raises guard immediately and retains its 0.28-second window, making it the more forgiving response. Emergency defense still costs 45 load when contact interrupts the enemy.

Both fighters have **2 health points** and deal **1 damage** per unguarded strike: two landed hits kill, including during a guard break. Dummies keep 3 health. The enemy has reduced hit knockback of 3 (dummies keep 5) and respawns with full health after 2.5 seconds. Player hits stun it for 0.23 seconds and interrupt its attack. The player is invulnerable while rolling and has 0.8 seconds of protection after taking damage. At zero health, movement and attacks stop and the enemy stops attacking. Press R to revive/heal at the center with one second of protection.

The enemy defends every incoming attack while its concentration permits. It attempts a fresh timed parry, subject to the shared anti-spam cooldown; otherwise it blocks. A nearby player attack can interrupt its windup or attack recovery with an emergency defense costing **45 load**, cancelling the swing and remaining combo. An abrupt defensive turn beyond the guard cone has the same cost. The charge is applied before defense: if it breaks concentration, the incoming hit can land. A successful parry does not refund the emergency defense cost. Ordinary raised-guard defense does not pay this extra charge. The enemy turns toward incoming strikes and prioritizes guard during the player's windup and active strike, but can start a counterattack during player recovery. Only concentration break or parry recoil prevents defense outside a roll. Defense is also refreshed when a hit arrives, avoiding an unguarded frame caused by script update order. Tune `emergencyGuardPressure` in `EnemyBrain.cs`.

## Weapons, blocking and concentration

Both fighters carry a visible 2D sword with idle, guard, windup, swing and stagger poses. Hold RMB / K / LB to block; a fresh press opens a 0.28-second parry window. Guard protects a 140-degree frontal cone, so attacks from behind still hurt. Blocking slows the player to 45% movement speed. Release guard to attack; rolling lowers guard. While attacking or staggered, a fighter cannot block.

Orange concentration bars display **load**: 0 is stable, 100 breaks defense. Rules are shared by the player and enemy:

Overhead bar fills render explicitly above their gray tracks and slightly closer to the camera, preventing transparent sorting from hiding the fill. At zero load the fill is disabled rather than drawing a degenerate line; at a break the bar is fully red. The HUD's numeric load remains available.

- Block: no HP loss, defender gains 28 load.
- Parry: no HP loss, defender's load stays unchanged; attacker gains 45 and recoils for 0.35 seconds. A press deflects one strike. Repeated presses within 0.3 seconds cannot reopen the window. Parrying restarts the defender's recovery delay.
- Unguarded hit: HP damage and 22 load.
- At 100 load: guard drops and attacks/movement are interrupted for 1.25 seconds. Unblocked hits still deal 1 damage. Breaking a block protects HP on that particular strike. The enemy follows up a guard break promptly instead of waiting through its guard phase.
- Load recovers at 18/second without guard or 28/second with guard only after **more than two seconds without combat pressure**. Starting an attack, blocking, parrying, taking a hit, being parried or using emergency defense restarts the delay. Releasing/re-raising guard cannot bypass it. After a break, load explicitly resets to 35; this partial reset is separate from passive recovery.

Recovery also remembers sustained overload. Above **60/100 load**, both fighters accumulate one second of fatigue per second, including recovery delays and guard-break stun, capped at eight seconds. Recovery speed is multiplied by `1 / (1 + fatigue * 0.5)`: four seconds of overload means one-third speed; eight means one-fifth. At or below 60, fatigue dissipates at half a second per second only after the same two-second combat pause and while not disrupted. Crossing the threshold or continuously parrying cannot erase fatigue in combat. Holding guard still recovers faster after a pause. Revive/respawn clears fatigue. Tune `FatigueThreshold` and `MaximumFatigue` in `PostureState.cs`.

The enemy uses the same parry window and direction checks, so its newly raised guard can deflect a player attack. Force expensive emergency defenses during its windup/recovery, parry its attacks and punish concentration breaks or recoil. Combat feedback shows BLOCK, PARRY, DEFLECTED and GUARD BROKEN, with weapon-clash sparks. Death/respawn hide and restore weapons; R resets the player's concentration and protection state.

The player's missing health progressively desaturates the scene toward black and white, without extra darkening or a red filter. Concentration load adds pulsing lens distortion, chromatic channel separation resembling 3D glasses, and camera shake, with the strongest effects at guard break. Both effects ease toward the current combat state; healing/reviving with R clears them. Camera shake is applied separately from the smooth follow position, so it does not accumulate drift. The HUD stays sharp and readable. These effects use a runtime URP volume and preserve the arena's existing bloom.

## Combat audio

Eight original synthesized WAV effects cover committed swings, damaging hits, ordinary blocks, timed parries, rolls, concentration breaks, death and player revive. Both fighters use the same event hooks; blocked/invulnerable hits do not play damage sounds, rejected rolls are silent, and player swing audio plays once per active swing rather than every hit-scan frame. Concentration break audio plays once per transition. A shared arena-level Combat audio object pools 16 partially positional AudioSources, varies pitch slightly, limits same-cue retriggers and exposes a Volume slider during Play. The saved Main Camera already has an AudioListener. Clips are retained in builds through `Assets/Resources/Audio/Combat`; replace those WAV files to customize the sounds. Regenerate with `python3 Tools/generate_combat_audio.py`. Batch integration check: `Parry.Editor.ArenaSceneBuilder.VerifyAudio` (without `-quit`).

## Blood, parry sparks and sprite lighting

Every damaging direct hit against the player emits 64 dark red blood droplets, regardless of concentration. Enemy blood still requires broken concentration. Lethal hits emit blood too; blocks, parries, dodges and invulnerable hits do not. Droplets follow the attack direction, fall under gravity and leave small stains when they hit solid floors or walls. Blood traces ignore the player's controller so it cannot swallow droplets or prevent floor stains. A larger stain appears beneath the impact. Both fighters share an arena-level pool, so death/respawn does not erase blood and pools are not duplicated. The effect is capped at 192 airborne droplets and 384 stains; stains last 25 seconds and fade over the last five. These transient effects are initialized at runtime by `BloodSplatter.cs`.

Successful parries by either fighter emit 42 bright golden metallic streaks, a 1.5-unit soft impact flash and a 0.18-second golden point-light pulse (peak intensity 18). Ordinary blocks emit 14 dim orange-red sparks, a smaller 0.7-unit flash and a 0.1-second orange-red light pulse (peak intensity 4.5). Both illuminate nearby lit level surfaces. Each fighter reuses its emitters; particles travel in world space, fall under gravity and fade. Block audio is a short damped low impact at reduced volume; parry audio has a sharp, bright metallic ring with a longer tail. Regenerate only these cues with `python Tools/generate_combat_audio.py Block Parry`.

Player, enemy, training dummies and swords now use `Parry/Sprite Normal Lit`, a transparent URP shader for 3D directional and additional lights. It handles sprite tint, flip, rotation, depth sorting, directional-light shadows and metallic sword highlights. The arena has warm/cool point lights, and parry flashes illuminate the normal-mapped surfaces.

The maps in `Assets/Resources/Combat/*_normal.png` match the existing code-drawn sprite silhouettes: rounded heads, cloak folds, target relief and sword bevels. These are **linear uncompressed RGB vector textures**, decoded explicitly by the shader; keep their texture type Default rather than switching to Unity's packed Normal Map format. `CombatNormalMapImporter` enforces the required linear, point-filtered, clamp, no-mipmap import settings. Regenerate with `python3 Tools/generate_normal_maps.py`. If replacing the sprite art, supply matching normal maps too. Shader and map resources are retained in builds via Resources.

## Tuning

- Player and Enemy scene objects: serialized movement, roll, melee, defense and tactical settings. Tune in the Inspector outside Play Mode and save the scene; changing script defaults does not overwrite saved component values.
- `ArenaDemo.cs`: saved scene references, camera follow, screen feedback and control overlay. `BuildScene` is used only by the editor migration.
- `Assets/Editor/ArenaSceneBuilder.cs`: one-time scene migration, generated art persistence and scene/runtime initialization checks.
- `PracticeTarget.cs`: shared dummy/enemy health, knockback and respawn.
- `EnemyBrain.cs`: enemy durability, pursuit, diagonal retreat, obstacle steering, attack combos, telegraph, damage, recovery, hit stun and emergency defense cost.
- `EnemyTactics.cs`: concentration thresholds and stable retreat/reengagement decisions.
- `CombatGuard.cs` / `PostureState.cs`: shared defense, pressure, recovery, parry rules and concentration bars.
- `WeaponVisual.cs`: code-drawn sword sprite and weapon poses.
- `ParrySparks.cs`: spark count, velocity, lifetime, HDR intensity and light flash.
- `Assets/Resources/Combat/SpriteNormalLit.shader`: normal strength, ambient fill and specular response.
- `DemoArt.cs`: placeholder sprite generation and rendering helpers. Replace generated sprites with authored Sprite assets for production art.

This is a mechanics prototype inspired by the requested perspective and action style. It includes a basic melee enemy and player damage; final animation/art assets are still placeholders.

## Validation

The ten gameplay scripts compile without errors using Roslyn against this project's Unity and Input System assemblies. The engine-independent concentration rules and enemy tactical decisions pass 146 assertions covering parry timing/consumption, spam suppression, attacker recoil, block breaks, recovery, update ordering, revive reset, retreat thresholds, recovery hysteresis and reengagement. Run them with `dotnet run --project Tests/CombatRules/CombatRules.csproj` (.NET 10 SDK). The existing Unity editor's Metal compiler logs confirm successful compilation of the sprite-lighting shader (including clustered lights/cascaded shadows) and additive spark shader. All three normal maps pass direction normalization and linear/uncompressed import checks. Runtime and editor C# checks compile without errors.

`Parry > Validate Rendering` checks the imported maps and synchronously compiles shader variants in the open editor. Visual appearance and spark timing have not been independently verified in Play Mode. Stop Play and reopen `Assets/Scenes/SampleScene.unity` after receiving the migrated scene if the editor still shows its old contents. The arena and its materials are now saved assets; weapons, combat indicators and transient spark effects still initialize at runtime.
