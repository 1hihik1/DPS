using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DPS
{
    /// <summary>
    /// Grab-able stack of banknotes. Carries an amount and can only be banked once, so dropping it
    /// into the bribe box and then grabbing it again cannot double-pay.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class CashBundle : MonoBehaviour
    {
        [Header("Value")]
        [SerializeField] int amount = 5000;

        [Header("Grab")]
        [SerializeField] XRGrabInteractable grabInteractable;

        [Header("Audio")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip pickupClip;

        Rigidbody m_Body;
        bool m_Consumed;
        int m_DropFrame = -1;

        public int Amount => amount;

        /// <summary>False once banked, so the box and its scanner both ignore it.</summary>
        public bool CanBeCollected => !m_Consumed;

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

        // The bundle hangs kinematic in the driver's hand until taken. XRGrabInteractable restores
        // that kinematic flag in its late-phase Detach, after this event, so the switch to physics
        // is deferred to the next frame or it would be overwritten and the cash would freeze mid-air.
        void OnReleased(UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs args)
        {
            if (m_Body == null || m_Consumed)
                return;

            m_DropFrame = Time.frameCount;
        }

        void Update()
        {
            if (m_DropFrame < 0 || Time.frameCount <= m_DropFrame)
                return;

            m_DropFrame = -1;

            if (m_Consumed || IsHeld || m_Body == null)
                return;

            m_Body.isKinematic = false;
            m_Body.useGravity = true;
        }

        void OnGrabbed(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
        {
            SimpleAudioSynth.Play(audioSource, pickupClip, SynthSound.Cash, 0.65f);
        }

        /// <summary>Marks this bundle as banked and removes it from the world.</summary>
        public void Consume()
        {
            if (m_Consumed)
                return;

            m_Consumed = true;

            // Release every hand first: destroying an object mid-select leaves the interactor
            // holding a dead reference and blocks the next grab. The cast is required because the
            // deprecated XRBaseInteractable overload would otherwise be picked.
            if (grabInteractable != null && grabInteractable.isSelected &&
                grabInteractable.interactionManager != null)
            {
                grabInteractable.interactionManager.CancelInteractableSelection(
                    (UnityEngine.XR.Interaction.Toolkit.Interactables.IXRSelectInteractable)grabInteractable);
            }

            Destroy(gameObject);
        }

        /// <summary>Sets the amount, used by ShiftManager when spawning.</summary>
        public void Configure(int newAmount)
        {
            amount = newAmount;
        }
    }
}
