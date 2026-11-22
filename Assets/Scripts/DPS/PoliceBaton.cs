using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DPS
{
    /// <summary>
    /// Striped traffic baton. Signalling a car has two paths, both required by design:
    /// <list type="bullet">
    /// <item>wave it while held inside the road wave zone, or</item>
    /// <item>simply hold it inside the zone for <see cref="holdToSignalTime"/> seconds.</item>
    /// </list>
    /// When a car is approaching, a whistle plays and the car is told to brake at the stop line.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class PoliceBaton : MonoBehaviour
    {
        [Header("Grab")]
        [SerializeField] XRGrabInteractable grabInteractable;

        [Header("Wave zone")]
        [Tooltip("Trigger volume covering the carriageway in front of the stop line.")]
        [SerializeField] Collider waveZone;

        [Tooltip("How far the tip may stray outside the zone and still count as inside.")]
        [SerializeField] float zonePadding = 0.25f;

        [Header("Signal")]
        [Tooltip("Seconds the baton must be held in the zone before a car is waved down.")]
        [SerializeField] float holdToSignalTime = 0.3f;

        [Tooltip("Movement speed (m/s) of the baton tip that also counts as a wave.")]
        [SerializeField] float waveSpeed = 0.9f;

        [SerializeField] float signalCooldown = 1.5f;

        [Header("Audio")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip whistleClip;
        [SerializeField, Range(0f, 1f)] float whistleVolume = 1f;

        Transform m_Tip;
        Vector3 m_LastTipPosition;
        float m_ZoneDwellTime;
        float m_LastSignalTime = float.NegativeInfinity;
        Rigidbody m_Body;

        /// <summary>Raised when the player successfully signals. ShiftManager wires this to the active car.</summary>
        public event System.Action Signalled;

        public bool IsHeld => grabInteractable != null && grabInteractable.isSelected;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();

            if (grabInteractable == null)
                grabInteractable = GetComponent<XRGrabInteractable>();

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;

            GrabTuning.MakeHoldable(grabInteractable, m_Body);

            m_Tip = transform.childCount > 0 ? transform.GetChild(0) : transform;
            m_LastTipPosition = m_Tip.position;
        }

        void Update()
        {
            var tip = m_Tip != null ? m_Tip.position : transform.position;
            var speed = (tip - m_LastTipPosition).magnitude / Mathf.Max(Time.deltaTime, 0.0001f);
            m_LastTipPosition = tip;

            if (waveZone == null)
                return;

            var inside = IsInsideZone(tip);

            if (!inside)
            {
                m_ZoneDwellTime = 0f;
                return;
            }

            // Fallback path: holding it in the zone is enough, so testing needs no wild waving.
            m_ZoneDwellTime += Time.deltaTime;

            var isWaving = IsHeld && speed >= waveSpeed;
            if (m_ZoneDwellTime < holdToSignalTime && !isWaving)
                return;

            TrySignal();
        }

        bool IsInsideZone(Vector3 point)
        {
            if (waveZone == null)
                return false;

            // Bounds-based test works for any collider shape and needs no rigidbody on the zone.
            var bounds = waveZone.bounds;
            return bounds.SqrDistance(point) <= zonePadding * zonePadding;
        }

        void TrySignal()
        {
            if (Time.time - m_LastSignalTime < signalCooldown)
                return;

            m_LastSignalTime = Time.time;
            m_ZoneDwellTime = 0f;

            SimpleAudioSynth.Play(audioSource, whistleClip, SynthSound.Whistle, whistleVolume);
            Signalled?.Invoke();
        }
    }
}
