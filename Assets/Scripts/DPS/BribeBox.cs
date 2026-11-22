using UnityEngine;

namespace DPS
{
    /// <summary>
    /// Bribe collection box on the desk. Dropping a grabbed <see cref="CashBundle"/> into the
    /// trigger volume banks the money. Hand detection falls back to proximity because the rig has
    /// no hand colliders.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class BribeBox : MonoBehaviour
    {
        [Header("Visuals")]
        [SerializeField] Renderer rimRenderer;
        [SerializeField] Color idleColor = new Color(0.35f, 0.28f, 0.1f);
        [SerializeField] Color acceptColor = new Color(0.95f, 0.8f, 0.2f);

        [Header("Audio")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip collectClip;
        [SerializeField, Range(0f, 1f)] float collectVolume = 1f;

        [Header("Behaviour")]
        [Tooltip("Cash below this Y is treated as dropped in the box even without a physics event.")]
        [SerializeField] float catchRadius = 0.3f;

        MaterialPropertyBlock m_Block;

        /// <summary>Raised with the collected amount.</summary>
        public event System.Action<int> Collected;

        void Awake()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;

            GetComponent<Collider>().isTrigger = true;
            m_Block = new MaterialPropertyBlock();
            SetRim(idleColor);
        }

        void OnTriggerEnter(Collider other)
        {
            Accept(other != null ? other.GetComponentInParent<CashBundle>() : null);
        }

        void Update()
        {
            // Safety net: a bundle released from a hand can tunnel through the thin trigger.
            var bundles = FindObjectsByType<CashBundle>(FindObjectsSortMode.None);
            foreach (var bundle in bundles)
            {
                if (bundle == null || !bundle.CanBeCollected)
                    continue;

                if (Vector3.Distance(bundle.transform.position, transform.position) <= catchRadius)
                    Accept(bundle);
            }
        }

        /// <summary>Banks a bundle. Public so tests and the desk UI can deposit directly.</summary>
        public bool Accept(CashBundle bundle)
        {
            if (bundle == null || !bundle.CanBeCollected)
                return false;

            var amount = bundle.Amount;
            bundle.Consume();

            SimpleAudioSynth.Play(audioSource, collectClip, SynthSound.Cash, collectVolume);
            Collected?.Invoke(amount);

            StopAllCoroutines();
            StartCoroutine(FlashAccept());
            return true;
        }

        System.Collections.IEnumerator FlashAccept()
        {
            SetRim(acceptColor);
            yield return new WaitForSeconds(0.4f);
            SetRim(idleColor);
        }

        void SetRim(Color color)
        {
            if (rimRenderer == null)
                return;

            if (m_Block == null)
                m_Block = new MaterialPropertyBlock();

            rimRenderer.GetPropertyBlock(m_Block);
            m_Block.SetColor("_BaseColor", color);
            m_Block.SetColor("_Color", color);
            m_Block.SetColor("_EmissionColor", color * 1.6f);
            rimRenderer.SetPropertyBlock(m_Block);
        }
    }
}
