using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

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
                    if (!player.TryRoll(Vector3.right) || !player.Rolling || Mathf.Abs(player.Guard.Pressure - 20) > .01f)
                        throw new InvalidOperationException("Player roll did not spend concentration.");
                    if (player.TryRoll(Vector3.right) || Mathf.Abs(player.Guard.Pressure - 20) > .01f)
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
                    if (!enemy.Health.IsAlive || corpses[0].transform.position != deathPosition
                        || !corpses[0].GetComponentInChildren<SpriteRenderer>().enabled)
                        throw new InvalidOperationException("The body did not remain after enemy respawn.");
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
                    if (!player.TryRoll(Vector3.right) || Mathf.Abs(player.Guard.RemainingEndurance - 130) > .01f
                        || Mathf.Abs(player.Guard.Pressure - 100f * 20 / 150) > .01f)
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
            if (arena.GetComponentsInChildren<PlayerMotor>().Length != 1
                || arena.GetComponentsInChildren<EnemyBrain>().Length != 1
                || arena.GetComponentsInChildren<PracticeTarget>().Length != 6
                || arena.GetComponentsInChildren<BoxCollider>().Length != 7)
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
