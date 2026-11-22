using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DPS
{
    /// <summary>
    /// Trunk handle. Detects a hand brought near the handle (the rig has no hand colliders, so this
    /// measures distance instead of using OnTriggerEnter) and smoothly rotates the lid open.
    /// No HingeJoint - a joint on a moving car would fight its parenting and jitter.
    /// </summary>
    public class TrunkInteractable : MonoBehaviour
    {
        [Header("Lid")]
        [Tooltip("Pivot that the lid rotates around.")]
        [SerializeField] Transform lidPivot;

        [Tooltip("Open rotation applied to the pivot, in local euler angles.")]
        [SerializeField] Vector3 openEuler = new Vector3(-70f, 0f, 0f);

        [SerializeField] float openDuration = 0.6f;

        [Header("Activation")]
        [Tooltip("How close a hand must come to the handle.")]
        [SerializeField] float touchRadius = 0.28f;

        [Tooltip("Ignore hand proximity unless the trunk actually contains contraband or cargo.")]
        [SerializeField] bool requirePlayerNear = true;

        [Header("Audio")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip openClip;

        Quaternion m_ClosedRotation;
        Coroutine m_Anim;
        bool m_IsOpen;

        /// <summary>True once the lid has finished opening.</summary>
        public bool IsOpen => m_IsOpen;

        /// <summary>Raised the first time the trunk is opened.</summary>
        public event System.Action Opened;

        void Awake()
        {
            if (lidPivot == null)
                lidPivot = transform;

            m_ClosedRotation = lidPivot.localRotation;

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;
        }

        void Update()
        {
            if (m_IsOpen || m_Anim != null)
                return;

            if (!requirePlayerNear || !HandProximity.IsHandNear(transform.position, touchRadius))
                return;

            Open();
        }

        /// <summary>Opens the lid. Public so a test or a UI button can trigger it.</summary>
        public void Open()
        {
            if (m_IsOpen)
                return;

            m_IsOpen = true;

            SimpleAudioSynth.Play(audioSource, openClip, SynthSound.Click, 0.8f, 0.7f);
            Opened?.Invoke();

            if (m_Anim != null)
                StopCoroutine(m_Anim);

            m_Anim = StartCoroutine(OpenRoutine());
        }

        IEnumerator OpenRoutine()
        {
            var target = m_ClosedRotation * Quaternion.Euler(openEuler);
            var start = lidPivot.localRotation;
            var t = 0f;

            while (t < openDuration)
            {
                t += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / openDuration));
                lidPivot.localRotation = Quaternion.Slerp(start, target, k);
                yield return null;
            }

            lidPivot.localRotation = target;
            m_Anim = null;
        }

        /// <summary>Snaps the trunk shut for a reused car.</summary>
        public void Close()
        {
            if (m_Anim != null)
            {
                StopCoroutine(m_Anim);
                m_Anim = null;
            }

            m_IsOpen = false;
            if (lidPivot != null)
                lidPivot.localRotation = m_ClosedRotation;
        }
    }
}
