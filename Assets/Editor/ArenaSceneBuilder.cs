using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace Parry.Editor
{
    public static class ArenaSceneBuilder
    {
        const string ScenePath = "Assets/Scenes/SampleScene.unity";
        const string ArtPath = "Assets/Art/Generated";

        [MenuItem("Parry/Bake Arena Into Scene")]
        public static void Run()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before baking the arena.");
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            if (!scene.isLoaded) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            foreach (var root in scene.GetRootGameObjects())
            {
                var arena = root.GetComponent<ArenaDemo>();
                if (arena == null) continue;
                if (arena.transform.childCount == 0) Bake(arena);
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                arena = null;
                foreach (var savedRoot in scene.GetRootGameObjects())
                    if (savedRoot.TryGetComponent(out ArenaDemo savedArena)) arena = savedArena;
                if (arena == null) throw new InvalidOperationException("Saved arena is missing.");
                Validate(arena);
                return;
            }
            throw new InvalidOperationException("ArenaDemo is missing from SampleScene.");
        }

        public static void VerifyRuntime()
        {
            PrepareRuntimeCheck();
            double started = 0;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state == PlayModeStateChange.EnteredPlayMode)
                {
                    UnityEngine.Object.FindAnyObjectByType<EnemyBrain>().enabled = false;
                    started = EditorApplication.timeSinceStartup;
                }
            };
            EditorApplication.update += () =>
            {
                if (started == 0 || EditorApplication.timeSinceStartup - started < 1) return;
                started = 0;
                try
                {
                    var arena = UnityEngine.Object.FindAnyObjectByType<ArenaDemo>();
                    Validate(arena);
                    var player = arena.GetComponentInChildren<PlayerMotor>();
                    var enemy = arena.GetComponentInChildren<EnemyBrain>();
                    if (player.Guard == null || enemy.Guard == null || player.MaxHealth != 2 || enemy.Health.MaxHealth != 2)
                        throw new InvalidOperationException("Baked fighters did not initialize correctly.");
                    if (arena.GetComponentsInChildren<Volume>().Length != 1)
                        throw new InvalidOperationException("Runtime created a duplicate feedback volume.");
                    enemy.Guard.ResetCombat();
                    enemy.Guard.State.AddPressure(100, Time.time);
                    int previousHealth = enemy.Health.Health;
                    enemy.ReceiveAttack(1, Vector3.forward, player.Guard);
                    var blood = arena.GetComponentInChildren<BloodSplatter>();
                    int visibleBlood = 0;
                    foreach (var renderer in blood.GetComponentsInChildren<SpriteRenderer>())
                        if (renderer.enabled) visibleBlood++;
                    if (enemy.Health.Health != previousHealth - 1 || visibleBlood < BloodSplatter.ParticlesPerHit)
                        throw new InvalidOperationException("A hit against broken concentration did not emit blood.");
                    enemy.transform.position = new Vector3(3, 0, 4);
                    Vector3 deathPosition = enemy.transform.position;
                    enemy.ReceiveAttack(1, Vector3.forward, player.Guard);
                    if (enemy.Health.IsAlive || blood == null || !blood.gameObject.activeInHierarchy)
                        throw new InvalidOperationException("Blood effects did not survive the lethal hit.");
                    var corpses = arena.GetComponentsInChildren<EnemyCorpse>();
                    if (corpses.Length != 1 || corpses[0].transform.position != deathPosition
                        || corpses[0].GetComponentInChildren<Collider>() != null)
                        throw new InvalidOperationException("Enemy death did not leave a non-blocking body at the impact position.");
                    typeof(EnemyCorpse).GetField("createdAt", System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic).SetValue(corpses[0], Time.time - .35f);
                    typeof(EnemyCorpse).GetMethod("LateUpdate", System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic).Invoke(corpses[0], null);
                    Vector3 restingPosition = corpses[0].transform.position;
                    Vector3 corpseTravel = restingPosition - deathPosition;
                    if (corpseTravel.z <= 0 || corpseTravel.magnitude > .651f
                        || Mathf.Abs(corpseTravel.x) > .001f || Mathf.Abs(corpseTravel.y) > .001f)
                        throw new InvalidOperationException("The body did not slide a short distance along the lethal strike.");
                    enemy.Health.TakeHit(1, Vector3.forward);
                    if (arena.GetComponentsInChildren<EnemyCorpse>().Length != 1)
                        throw new InvalidOperationException("Repeated damage to a defeated enemy duplicated the body.");
                    if (arena.GetComponentsInChildren<BloodSplatter>().Length != 1)
                        throw new InvalidOperationException("Fighters did not share the blood effect pool.");
                    player.Guard.ResetCombat();
                    int previousPlayerHealth = player.Health;
                    player.ReceiveAttack(1, Vector3.back, enemy.Guard);
                    visibleBlood = 0;
                    foreach (var renderer in blood.GetComponentsInChildren<SpriteRenderer>())
                        if (renderer.enabled) visibleBlood++;
                    if (player.Health != previousPlayerHealth - 1 || player.Guard.Broken
                        || visibleBlood < BloodSplatter.ParticlesPerHit * 3)
                        throw new InvalidOperationException("An ordinary hit against the player did not emit blood without a concentration break.");
                    player.ReceiveAttack(1, Vector3.back, enemy.Guard);
                    if (player.Health != previousPlayerHealth - 1)
                        throw new InvalidOperationException("Blood handling bypassed player damage invulnerability.");
                    var surfaceTrace = typeof(BloodSplatter).GetMethod("TrySurfaceHit",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    object[] traceArguments = { player.transform.position + Vector3.up * 2, Vector3.down, 3f, default(RaycastHit) };
                    if (!(bool)surfaceTrace.Invoke(blood, traceArguments)
                        || ((RaycastHit)traceArguments[3]).collider.GetComponentInParent<PlayerMotor>() != null)
                        throw new InvalidOperationException("Blood traces hit the player's controller instead of the floor.");
                    Debug.Log("PARRY BLOOD IMPACT CHECK PASSED");
                    player.Guard.ResetCombat();
                    if (!player.TryRoll(Vector3.right) || !player.Rolling || Mathf.Abs(player.Guard.Pressure - 100f * 50 / player.Guard.EndurancePoints) > .01f)
                        throw new InvalidOperationException("Player roll did not spend concentration.");
                    if (player.TryRoll(Vector3.right) || Mathf.Abs(player.Guard.Pressure - 100f * 50 / player.Guard.EndurancePoints) > .01f)
                        throw new InvalidOperationException("Rejected player roll spent concentration again.");
                    var fill = player.transform.Find("Concentration pressure").GetComponent<LineRenderer>();
                    var background = player.transform.Find("Concentration background").GetComponent<LineRenderer>();
                    var drawGuard = typeof(CombatGuard).GetMethod("LateUpdate",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    foreach (float yaw in new[] { -30f, 0, 30f })
                    {
                        Camera.main.transform.rotation = Quaternion.Euler(39, yaw, 0);
                        drawGuard.Invoke(player.Guard, null);
                        if (!fill.enabled || fill.sortingOrder <= background.sortingOrder
                            || Vector3.Dot(fill.GetPosition(0) - background.GetPosition(0), Camera.main.transform.forward) >= -.01f
                            || Mathf.Abs(Vector3.Distance(fill.GetPosition(0), fill.GetPosition(1)) - .22f) > .001f)
                            throw new InvalidOperationException("Concentration fill is obscured or has an incorrect length.");
                    }
                    player.Guard.ResetCombat();
                    drawGuard.Invoke(player.Guard, null);
                    if (fill.enabled || !background.enabled)
                        throw new InvalidOperationException("Zero concentration load did not show a clean empty track.");
                    player.Guard.State.AddPressure(100, Time.time);
                    drawGuard.Invoke(player.Guard, null);
                    if (!fill.enabled || fill.startColor != Color.red
                        || Mathf.Abs(Vector3.Distance(fill.GetPosition(0), fill.GetPosition(1)) - 1.1f) > .001f)
                        throw new InvalidOperationException("Broken concentration did not show a full red bar.");
                    Debug.Log("PARRY PLAYER ROLL AND BAR CHECK PASSED");
                    typeof(PracticeTarget).GetField("respawnAt", System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic).SetValue(enemy.Health, Time.time);
                    typeof(PracticeTarget).GetMethod("Update", System.Reflection.BindingFlags.Instance
                        | System.Reflection.BindingFlags.NonPublic).Invoke(enemy.Health, null);
                    if (enemy.Health.IsAlive || corpses[0].transform.position != restingPosition
                        || !corpses[0].GetComponentInChildren<SpriteRenderer>().enabled)
                        throw new InvalidOperationException("A defeated enemy respawned automatically or lost its body.");
                    Debug.Log("PARRY ENEMY CORPSE CHECK PASSED");
                    Debug.Log("PARRY BAKED SCENE PLAY MODE CHECK PASSED");
                    EditorApplication.Exit(0);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.EnterPlaymode();
        }

        public static void VerifyCircularAttacks()
        {
            PrepareRuntimeCheck();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                try
                {
                    var arena = UnityEngine.Object.FindAnyObjectByType<ArenaDemo>();
                    var player = arena.GetComponentInChildren<PlayerMotor>();
                    var enemy = arena.GetComponentInChildren<EnemyBrain>();
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    player.enabled = false;
                    player.transform.position = Vector3.zero;
                    enemy.transform.position = Vector3.back * 1.5f;
                    Physics.SyncTransforms();
                    typeof(EnemyBrain).GetField("wasAlive", flags).SetValue(enemy, true);
                    typeof(EnemyBrain).GetField("observedHitTime", flags).SetValue(enemy, enemy.Health.LastHitTime);
                    typeof(EnemyBrain).GetField("facing", flags).SetValue(enemy, Vector3.back);
                    var stateField = typeof(EnemyBrain).GetField("state", flags);
                    int playerHealth = player.Health;
                    foreach (bool circular in new[] { false, true })
                    {
                        arena.CircularAttacks = circular;
                        stateField.SetValue(enemy, Enum.Parse(stateField.FieldType, "Windup"));
                        typeof(EnemyBrain).GetField("stateUntil", flags).SetValue(enemy, Time.time);
                        typeof(EnemyBrain).GetMethod("Update", flags).Invoke(enemy, null);
                        if (player.Health != playerHealth - (circular ? 1 : 0))
                            throw new InvalidOperationException("Enemy rear-hit geometry is incorrect.");
                        var warning = (LineRenderer)typeof(EnemyBrain).GetField("warning", flags).GetValue(enemy);
                        if (warning.loop != circular || warning.positionCount != (circular ? 48 : 23))
                            throw new InvalidOperationException("Enemy warning does not match attack mode.");
                    }
                    enemy.Guard.ResetCombat();
                    enemy.Guard.State.AddPressure(100, Time.time);
                    typeof(PlayerMotor).GetField("attackDirection", flags).SetValue(player, Vector3.forward);
                    var strike = typeof(PlayerMotor).GetMethod("Strike", flags);
                    int enemyHealth = enemy.Health.Health;
                    arena.CircularAttacks = false;
                    strike.Invoke(player, null);
                    if (enemy.Health.Health != enemyHealth)
                        throw new InvalidOperationException("Directional player attack hit behind the player.");
                    arena.CircularAttacks = true;
                    var wall = new GameObject("Circular attack test wall");
                    wall.transform.position = Vector3.back * .75f + Vector3.up;
                    wall.AddComponent<BoxCollider>().size = new Vector3(1, 2, .1f);
                    Physics.SyncTransforms();
                    strike.Invoke(player, null);
                    if (enemy.Health.Health != enemyHealth)
                        throw new InvalidOperationException("Circular attack passed through a wall.");
                    UnityEngine.Object.DestroyImmediate(wall);
                    Physics.SyncTransforms();
                    strike.Invoke(player, null);
                    strike.Invoke(player, null);
                    if (enemy.Health.Health != enemyHealth - 1)
                        throw new InvalidOperationException("Circular player attack missed or hit twice.");
                    foreach (bool circular in new[] { true, false })
                    {
                        arena.CircularAttacks = circular;
                        typeof(PlayerMotor).GetMethod("DrawSlash", flags).Invoke(player, new object[] { 0f, Vector3.forward });
                        var slash = (LineRenderer)typeof(PlayerMotor).GetField("slash", flags).GetValue(player);
                        if (slash.loop != circular || slash.positionCount != (circular ? 48 : 18))
                            throw new InvalidOperationException("Player indicator does not match attack mode.");
                    }
                    Debug.Log("PARRY CIRCULAR ATTACKS CHECK PASSED");
                    EditorApplication.Exit(0);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.EnterPlaymode();
        }

        public static void VerifyRelaxation()
        {
            // This feature check does not depend on the original demo's dummy/obstacle counts.
            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            UnityEngine.Object.FindAnyObjectByType<EnemyBrain>().enabled = false;
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                EditorApplication.CallbackFunction check = null;
                check = () =>
                {
                    EditorApplication.update -= check;
                    try
                    {
                        var arena = UnityEngine.Object.FindAnyObjectByType<ArenaDemo>();
                        var player = arena.GetComponentInChildren<PlayerMotor>();
                        var enemy = arena.GetComponentInChildren<EnemyBrain>();
                        player.enabled = enemy.enabled = false;
                        player.Respawn();
                        var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                        var update = typeof(PlayerMotor).GetMethod("Update", flags);
                        Application.runInBackground = true;
                        InputSystem.settings.backgroundBehavior = InputSettings.BackgroundBehavior.IgnoreFocus;
                        InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
                        InputSystem.settings.editorInputBehaviorInPlayMode = InputSettings.EditorInputBehaviorInPlayMode.AllDeviceInputAlwaysGoesToGameView;
                        var updateInput = typeof(InputSystem).GetMethod("Update", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic,
                            null, new[] { typeof(InputUpdateType) }, null);
                        var keyboard = InputSystem.AddDevice<Keyboard>();
                        InputSystem.EnableDevice(keyboard);
                        void Keys(params Key[] keys)
                        {
                            InputSystem.QueueStateEvent(keyboard, new KeyboardState(keys));
                            // An unfocused batch editor otherwise defaults to Editor input buffers.
                            updateInput.Invoke(null, new object[] { InputUpdateType.Dynamic });
                        }
                        Keys();
                        Keys(Key.LeftAlt); update.Invoke(player, null);
                        if (!player.Guard.State.Relaxing || player.Guard.Blocking)
                            throw new InvalidOperationException($"Alt did not enter unguarded relaxation (pressed={keyboard.leftAltKey.wasPressedThisFrame}, current={Keyboard.current == keyboard}, alive={player.IsAlive}, block={player.Guard.Blocking}, transition={player.Guard.State.GuardTransition}).");
                        Keys(Key.D);
                        for (int frame = 0; frame < 12; frame++) update.Invoke(player, null);
                        float speed = (float)typeof(PlayerMotor).GetField("speed", flags).GetValue(player);
                        if (((Vector3)typeof(PlayerMotor).GetField("velocity", flags).GetValue(player)).magnitude > speed * .31f)
                            throw new InvalidOperationException("Relaxation did not slow player movement.");
                        Keys(Key.J); update.Invoke(player, null);
                        Keys(); update.Invoke(player, null);
                        float windup = (float)typeof(PlayerMotor).GetField("windup", flags).GetValue(player);
                        float actualWindup = (float)typeof(PlayerMotor).GetProperty("CurrentWindup", flags).GetValue(player);
                        if (player.Guard.State.Relaxing || !player.Attacking || Mathf.Abs(actualWindup - windup * 2) > .001f)
                            throw new InvalidOperationException("An attack did not leave relaxation with double windup.");
                        player.InterruptAttack();
                        Keys(Key.RightAlt); update.Invoke(player, null);
                        if (!player.Guard.State.Relaxing)
                            throw new InvalidOperationException("Right Alt / Option did not enter relaxation.");
                        Keys(Key.K); update.Invoke(player, null);
                        if (player.Guard.State.Relaxing || player.Guard.Blocking || !player.Guard.State.GuardTransition)
                            throw new InvalidOperationException("Guard input bypassed the relaxation weapon-raise delay.");
                        Keys();
                        enemy.Guard.ResetCombat();
                        ((EnemyTactics)typeof(EnemyBrain).GetField("tactics", flags).GetValue(enemy)).Reset();
                        typeof(EnemyBrain).GetField("nextAttackAt", flags).SetValue(enemy, 0f);
                        enemy.transform.position = player.transform.position + Vector3.forward * 8;
                        typeof(EnemyBrain).GetMethod("BeginRetreat", flags).Invoke(enemy, null);
                        if (!enemy.Guard.State.Relaxing || enemy.Guard.Blocking)
                            throw new InvalidOperationException("Enemy retreat did not enter relaxation.");
                        enemy.transform.position = player.transform.position + Vector3.forward * 1.5f;
                        typeof(EnemyBrain).GetMethod("UpdateRestStance", flags).Invoke(enemy, new object[] { 1.5f });
                        if (enemy.Guard.State.Relaxing)
                            throw new InvalidOperationException("Enemy stayed relaxed next to its opponent.");
                        typeof(EnemyBrain).GetMethod("BeginWindup", flags).Invoke(enemy, new object[] { true, false });
                        float enemyWindup = (float)typeof(EnemyBrain).GetField("windup", flags).GetValue(enemy);
                        float actualEnemyWindup = (float)typeof(EnemyBrain).GetProperty("CurrentWindup", flags).GetValue(enemy);
                        if (enemy.Guard.State.Relaxing || Mathf.Abs(actualEnemyWindup - enemyWindup * 2) > .001f)
                            throw new InvalidOperationException("Enemy attack skipped its slower transition from relaxation.");
                        var weapon = player.GetComponentInChildren<WeaponVisual>();
                        typeof(WeaponVisual).GetField("relaxationBlend", flags).SetValue(weapon, 0f);
                        weapon.Pose(Vector3.forward, true, false, false);
                        Vector3 ready = weapon.transform.position;
                        typeof(WeaponVisual).GetField("relaxationBlend", flags).SetValue(weapon, 1f);
                        weapon.Pose(Vector3.forward, true, false, false, relaxing: true);
                        if (Vector3.Distance(ready, weapon.transform.position) < .3f)
                            throw new InvalidOperationException("Relaxation did not stretch the weapon arm to the side.");
                        Debug.Log("PARRY RELAXATION INPUT, MOVEMENT, ATTACK/GUARD TRANSITIONS, ENEMY REST AND WEAPON POSE CHECK PASSED");
                        EditorApplication.Exit(0);
                    }
                    catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
                };
                EditorApplication.update += check;
            };
            EditorApplication.EnterPlaymode();
        }

        public static void VerifyAttackClashes()
        {
            PrepareRuntimeCheck();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                try
                {
                    var arena = UnityEngine.Object.FindAnyObjectByType<ArenaDemo>();
                    var player = arena.GetComponentInChildren<PlayerMotor>();
                    var enemy = arena.GetComponentInChildren<EnemyBrain>();
                    player.enabled = enemy.enabled = false;
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var enemyState = typeof(EnemyBrain).GetField("state", flags);
                    float windup = (float)typeof(PlayerMotor).GetField("windup", flags).GetValue(player);
                    foreach (bool mutualActive in new[] { true, false })
                    foreach (bool receiveOnEnemy in new[] { true, false })
                    {
                        player.Respawn();
                        enemy.Health.ConfigureDurability(2, 0);
                        player.Guard.State.ConfigureEndurance(100);
                        enemy.Guard.State.ConfigureEndurance(100);
                        player.Guard.ResetCombat(); enemy.Guard.ResetCombat();
                        player.Push(Vector3.zero, 0); enemy.Health.Push(Vector3.zero, 0);
                        enemy.transform.position = Vector3.forward * 1.5f;
                        typeof(PlayerMotor).GetField("activeAttack", flags).SetValue(player, AttackChoice.Slash);
                        typeof(PlayerMotor).GetField("attackRemaining", flags).SetValue(player, .3f);
                        // Block cases always give the receiver a windup and the sender an active strike.
                        bool playerActive = mutualActive || receiveOnEnemy;
                        bool enemyActive = mutualActive || !receiveOnEnemy;
                        typeof(PlayerMotor).GetField("attackStartedAt", flags).SetValue(player,
                            Time.time - (playerActive ? windup + .02f : .05f));
                        typeof(PlayerMotor).GetField("attackDirection", flags).SetValue(player, Vector3.forward);
                        typeof(EnemyBrain).GetField("facing", flags).SetValue(enemy, Vector3.back);
                        enemyState.SetValue(enemy, Enum.Parse(enemyState.FieldType, "Windup"));
                        typeof(EnemyBrain).GetField("stateUntil", flags).SetValue(enemy, Time.time + (enemyActive ? -.01f : .1f));
                        Physics.SyncTransforms();
                        if (receiveOnEnemy) enemy.ReceiveAttack(1, Vector3.forward, player.Guard);
                        else player.ReceiveAttack(1, Vector3.back, enemy.Guard);
                        float cost = mutualActive ? 45 : 28;
                        if (player.Health != 2 || enemy.Health.Health != 2 || player.Attacking || enemy.AttackThreatening
                            || !player.Guard.Disrupted || !enemy.Guard.Disrupted
                            || Mathf.Abs(player.Guard.Pressure - cost) > .01f || Mathf.Abs(enemy.Guard.Pressure - cost) > .01f
                            || ((Vector3)typeof(PlayerMotor).GetField("knockback", flags).GetValue(player)).z >= 0
                            || ((Vector3)typeof(PracticeTarget).GetField("knockback", flags).GetValue(enemy.Health)).z <= 0)
                            throw new InvalidOperationException("Attack clash outcome depends on callback order or damages health.");
                        // A second callback from this same pair cannot apply damage or load again.
                        enemy.ReceiveAttack(1, Vector3.forward, player.Guard);
                        player.ReceiveAttack(1, Vector3.back, enemy.Guard);
                        if (player.Health != 2 || enemy.Health.Health != 2
                            || Mathf.Abs(player.Guard.Pressure - cost) > .01f || Mathf.Abs(enemy.Guard.Pressure - cost) > .01f)
                            throw new InvalidOperationException("The same clash resolved more than once.");
                    }
                    Debug.Log("PARRY ATTACK CLASH PHASES, CALLBACK ORDER AND SINGLE RESOLUTION CHECK PASSED");
                    EditorApplication.Exit(0);
                }
                catch (Exception error) { Debug.LogException(error); EditorApplication.Exit(1); }
            };
            EditorApplication.EnterPlaymode();
        }

        public static void VerifyLunge()
        {
            PrepareRuntimeCheck();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                try
                {
                    var arena = UnityEngine.Object.FindAnyObjectByType<ArenaDemo>();
                    var player = arena.GetComponentInChildren<PlayerMotor>();
                    var enemy = arena.GetComponentInChildren<EnemyBrain>();
                    player.enabled = enemy.enabled = false;
                    var flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
                    var stateField = typeof(EnemyBrain).GetField("state", flags);
                    var strike = typeof(PlayerMotor).GetMethod("Strike", flags);
                    var hits = (HashSet<PracticeTarget>)typeof(PlayerMotor).GetField("hit", flags).GetValue(player);
                    player.GetComponent<CharacterController>().enabled = false;
                    player.transform.position = Vector3.zero;
                    typeof(PlayerMotor).GetField("activeAttack", flags).SetValue(player, AttackChoice.Lunge);
                    typeof(PlayerMotor).GetField("attackRemaining", flags).SetValue(player, .6f);
                    typeof(PlayerMotor).GetField("attackDirection", flags).SetValue(player, Vector3.forward);
                    enemy.transform.position = Vector3.forward * 1.5f;
                    enemy.Health.ConfigureDurability(2, 0);
                    // Keep defense-cost assertions independent of the scene's editable endurance.
                    enemy.Guard.State.ConfigureEndurance(100);
                    player.Guard.State.ConfigureEndurance(100);
                    enemy.Guard.ResetCombat();
                    enemy.Guard.SetFacing(Vector3.back);
                    enemy.Guard.State.SetBlocking(true, Time.time);
                    Physics.SyncTransforms();
                    // A timed parry stops the lunge but heavily loads and pushes the defender.
                    if (enemy.Guard.Defend(player.Guard, Vector3.forward, true, true) != DefenseResult.Parry
                        || enemy.Health.Health != 2 || Mathf.Abs(enemy.Guard.Pressure - 80) > .01f
                        || !player.Guard.Disrupted
                        || ((Vector3)typeof(PlayerMotor).GetField("knockback", flags).GetValue(player)).z >= 0
                        || ((Vector3)typeof(PracticeTarget).GetField("knockback", flags).GetValue(enemy.Health)).z <= 0)
                        throw new InvalidOperationException("Powerful lunge parry did not apply defender cost and opposing knockback.");
                    enemy.Guard.ResetCombat();
                    player.Guard.ResetCombat();
                    enemy.Guard.State.SetBlocking(true, Time.time);
                    player.Push(Vector3.zero, 0);
                    enemy.Health.Push(Vector3.zero, 0);
                    if (enemy.Guard.Defend(player.Guard, Vector3.forward, false, true) != DefenseResult.Block
                        || enemy.Health.Health != 2 || !enemy.Guard.Broken || !player.Guard.Disrupted
                        || ((Vector3)typeof(PlayerMotor).GetField("knockback", flags).GetValue(player)).z >= 0
                        || ((Vector3)typeof(PracticeTarget).GetField("knockback", flags).GetValue(enemy.Health)).z <= 0)
                        throw new InvalidOperationException("Powerful lunge block did not protect health and push both fighters apart.");
                    player.Guard.ResetCombat();
                    enemy.Guard.ResetCombat();
                    enemy.Guard.State.Recoil(Time.time);
                    typeof(EnemyBrain).GetField("parryChance", flags).SetValue(enemy, 0f);
                    strike.Invoke(player, null);
                    strike.Invoke(player, null);
                    if (enemy.Health.Health != 1)
                        throw new InvalidOperationException("Lunge was blocked or hit more than once.");

                    enemy.Health.ConfigureDurability(2, 0);
                    hits.Clear();
                    float standingRadius = enemy.Health.Hitbox.Shape.radius;
                    float standingHeight = enemy.Health.Hitbox.Shape.height;
                    stateField.SetValue(enemy, Enum.Parse(stateField.FieldType, "Roll"));
                    enemy.transform.position = new Vector3(.55f, 0, 1.5f);
                    Physics.SyncTransforms();
                    strike.Invoke(player, null);
                    if (enemy.Health.Health != 2 || hits.Contains(enemy.Health)
                        || enemy.Health.Hitbox.Shape.radius >= standingRadius
                        || enemy.Health.Hitbox.Shape.height >= standingHeight)
                        throw new InvalidOperationException("Rolling did not shrink the enemy hitbox or avoid an off-center lunge.");
                    enemy.transform.position = Vector3.forward * 1.5f;
                    Physics.SyncTransforms();
                    strike.Invoke(player, null);
                    strike.Invoke(player, null);
                    if (enemy.Health.Health != 1 || enemy.Rolling)
                        throw new InvalidOperationException("An on-center lunge did not hit and interrupt the enemy roll exactly once.");
                    if (Mathf.Abs(enemy.Health.Hitbox.Shape.radius - standingRadius) > .001f)
                        throw new InvalidOperationException("Enemy hitbox did not restore after roll interruption.");

                    enemy.Health.ConfigureDurability(2, 0);
                    hits.Clear();
                    stateField.SetValue(enemy, Enum.Parse(stateField.FieldType, "Guard"));
                    enemy.transform.position = new Vector3(1.3f, 0, 1);
                    Physics.SyncTransforms();
                    strike.Invoke(player, null);
                    if (enemy.Health.Health != 2)
                        throw new InvalidOperationException("Lunge hit a target outside its narrow lane.");

                    enemy.transform.position = Vector3.forward * 1.5f;
                    var wall = new GameObject("Lunge test wall");
                    wall.transform.position = new Vector3(0, 1, .75f);
                    wall.AddComponent<BoxCollider>().size = new Vector3(1, 2, .1f);
                    hits.Clear();
                    Physics.SyncTransforms();
                    strike.Invoke(player, null);
                    if (enemy.Health.Health != 2)
                        throw new InvalidOperationException("Lunge passed through a solid wall.");
                    float playerStandingRadius = player.Hitbox.Shape.radius;
                    typeof(PlayerMotor).GetField("rollRemaining", flags).SetValue(player, .2f);
                    if (player.Hitbox.Shape.radius >= playerStandingRadius)
                        throw new InvalidOperationException("Player roll did not shrink the damage hitbox.");
                    player.Guard.ResetCombat();
                    typeof(PlayerMotor).GetField("invulnerableUntil", flags).SetValue(player, 0f);
                    int playerHealth = player.Health;
                    player.ReceiveAttack(1, Vector3.back, enemy.Guard);
                    if (player.Health != playerHealth - 1 || player.Rolling
                        || Mathf.Abs(player.Hitbox.Shape.radius - playerStandingRadius) > .001f)
                        throw new InvalidOperationException("A connecting attack could not damage and interrupt the player roll.");
                    Debug.Log("PARRY LUNGE DEFENSE, SHRINKING ROLL HITBOXES, LANE AND WALL CHECK PASSED");
                    EditorApplication.Exit(0);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.EnterPlaymode();
        }

        public static void VerifyAudio()
        {
            PrepareRuntimeCheck();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                try
                {
                    var arena = UnityEngine.Object.FindAnyObjectByType<ArenaDemo>();
                    var audio = CombatAudio.Initialize(arena.transform);
                    var player = arena.GetComponentInChildren<PlayerMotor>();
                    var enemy = arena.GetComponentInChildren<EnemyBrain>();
                    if (audio.LoadedClipCount != 8 || audio.GetComponentsInChildren<AudioSource>().Length != 16
                        || UnityEngine.Object.FindAnyObjectByType<AudioListener>() == null)
                        throw new InvalidOperationException("Combat audio clips, voices or listener are missing.");
                    foreach (CombatSound sound in Enum.GetValues(typeof(CombatSound)))
                    {
                        var clip = Resources.Load<AudioClip>("Audio/Combat/" + sound);
                        if (clip.samples < 1000 || clip.channels != 1 || clip.frequency != 44100)
                            throw new InvalidOperationException("Invalid combat clip: " + sound);
                    }
                    player.Guard.ResetCombat();
                    int before = audio.PlayedSounds;
                    if (!player.TryRoll(Vector3.right) || audio.PlayedSounds != before + 1)
                        throw new InvalidOperationException("Successful roll did not trigger audio.");
                    if (player.TryRoll(Vector3.right) || audio.PlayedSounds != before + 1)
                        throw new InvalidOperationException("Rejected roll triggered audio.");
                    player.Respawn();
                    player.Guard.SetFacing(Vector3.back);
                    player.Guard.State.SetBlocking(true, Time.time);
                    before = audio.PlayedSounds;
                    if (player.Guard.Defend(enemy.Guard, Vector3.forward) != DefenseResult.Parry
                        || audio.PlayedSounds != before + 1)
                        throw new InvalidOperationException("Parry did not trigger its distinct audio.");
                    before = audio.PlayedSounds;
                    if (player.Guard.Defend(enemy.Guard, Vector3.forward, false) != DefenseResult.Block
                        || audio.PlayedSounds != before + 1)
                        throw new InvalidOperationException("Block did not trigger its distinct audio.");
                    var protection = typeof(PlayerMotor).GetField("invulnerableUntil",
                        System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                    protection.SetValue(player, 0f);
                    before = audio.PlayedSounds;
                    if (!player.TakeHit(1, Vector3.forward) || audio.PlayedSounds != before + 1)
                        throw new InvalidOperationException("A damaging hit did not trigger audio.");
                    if (player.TakeHit(1, Vector3.forward) || audio.PlayedSounds != before + 1)
                        throw new InvalidOperationException("An invulnerable hit triggered damage audio.");
                    protection.SetValue(player, 0f);
                    before = audio.PlayedSounds;
                    if (!player.TakeHit(1, Vector3.forward) || player.IsAlive || audio.PlayedSounds != before + 1)
                        throw new InvalidOperationException("Lethal hit did not trigger death audio.");
                    int assignedVoices = 0;
                    foreach (var voice in audio.GetComponentsInChildren<AudioSource>())
                        if (voice.clip != null) assignedVoices++;
                    if (assignedVoices < 6)
                        throw new InvalidOperationException("Simultaneous combat cues overwrote each other's audio voices.");
                    Debug.Log("PARRY COMBAT AUDIO CHECK PASSED");
                    EditorApplication.Exit(0);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.EnterPlaymode();
        }

        public static void VerifyEndurance()
        {
            PrepareRuntimeCheck();
            var configuredEnemy = UnityEngine.Object.FindAnyObjectByType<EnemyBrain>();
            var settings = new SerializedObject(configuredEnemy);
            settings.FindProperty("endurance").intValue = 200;
            settings.ApplyModifiedPropertiesWithoutUndo();
            var playerSettings = new SerializedObject(UnityEngine.Object.FindAnyObjectByType<PlayerMotor>());
            playerSettings.FindProperty("endurance").intValue = 150;
            playerSettings.ApplyModifiedPropertiesWithoutUndo();
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                try
                {
                    var enemy = UnityEngine.Object.FindAnyObjectByType<EnemyBrain>();
                    var player = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
                    if (enemy.Guard.EndurancePoints != 200 || player.Guard.EndurancePoints != 150 || enemy.Health.MaxHealth != 2)
                        throw new InvalidOperationException("Player and enemy endurance settings did not initialize independently of each other and HP.");
                    enemy.Guard.State.AddPressure(28, Time.time);
                    if (!enemy.Guard.State.TrySpendForRoll(20, Time.time)
                        || Mathf.Abs(enemy.Guard.Pressure - 24) > .01f || Mathf.Abs(enemy.Guard.RemainingEndurance - 152) > .01f)
                        throw new InvalidOperationException("Configured endurance did not scale block and roll costs correctly.");
                    enemy.Guard.ResetCombat();
                    enemy.Guard.State.AddPressure(200, Time.time);
                    if (!enemy.Guard.Broken || enemy.Health.Health != 2)
                        throw new InvalidOperationException("Endurance capacity did not control concentration break independently of HP.");
                    enemy.Guard.ResetCombat();
                    if (enemy.Guard.EndurancePoints != 200 || enemy.Guard.RemainingEndurance != 200)
                        throw new InvalidOperationException("Combat reset lost configured endurance.");
                    if (!player.TryRoll(Vector3.right) || Mathf.Abs(player.Guard.RemainingEndurance - 100) > .01f
                        || Mathf.Abs(player.Guard.Pressure - 100f * 50 / 150) > .01f)
                        throw new InvalidOperationException("Player roll did not use the configured endurance capacity.");
                    player.Respawn();
                    if (player.Guard.EndurancePoints != 150 || player.Guard.RemainingEndurance != 150
                        || enemy.Guard.EndurancePoints != 200 || player.Health != 2)
                        throw new InvalidOperationException("Player respawn lost configured endurance or affected enemy capacity.");
                    Debug.Log("PARRY CONFIGURABLE FIGHTER ENDURANCE CHECK PASSED");
                    EditorApplication.Exit(0);
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.EnterPlaymode();
        }

        public static void VerifyApproachRoll()
        {
            PrepareRuntimeCheck();
            EnemyBrain enemy = null;
            PlayerMotor player = null;
            double started = 0;
            float combatStarted = 0;
            int stage = 0;
            bool observedRoll = false;
            Vector3 corner = new Vector3(11.2f, 0, 9.2f);
            var lastSwing = typeof(EnemyBrain).GetField("lastSwingAt",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                enemy = UnityEngine.Object.FindAnyObjectByType<EnemyBrain>();
                player = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
                enemy.transform.position = new Vector3(0, 0, 4.8f);
                enemy.Guard.ResetCombat();
                typeof(PlayerMotor).GetField("invulnerableUntil", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic).SetValue(player, Time.time + 10);
                Physics.SyncTransforms();
                combatStarted = Time.time;
                started = EditorApplication.timeSinceStartup;
                enemy.enabled = true;
            };
            EditorApplication.update += () =>
            {
                if (started == 0) return;
                try
                {
                    if (stage == 0)
                    {
                        if (enemy.Rolling)
                        {
                            observedRoll = true;
                            if (enemy.TacticalStatus != "CLOSING" || Mathf.Abs(enemy.Guard.Pressure - 20) > .01f)
                                throw new InvalidOperationException($"Approach roll intent/cost: {enemy.TacticalStatus}, load {enemy.Guard.Pressure}.");
                        }
                        else if (observedRoll)
                        {
                            float distance = Vector3.Distance(enemy.transform.position, player.transform.position);
                            if (distance < 1.2f || distance > 2.2f)
                                throw new InvalidOperationException("Approach roll did not stop safely in melee range.");
                            stage = 1;
                        }
                    }
                    else if (stage == 1 && (float)lastSwing.GetValue(enemy) >= combatStarted)
                    {
                        var controller = player.GetComponent<CharacterController>();
                        controller.enabled = false;
                        player.transform.position = new Vector3(9.9f, 0, 7.9f);
                        controller.enabled = true;
                        enemy.transform.position = corner;
                        Physics.SyncTransforms();
                        stage = 2;
                    }
                    else if (stage == 2 && enemy.Rolling)
                    {
                        if (Time.time - combatStarted < 2.95f || enemy.TacticalStatus != "ESCAPING"
                            || Mathf.Abs(enemy.Guard.Pressure - 40) > .01f)
                            throw new InvalidOperationException("Approach and escape rolls did not share their cooldown and cost.");
                        stage = 3;
                    }
                    else if (stage == 3 && !enemy.Rolling)
                    {
                        if (Vector3.Distance(enemy.transform.position, corner) < 3
                            || Mathf.Abs(enemy.transform.position.x) > 11.4f || Mathf.Abs(enemy.transform.position.z) > 9.4f)
                            throw new InvalidOperationException("Escape roll stopped working after an approach roll.");
                        Debug.Log("PARRY APPROACH AND ESCAPE ROLL CHECK PASSED");
                        started = 0;
                        EditorApplication.Exit(0);
                    }
                    if (started != 0 && EditorApplication.timeSinceStartup - started > 8)
                        throw new InvalidOperationException("Enemy failed to approach, attack and escape using tactical rolls.");
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    started = 0;
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.EnterPlaymode();
        }

        public static void VerifyAggression()
        {
            PrepareRuntimeCheck();
            EnemyBrain enemy = null;
            double started = 0;
            float combatStarted = 0;
            var lastSwing = typeof(EnemyBrain).GetField("lastSwingAt",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                enemy = UnityEngine.Object.FindAnyObjectByType<EnemyBrain>();
                var player = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
                var controller = player.GetComponent<CharacterController>();
                controller.enabled = false;
                player.transform.position = new Vector3(9.9f, 0, 7.9f);
                controller.enabled = true;
                enemy.transform.position = new Vector3(11.2f, 0, 9.2f);
                enemy.Guard.ResetCombat();
                enemy.Guard.State.AddPressure(80, Time.time);
                typeof(PlayerMotor).GetField("attackRemaining", System.Reflection.BindingFlags.Instance
                    | System.Reflection.BindingFlags.NonPublic).SetValue(player, .19f);
                if (!player.Attacking || player.AttackThreatening)
                    throw new InvalidOperationException("Aggression check did not start during player attack recovery.");
                Physics.SyncTransforms();
                combatStarted = Time.time;
                started = EditorApplication.timeSinceStartup;
                enemy.enabled = true;
            };
            EditorApplication.update += () =>
            {
                if (started == 0) return;
                if ((float)lastSwing.GetValue(enemy) >= combatStarted)
                {
                    bool attackedWithoutRecovering = enemy.Guard.Pressure >= 75 && enemy.TacticalStatus != "RECOVERING";
                    if (attackedWithoutRecovering) Debug.Log("PARRY STRAINED CORNER COUNTERATTACK CHECK PASSED");
                    else Debug.LogError("Enemy waited for concentration recovery instead of counterattacking.");
                    started = 0;
                    EditorApplication.Exit(attackedWithoutRecovering ? 0 : 1);
                }
                else if (EditorApplication.timeSinceStartup - started > 3)
                {
                    Debug.LogError("Strained enemy kept retreating at the wall instead of attacking.");
                    started = 0;
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.EnterPlaymode();
        }

        public static void VerifyCornerRoll()
        {
            PrepareRuntimeCheck();
            EnemyBrain enemy = null;
            double started = 0, finished = 0;
            bool observedRoll = false;
            Vector3 corner = new Vector3(11.2f, 0, 9.2f);
            EditorApplication.playModeStateChanged += state =>
            {
                if (state != PlayModeStateChange.EnteredPlayMode) return;
                enemy = UnityEngine.Object.FindAnyObjectByType<EnemyBrain>();
                var player = UnityEngine.Object.FindAnyObjectByType<PlayerMotor>();
                var controller = player.GetComponent<CharacterController>();
                controller.enabled = false;
                player.transform.position = new Vector3(9.9f, 0, 7.9f);
                controller.enabled = true;
                enemy.transform.position = corner;
                enemy.Guard.ResetCombat();
                Physics.SyncTransforms();
                started = EditorApplication.timeSinceStartup;
                enemy.enabled = true;
            };
            EditorApplication.update += () =>
            {
                if (started == 0) return;
                try
                {
                    if (enemy.Rolling)
                    {
                        observedRoll = true;
                        int health = enemy.Health.Health;
                        enemy.ReceiveAttack(1, Vector3.forward, null);
                        if (enemy.Health.Health != health || enemy.Guard.Blocking)
                            throw new InvalidOperationException("Escape roll did not dodge the hit with guard lowered.");
                    }
                    else if (observedRoll && finished == 0)
                    {
                        if (Vector3.Distance(enemy.transform.position, corner) < 3 || enemy.Guard.Pressure < 20)
                            throw new InvalidOperationException("Corner escape did not travel far enough or charge concentration.");
                        var body = enemy.GetComponent<CapsuleCollider>();
                        foreach (var wall in enemy.transform.parent.GetComponentsInChildren<BoxCollider>())
                            if (Physics.ComputePenetration(body, enemy.transform.position, enemy.transform.rotation,
                                wall, wall.transform.position, wall.transform.rotation, out _, out float penetration)
                                && penetration > .001f)
                                throw new InvalidOperationException("Escape roll crossed a solid obstacle.");
                        enemy.transform.position = corner;
                        Physics.SyncTransforms();
                        finished = EditorApplication.timeSinceStartup;
                    }
                    else if (finished != 0 && EditorApplication.timeSinceStartup - finished > .15)
                    {
                        if (enemy.Rolling) throw new InvalidOperationException("Escape roll ignored its cooldown.");
                        Debug.Log("PARRY CORNER ESCAPE CHECK PASSED");
                        EditorApplication.Exit(0);
                        started = 0;
                    }
                    if (EditorApplication.timeSinceStartup - started > 3 && started != 0)
                        throw new InvalidOperationException("Enemy failed to escape the corner.");
                }
                catch (Exception error)
                {
                    Debug.LogException(error);
                    started = 0;
                    EditorApplication.Exit(1);
                }
            };
            EditorApplication.EnterPlaymode();
        }

        static void PrepareRuntimeCheck()
        {
            Run();
            UnityEngine.Object.FindAnyObjectByType<EnemyBrain>().enabled = false;
        }

        static void Bake(ArenaDemo arena)
        {
            Scene previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(arena.gameObject.scene);
            try
            {
                if (!AssetDatabase.IsValidFolder("Assets/Art")) AssetDatabase.CreateFolder("Assets", "Art");
                if (!AssetDatabase.IsValidFolder(ArtPath)) AssetDatabase.CreateFolder("Assets/Art", "Generated");
                arena.BuildScene();
                var sprites = new HashSet<Sprite>();
                var materials = new HashSet<Material>();
                foreach (var renderer in arena.GetComponentsInChildren<Renderer>(true))
                {
                    if (renderer is SpriteRenderer spriteRenderer && spriteRenderer.sprite != null)
                        sprites.Add(spriteRenderer.sprite);
                    foreach (var material in renderer.sharedMaterials)
                        if (material != null) materials.Add(material);
                }
                int index = 0;
                foreach (var sprite in sprites)
                {
                    if (EditorUtility.IsPersistent(sprite)) continue;
                    string path = AssetDatabase.GenerateUniqueAssetPath(ArtPath + "/Sprite" + index++ + ".asset");
                    AssetDatabase.CreateAsset(sprite.texture, path);
                    AssetDatabase.AddObjectToAsset(sprite, path);
                }
                index = 0;
                foreach (var material in materials)
                {
                    if (!EditorUtility.IsPersistent(material))
                        AssetDatabase.CreateAsset(material, AssetDatabase.GenerateUniqueAssetPath(ArtPath + "/Material" + index++ + ".mat"));
                }
                var volume = arena.GetComponentInChildren<Volume>();
                var profile = volume.sharedProfile;
                AssetDatabase.CreateAsset(profile, AssetDatabase.GenerateUniqueAssetPath(ArtPath + "/PlayerFeedback.asset"));
                foreach (var component in profile.components) AssetDatabase.AddObjectToAsset(component, profile);
                EditorUtility.SetDirty(arena);
                EditorUtility.SetDirty(arena.GetComponentInChildren<EnemyBrain>());
                AssetDatabase.SaveAssets();
                EditorSceneManager.MarkSceneDirty(arena.gameObject.scene);
                EditorSceneManager.SaveScene(arena.gameObject.scene);
                Validate(arena);
                Debug.Log("PARRY ARENA BAKED: objects and graphics are saved in SampleScene.");
            }
            finally
            {
                if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
            }
        }

        static void Validate(ArenaDemo arena)
        {
            bool hasRooms = arena.GetComponent<RoomEncounter>() != null;
            if (arena.GetComponentsInChildren<PlayerMotor>().Length != 1
                || arena.GetComponentsInChildren<EnemyBrain>().Length != (hasRooms ? 3 : 4)
                || arena.GetComponentsInChildren<PracticeTarget>().Length != (hasRooms ? 3 : 9)
                || arena.GetComponentsInChildren<BoxCollider>().Length != (hasRooms ? 16 : 7))
                throw new InvalidOperationException("Unexpected baked arena object counts.");
            foreach (var renderer in arena.GetComponentsInChildren<SpriteRenderer>())
                if (renderer.GetComponent<WeaponVisual>() == null
                    && renderer.GetComponentInParent<BloodSplatter>() == null
                    && renderer.GetComponentInParent<EnemyCorpse>() == null
                    && (renderer.sprite == null || !EditorUtility.IsPersistent(renderer.sprite)))
                    throw new InvalidOperationException("Arena sprite was not saved as an asset.");
            var references = new SerializedObject(arena);
            foreach (string field in new[] { "player", "enemyFighter", "view", "feedbackVolume" })
                if (references.FindProperty(field).objectReferenceValue == null)
                    throw new InvalidOperationException("Missing arena reference: " + field);
            Debug.Log("PARRY ARENA VALIDATION PASSED");
        }
    }
}
