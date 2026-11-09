using UnityEngine;

namespace Parry
{
    public enum CombatSound { Swing, Hit, Block, Parry, Roll, Break, Death, Revive }

    public sealed class CombatAudio : MonoBehaviour
    {
        [SerializeField, Range(0, 1)] float volume = .7f;
        const int VoiceCount = 16;
        readonly AudioClip[] clips = new AudioClip[8];
        readonly AudioSource[] voices = new AudioSource[VoiceCount];
        readonly float[] nextAllowed = new float[8];
        static readonly float[] Levels = { .32f, .58f, .25f, .78f, .3f, .62f, .52f, .4f };
        static CombatAudio instance;
        int nextVoice;
        public int PlayedSounds { get; private set; }
        public int LoadedClipCount { get; private set; }

        public static CombatAudio Initialize(Transform arena = null)
        {
            if (instance == null)
                instance = new GameObject("Combat audio").AddComponent<CombatAudio>();
            if (arena != null && instance.transform.parent == null) instance.transform.SetParent(arena, false);
            return instance;
        }

        void Awake()
        {
            instance = this;
            for (int index = 0; index < clips.Length; index++)
            {
                clips[index] = Resources.Load<AudioClip>("Audio/Combat/" + (CombatSound)index);
                if (clips[index] != null) LoadedClipCount++;
                else Debug.LogError("Missing combat audio: " + (CombatSound)index, this);
            }
            for (int index = 0; index < voices.Length; index++)
            {
                var voice = new GameObject("Combat voice " + index);
                voice.transform.SetParent(transform, false);
                var source = voice.AddComponent<AudioSource>();
                source.playOnAwake = false;
                source.loop = false;
                source.spatialBlend = .25f;
                source.rolloffMode = AudioRolloffMode.Linear;
                source.minDistance = 10;
                source.maxDistance = 45;
                source.dopplerLevel = 0;
                voices[index] = source;
            }
        }

        public static void Play(CombatSound sound, Vector3 position)
        {
            var audio = Initialize();
            int index = (int)sound;
            if (audio.clips[index] == null || Time.unscaledTime < audio.nextAllowed[index]) return;
            audio.nextAllowed[index] = Time.unscaledTime + .035f;
            AudioSource voice = audio.voices[audio.nextVoice++ % VoiceCount];
            voice.Stop();
            voice.transform.position = position;
            voice.clip = audio.clips[index];
            voice.volume = audio.volume * Levels[index];
            voice.pitch = sound == CombatSound.Block ? Random.Range(.82f, .9f)
                : sound == CombatSound.Parry ? Random.Range(1.02f, 1.08f) : Random.Range(.94f, 1.06f);
            voice.Play();
            audio.PlayedSounds++;
        }

        void OnDestroy()
        {
            if (instance == this) instance = null;
        }
    }
}
