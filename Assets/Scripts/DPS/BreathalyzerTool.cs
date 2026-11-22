using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;
using TMPro;

namespace DPS
{
    /// <summary>
    /// Breathalyzer prop. When its nozzle is brought near the driver's window it beeps and shows
    /// the result: green "0.00 &#8240;" for a sober driver, red with 1.2-2.8 &#8240; for a drunk one.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class BreathalyzerTool : MonoBehaviour
    {
        [Header("Grab")]
        [SerializeField] XRGrabInteractable grabInteractable;

        [Header("World anchors")]
        [Tooltip("Nozzle tip that must reach the driver's breath zone.")]
        [SerializeField] Transform nozzle;

        [Tooltip("How close the nozzle must get to the driver zone.")]
        [SerializeField] float touchRadius = 0.45f;

        [Header("Display")]
        [SerializeField] TMP_Text readout;
        [SerializeField] Renderer ledRenderer;
        [SerializeField] Color idleColor = new Color(0.15f, 0.15f, 0.15f);
        [SerializeField] Color greenColor = new Color(0.1f, 0.9f, 0.25f);
        [SerializeField] Color redColor = new Color(1f, 0.12f, 0.1f);
        [SerializeField] string idleText = "---";

        [Header("Audio")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip greenClip;
        [SerializeField] AudioClip redClip;
        [SerializeField, Range(0f, 1f)] float beepVolume = 0.9f;

        [Header("Behaviour")]
        [Tooltip("Rejects a second reading of the same car for this long.")]
        [SerializeField] float cooldown = 1f;

        Rigidbody m_Body;
        float m_LastReadTime = float.NegativeInfinity;
        CarController m_LastCar;
        MaterialPropertyBlock m_Block;

        /// <summary>Last result, or null before the first reading.</summary>
        public BreathResult? LastResult { get; private set; }

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

            if (nozzle == null)
                nozzle = transform;

            m_Block = new MaterialPropertyBlock();
            GrabTuning.MakeHoldable(grabInteractable, m_Body);
            ResetDisplay();
        }

        void Update()
        {
            if (Time.time - m_LastReadTime < cooldown)
                return;

            var car = ShiftManager.Instance != null ? ShiftManager.Instance.ActiveCar : null;
            if (car == null || car.Data == null || !car.IsWindowOpen)
                return;

            var zone = car.BreathZone;
            if (zone == null)
                return;

            var nozzlePosition = nozzle != null ? nozzle.position : transform.position;
            if (Vector3.Distance(nozzlePosition, zone.position) > touchRadius)
                return;

            TakeReading(car);
        }

        /// <summary>Performs a reading immediately. Public so it can be driven by tests.</summary>
        public void TakeReading(CarController car)
        {
            if (car == null || car.Data == null)
                return;

            if (car == m_LastCar && Time.time - m_LastReadTime < cooldown)
                return;

            m_LastCar = car;
            m_LastReadTime = Time.time;

            var result = car.Data.breath;
            LastResult = result;

            if (readout != null)
                readout.text = result.Display;

            if (result.isDrunk)
            {
                SetLed(redColor);
                SimpleAudioSynth.Play(audioSource, redClip, SynthSound.BeepRed, beepVolume);
            }
            else
            {
                SetLed(greenColor);
                SimpleAudioSynth.Play(audioSource, greenClip, SynthSound.BeepGreen, beepVolume);
            }

            car.OnInspectedWithTool();
        }

        /// <summary>Restores the idle state, e.g. when a new car arrives.</summary>
        public void ResetDisplay()
        {
            LastResult = null;
            m_LastCar = null;

            if (readout != null)
                readout.text = idleText;

            SetLed(idleColor);
        }

        void SetLed(Color color)
        {
            if (ledRenderer == null)
                return;

            if (m_Block == null)
                m_Block = new MaterialPropertyBlock();

            ledRenderer.GetPropertyBlock(m_Block);
            m_Block.SetColor("_BaseColor", color);
            m_Block.SetColor("_Color", color);
            // Unlit material uses _BaseColor; the emissive key keeps Lit materials glowing too.
            m_Block.SetColor("_EmissionColor", color * 2f);
            ledRenderer.SetPropertyBlock(m_Block);
        }
    }
}
