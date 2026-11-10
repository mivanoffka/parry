using UnityEngine;

namespace Parry
{
    // Samples whole-limb poses; the sword follows the rigid arm without elbow IK.
    [DefaultExecutionOrder(100)]
    public sealed class CharacterPresentation : MonoBehaviour
    {
        SpriteRenderer body;
        PlayerMotor player;
        EnemyBrain enemy;
        CombatGuard guard;
        Vector3 previousPosition;
        float motion, impactAt = float.NegativeInfinity, deathAt, rollDirection = 1;
        readonly LocomotionCycle gait = new LocomotionCycle();
        bool parryImpact;
        bool wasRolling;
        bool wasAlive = true;
        string currentClip;
        CharacterSkeleton skeleton;
        WeaponVisual weapon;
        static Material material;

        public static void Install(GameObject owner, SpriteRenderer renderer, bool sentinel)
        {
            if (renderer == null) return;
            var texture = Resources.Load<Texture2D>("Combat/Characters/" + (sentinel ? "armored-sentinel" : "masked-duelist"));
            if (texture == null) return;
            var sprite = Sprite.Create(texture, new Rect(0, 0, texture.width, texture.height),
                Vector2.one * .5f, texture.height / 1.8f);
            sprite.name = sentinel ? "armored-sentinel" : "masked-duelist";
            renderer.sprite = sprite;
            renderer.color = Color.white;
            if (material == null)
            {
                material = new Material(Resources.Load<Shader>("Combat/SpriteNormalLit"));
                // Old normal maps describe the placeholder art; use a flat surface for the new drawings.
                material.SetFloat("_NormalStrength", 0);
                material.SetFloat("_Ambient", .55f);
                material.SetFloat("_Specular", .05f);
            }
            renderer.sharedMaterial = material;
            // Finish construction before enabling the update component.
            var rig = new CharacterSkeleton(owner.transform, sentinel, material);
            var animation = owner.GetComponent<CharacterPresentation>();
            if (animation == null) animation = owner.AddComponent<CharacterPresentation>();
            animation.body = renderer;
            animation.player = owner.GetComponent<PlayerMotor>();
            animation.enemy = owner.GetComponent<EnemyBrain>();
            animation.guard = owner.GetComponent<CombatGuard>();
            animation.previousPosition = owner.transform.position;
            animation.skeleton = rig;
            renderer.forceRenderingOff = true;
        }

        public void Impact(bool parry) { impactAt = Time.time; parryImpact = parry; }

        void LateUpdate()
        {
            Vector3 travel = Vector3.ProjectOnPlane(transform.position - previousPosition, Vector3.up);
            previousPosition = transform.position;
            if (body == null || guard == null || skeleton == null) return;
            bool alive = player != null ? player.IsAlive : enemy != null && enemy.Health.IsAlive;
            skeleton.Root.gameObject.SetActive((alive || player != null) && body.enabled);
            if (!alive)
            {
                if (wasAlive) deathAt = Time.time;
                wasAlive = false;
                motion = 0; wasRolling = false;
                if (player != null)
                {
                    skeleton.ResetRollShape();
                    skeleton.Animator.Play("Stagger");
                    skeleton.Animator["Stagger"].normalizedTime = .5f;
                    skeleton.Animator.Sample();
                    float fall = Mathf.SmoothStep(0, 1, (Time.time - deathAt) / .35f);
                    skeleton.Root.rotation = Camera.main.transform.rotation * Quaternion.Euler(0, 0, -80 * fall);
                    skeleton.Root.position = transform.position + Vector3.up * .15f;
                    skeleton.Tint(body.color);
                }
                return;
            }
            if (!wasAlive) { currentClip = null; wasAlive = true; }
            bool rolling = player != null ? player.Rolling : enemy.Rolling;
            if (rolling && !wasRolling)
            {
                float side = Vector3.Dot(travel, Camera.main.transform.right);
                rollDirection = Mathf.Abs(side) > .001f ? Mathf.Sign(side) : body.flipX ? -1 : 1;
            }
            wasRolling = rolling;
            if (weapon == null) weapon = GetComponentInChildren<WeaponVisual>();
            float speed = travel.magnitude / Mathf.Max(.001f, Time.deltaTime);
            float targetMotion = Mathf.Clamp01(speed / 3);
            motion = Mathf.MoveTowards(motion, targetMotion, Time.deltaTime * 10);
            AttackPhase phase = player != null ? player.ClashPhase : enemy.ClashPhase;
            if (!rolling && !guard.Disrupted && phase == AttackPhase.None
                && (player == null || !player.PreparingPowerfulAttack))
                gait.Advance(speed, Time.deltaTime, guard.Blocking);
            string clip;
            float progress;
            if (rolling) { clip = "Roll"; progress = player != null ? player.RollProgress : enemy.RollProgress; }
            else if (guard.Disrupted) { clip = "Stagger"; progress = Time.time / .6f; }
            else if (Time.time - impactAt < .24f)
            {
                clip = parryImpact ? "Parry" : "Block";
                progress = (Time.time - impactAt) / .24f;
            }
            else if (phase == AttackPhase.Windup || (player != null && player.PreparingPowerfulAttack))
            { clip = "Windup"; progress = weapon != null ? weapon.WindupProgress : 0; }
            else if (phase == AttackPhase.Active || phase == AttackPhase.Recovery)
            {
                clip = "Strike";
                progress = weapon != null ? (weapon.RecoveryProgress > 0
                    ? .5f + weapon.RecoveryProgress * .5f : Mathf.Max(0, weapon.SwingProgress) * .5f) : 0;
            }
            else if (guard.State.Relaxing && motion > .1f) { clip = "RelaxWalk"; progress = gait.Phase; }
            else if (guard.State.Relaxing) { clip = "Relax"; progress = Time.time / 2.4f; }
            else if (guard.Blocking && motion > .1f) { clip = "GuardWalk"; progress = gait.Phase; }
            else if (guard.Blocking) { clip = "Guard"; progress = Time.time; }
            else if (motion > .1f) { clip = "Walk"; progress = gait.Phase; }
            else { clip = "Idle"; progress = Time.time / 1.8f; }
            Animation animator = skeleton.Animator;
            if (clip != currentClip)
            {
                if (rolling || currentClip == "Roll") animator.Play(clip);
                else animator.CrossFade(clip, clip == "Parry" || clip == "Block" ? .035f : .09f);
                currentClip = clip;
            }
            animator[clip].normalizedTime = progress;
            skeleton.ResetRollShape();
            animator.Sample();
            Camera camera = Camera.main;
            skeleton.Root.position = transform.position + Vector3.up * .85f - camera.transform.up * .85f;
            skeleton.Root.rotation = camera.transform.rotation;
            skeleton.Root.localScale = new Vector3(body.flipX ? -1 : 1, 1, 1);
            if (rolling) skeleton.PoseRoll(progress, transform.position, camera.transform.rotation, rollDirection);
            skeleton.Tint(body.color);
            if (!rolling && !guard.Disrupted && weapon != null)
            {
                Quaternion sampledArm = skeleton.SwordShoulder.localRotation;
                skeleton.AimSwordHand(weapon.transform.position + skeleton.Root.up * .075f);
                // The relaxation clip owns the stretched arm; the old world-space grip
                // could pull it across the torso or reverse it when facing the camera.
                float restBlend = weapon.RelaxationBlend;
                skeleton.SwordShoulder.localRotation = Quaternion.Slerp(
                    skeleton.SwordShoulder.localRotation, sampledArm, restBlend);
                Vector3 bladeDirection = skeleton.Root.TransformVector(new Vector3(1, -.25f, 0)).normalized;
                float bladeAngle = Mathf.Atan2(-Vector3.Dot(bladeDirection, camera.transform.right),
                    Vector3.Dot(bladeDirection, camera.transform.up)) * Mathf.Rad2Deg;
                weapon.transform.rotation = Quaternion.Slerp(weapon.transform.rotation,
                    camera.transform.rotation * Quaternion.Euler(0, 0, bladeAngle), restBlend);
                weapon.transform.position = skeleton.SwordWrist.position - skeleton.Root.up * .075f;
                if (player != null)
                    weapon.ChargeGlint(weapon.WindupProgress, player.PreparingPowerfulAttack && !guard.Blocking);
                else if (enemy != null)
                    weapon.ChargeGlint(weapon.WindupProgress, enemy.PowerfulThreat && !enemy.UnblockableAttack);
            }
        }

        public Transform CreateCorpseSkeleton(Transform parent)
        {
            if (skeleton == null) return null;
            var snapshot = Instantiate(skeleton.Root.gameObject, parent, false);
            snapshot.name = "Fallen skeleton";
            var animation = snapshot.GetComponent<Animation>();
            if (animation != null) { animation.Stop(); Destroy(animation); }
            snapshot.SetActive(true);
            return snapshot.transform;
        }
    }
}
