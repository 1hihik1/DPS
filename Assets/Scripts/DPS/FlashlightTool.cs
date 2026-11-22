using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DPS
{
    /// <summary>
    /// Grab-able torch with a shadowless child spotlight, used to look into the dark trunk.
    /// The light is only enabled while held so it costs nothing on the desk.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class FlashlightTool : MonoBehaviour
    {
        [Header("Grab")]
        [SerializeField] XRGrabInteractable grabInteractable;

        [Header("Light")]
        [Tooltip("Spotlight aimed out of the torch head. Must have Shadows = None.")]
        [SerializeField] Light spotLight;

        [Tooltip("Keep the light on even when not held (nobody holds it in the tests).")]
        [SerializeField] bool lightWhenIdle;

        [SerializeField] float spotIntensity = 8f;
        [SerializeField] float spotRange = 22f;
        [SerializeField] float spotAngle = 55f;
        [SerializeField] Color spotColor = new Color(1f, 0.95f, 0.85f);

        [Header("Audio")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip toggleClip;

        [Header("Battery flavour")]
        [Tooltip("Optional flicker when the tool is first grabbed.")]
        [SerializeField] bool flickerOnGrab = true;

        Rigidbody m_Body;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();

            if (grabInteractable == null)
                grabInteractable = GetComponent<XRGrabInteractable>();

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            GrabTuning.MakeHoldable(grabInteractable, m_Body);

            if (spotLight != null)
            {
                // Quest budgets: never let a torch cast real-time shadows.
                spotLight.shadows = LightShadows.None;
                spotLight.type = LightType.Spot;
                spotLight.intensity = spotIntensity;
                spotLight.range = spotRange;
                spotLight.spotAngle = spotAngle;
                spotLight.color = spotColor;
                spotLight.enabled = lightWhenIdle;
            }

            if (grabInteractable != null)
            {
                grabInteractable.selectEntered.AddListener(OnGrabbed);
                grabInteractable.selectExited.AddListener(OnReleased);
            }
        }

        void OnDestroy()
        {
            if (grabInteractable == null)
                return;

            grabInteractable.selectEntered.RemoveListener(OnGrabbed);
            grabInteractable.selectExited.RemoveListener(OnReleased);
        }

        void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
        {
            if (spotLight != null)
            {
                spotLight.enabled = true;
                spotLight.intensity = spotIntensity;
            }

            if (flickerOnGrab)
                StartCoroutine(Flicker());

            SimpleAudioSynth.Play(audioSource, toggleClip, SynthSound.Click, 0.5f);
        }

        void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
        {
            if (spotLight != null && !lightWhenIdle)
                spotLight.enabled = false;
        }

        System.Collections.IEnumerator Flicker()
        {
            if (spotLight == null)
                yield break;

            for (var i = 0; i < 2; i++)
            {
                spotLight.intensity = spotIntensity * 0.25f;
                yield return new WaitForSeconds(0.035f);
                spotLight.intensity = spotIntensity;
                yield return new WaitForSeconds(0.05f);
            }
        }
    }
}
