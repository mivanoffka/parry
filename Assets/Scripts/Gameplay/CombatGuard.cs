using UnityEngine;

namespace Parry
{
    public enum DefenseResult { Hit, Block, Parry }

    public sealed class CombatGuard : MonoBehaviour
    {
        public PostureState State { get; } = new PostureState();
        public bool Broken => State.Broken(Time.time);
        public bool Disrupted => State.Disrupted(Time.time);
        public bool Blocking => State.Blocking;
        public float Pressure => State.Pressure;
        public int EndurancePoints => State.EndurancePoints;
        public float RemainingEndurance => State.RemainingEndurance;
        public string Feedback => Time.time < feedbackUntil ? feedback : Broken ? "GUARD BROKEN" : "";
        public Vector3 Facing { get; private set; } = Vector3.back;
        float feedbackUntil;
        string feedback;
        LineRenderer bar, background, flash;
        ParrySparks sparks;
        bool wasBroken;
        public void SetFacing(Vector3 facing) => Facing = facing;
        public void Tick(bool wantsBlock, Vector3 facing)
        {
            Facing = facing;
            State.SetBlocking(wantsBlock, Time.time);
            State.Tick(Time.deltaTime, Time.time);
        }
        public DefenseResult Defend(CombatGuard attacker, Vector3 incomingDirection, bool allowParry = true)
        {
            if (!Blocking || Disrupted || Vector3.Angle(Facing, -incomingDirection) > 70) return DefenseResult.Hit;
            if (allowParry && State.CanParry(Time.time))
            {
                State.Deflect(Time.time);
                if (attacker != null)
                {
                    attacker.State.AddPressure(45, Time.time);
                    attacker.State.Recoil(Time.time);
                    attacker.Signal(attacker.Broken ? "GUARD BROKEN" : "DEFLECTED", false);
                }
                Signal("PARRY!", true);
                CombatAudio.Play(CombatSound.Parry, transform.position);
                sparks.Burst(transform.position + Facing * .65f + Vector3.up * .85f, incomingDirection);
                return DefenseResult.Parry;
            }
            State.AddPressure(28, Time.time);
            Signal(Broken ? "GUARD BROKEN" : "BLOCK", false);
            CombatAudio.Play(CombatSound.Block, transform.position);
            sparks.Burst(transform.position + Facing * .65f + Vector3.up * .85f, incomingDirection, false);
            return DefenseResult.Block;
        }
        public void OnHit() { State.AddPressure(22, Time.time); }
        public void ResetCombat() { State.Reset(); feedbackUntil = 0; wasBroken = false; }
        void Awake()
        {
            sparks = gameObject.AddComponent<ParrySparks>();
            background = DemoArt.Line("Concentration background", transform, new Color(.2f, .19f, .22f), .11f);
            bar = DemoArt.Line("Concentration pressure", transform, new Color(1, .65f, .15f), .07f);
            background.sortingOrder = 100;
            bar.sortingOrder = 101;
            background.positionCount = bar.positionCount = 2;
            flash = DemoArt.Line("Weapon clash", transform, Color.yellow, .055f);
            flash.enabled = false;
        }
        void Signal(string text, bool parry)
        {
            feedback = text; feedbackUntil = Time.time + .65f;
            flash.enabled = true; flash.positionCount = 12;
            Vector3 center = transform.position + Facing * .6f + Vector3.up * .85f;
            Vector3 right = Camera.main.transform.right, up = Camera.main.transform.up;
            for (int i = 0; i < 6; i++)
            {
                float angle = i * Mathf.PI / 3;
                Vector3 ray = right * Mathf.Cos(angle) + up * Mathf.Sin(angle);
                flash.SetPosition(i * 2, center + ray * .06f);
                flash.SetPosition(i * 2 + 1, center + ray * (parry ? .65f : .3f));
            }
            flash.startColor = flash.endColor = parry ? new Color(1, .75f, .15f) : new Color(.45f, .07f, .015f);
        }
        void LateUpdate()
        {
            if (Broken && !wasBroken) CombatAudio.Play(CombatSound.Break, transform.position);
            wasBroken = Broken;
            var player = GetComponent<PlayerMotor>();
            var target = GetComponent<PracticeTarget>();
            bool alive = player != null ? player.IsAlive : target != null && target.IsAlive;
            background.enabled = alive;
            bar.enabled = alive && Pressure > .001f;
            if (alive)
            {
                Camera view = Camera.main;
                Vector3 right = view.transform.right * .55f;
                Vector3 center = transform.position + Vector3.up * 2.05f;
                background.SetPosition(0, center - right); background.SetPosition(1, center + right);
                Vector3 fillStart = center - right - view.transform.forward * .02f;
                bar.SetPosition(0, fillStart);
                bar.SetPosition(1, fillStart + right * 2 * Mathf.Clamp01(Pressure / PostureState.Maximum));
                bar.startColor = bar.endColor = Broken ? Color.red : Blocking ? new Color(1, .85f, .3f) : new Color(1, .55f, .2f);
            }
            if (!alive || Time.time > feedbackUntil - .48f) flash.enabled = false;
        }
    }
}
