using Godot;
using MyGame.Core;

namespace MyGame.Combat
{
    public enum AudioFeedbackCue
    {
        LightHit,
        HeavyHit,
        Dodge,
        Parry,
        Invulnerable,
        Respawn,
        BossPhase,
        BossSlam,
    }

    /// <summary>
    /// One row of the cue table. Was a private [Serializable] struct inside AudioFeedback; Godot only
    /// exports arrays of Resources, so it became a small Resource of its own and had to move out to the
    /// namespace - nested Godot classes are not supported by the C# source generator.
    /// </summary>
    public partial class AudioFeedbackCueClip : Resource
    {
        [Export] public AudioFeedbackCue Cue { get; set; }
        [Export] public AudioStream Clip { get; set; }
        [Export(PropertyHint.Range, "0,1")] public float Volume { get; set; }
    }

    public partial class AudioFeedback : Node
    {
        [Export] private AudioStreamPlayer audioSource;
        [Export] private AudioFeedbackCueClip[] clips;
        [Export(PropertyHint.Range, "0,1")] private float fallbackVolume = 0.75f;

        public override void _Ready()
        {
            audioSource ??= this.GetComponent<AudioStreamPlayer>();
        }

        public void Play(AudioFeedbackCue cue)
        {
            AudioStream clip = null;
            float volume = fallbackVolume;

            if (clips != null)
            {
                for (int i = 0; i < clips.Length; i++)
                {
                    if (clips[i] == null || clips[i].Cue != cue)
                        continue;

                    clip = clips[i].Clip;
                    volume = clips[i].Volume <= 0f ? fallbackVolume : clips[i].Volume;
                    break;
                }
            }

            if (clip == null)
                return;

            if (audioSource == null)
            {
                audioSource = this.GetComponent<AudioStreamPlayer>();
                if (audioSource == null)
                {
                    // Unity's gameObject.AddComponent<AudioSource>() - the player is created on demand.
                    audioSource = new AudioStreamPlayer { Name = "AudioSource" };
                    AddChild(audioSource);
                }
            }

            // Godot has no PlayOneShot: one player plays one stream, so a cue landing while another is
            // still ringing cuts it off instead of layering. Give the actor a second AudioStreamPlayer
            // and a second AudioFeedback if overlapping cues ever matter.
            audioSource.Stream = clip;
            audioSource.VolumeDb = Mathf.LinearToDb(Mathf.Max(0.0001f, volume));
            audioSource.Play();
        }
    }
}
