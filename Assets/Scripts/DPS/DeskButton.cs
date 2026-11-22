using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DPS
{
    /// <summary>
    /// Desk button. Works with either input path required by design:
    /// a VR hand/poke physically touching the trigger collider, or an <see cref="XRSimpleInteractable"/>
    /// ray select. Both funnel into one cooldown-guarded <see cref="Press"/>.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class DeskButton : MonoBehaviour
    {
        public enum ButtonKind { Pass, Detain }

        [Header("Identity")]
        [SerializeField] ButtonKind kind = ButtonKind.Pass;

        [Header("Visuals")]
        [Tooltip("Mesh that sinks into the housing while pressed.")]
        [SerializeField] Transform cap;

        [SerializeField] Renderer capRenderer;
        [SerializeField] Color idleColor = new Color(0.2f, 0.7f, 0.25f);
        [SerializeField] Color pressedColor = new Color(0.05f, 0.25f, 0.08f);
        [SerializeField] float pressDepth = 0.018f;
        [SerializeField] float pressDuration = 0.16f;

        [Header("Audio")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip clickClip;
        [SerializeField, Range(0f, 1f)] float clickVolume = 0.9f;

        [Header("Behaviour")]
        [Tooltip("Guards against a poke firing the button several times in one touch.")]
        [SerializeField] float cooldown = 0.6f;

        [Tooltip("Hands only press when they actually enter the collider.")]
        [SerializeField] LayerMask handLayers = ~0;

        float m_LastPressTime = float.NegativeInfinity;
        Vector3 m_CapHome;
        bool m_Animating;
        XRSimpleInteractable m_Interactable;

        public ButtonKind Kind => kind;

        /// <summary>Raised with this button's kind when pressed. ShiftManager subscribes.</summary>
        public event System.Action<ButtonKind> Pressed;

        void Awake()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;

            if (cap != null)
                m_CapHome = cap.localPosition;

            var col = GetComponent<Collider>();
            col.isTrigger = true;

            m_Interactable = GetComponent<XRSimpleInteractable>();
            if (m_Interactable != null)
                m_Interactable.selectEntered.AddListener(OnSelected);

            ApplyColor(idleColor);
        }

        void OnDestroy()
        {
            if (m_Interactable != null)
                m_Interactable.selectEntered.RemoveListener(OnSelected);
        }

        void OnSelected(UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs args)
        {
            TryPress();
        }

        void OnTriggerEnter(Collider other)
        {
            if ((handLayers.value & (1 << other.gameObject.layer)) == 0)
                return;

            TryPress();
        }

        /// <summary>Presses the button if the cooldown has elapsed. Safe to call from tests.</summary>
        public void TryPress()
        {
            if (Time.unscaledTime - m_LastPressTime < cooldown)
                return;

            m_LastPressTime = Time.unscaledTime;
            SimpleAudioSynth.Play(audioSource, clickClip, SynthSound.Click, clickVolume);

            if (!m_Animating)
                StartCoroutine(PressRoutine());

            Pressed?.Invoke(kind);
        }

        IEnumerator PressRoutine()
        {
            m_Animating = true;
            ApplyColor(pressedColor);

            if (cap != null)
            {
                var down = m_CapHome + Vector3.down * pressDepth;
                yield return MoveCap(m_CapHome, down, pressDuration * 0.4f);
                yield return MoveCap(down, m_CapHome, pressDuration * 0.6f);
            }
            else
            {
                yield return new WaitForSeconds(pressDuration);
            }

            ApplyColor(idleColor);
            m_Animating = false;
        }

        IEnumerator MoveCap(Vector3 from, Vector3 to, float duration)
        {
            var t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                cap.localPosition = Vector3.Lerp(from, to, Mathf.Clamp01(t / duration));
                yield return null;
            }
            cap.localPosition = to;
        }

        void ApplyColor(Color color)
        {
            if (capRenderer == null)
                return;

            // MaterialPropertyBlock keeps the shared material untouched, so the other button
            // (and every artist mesh using the same material) is unaffected.
            var block = new MaterialPropertyBlock();
            capRenderer.GetPropertyBlock(block);
            block.SetColor("_BaseColor", color);
            block.SetColor("_Color", color);
            capRenderer.SetPropertyBlock(block);
        }
    }
}
