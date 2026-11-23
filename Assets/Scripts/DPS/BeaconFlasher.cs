using UnityEngine;

namespace DPS
{
    /// <summary>
    /// Alternates the roof beacon light between red and blue. Reuses one shadowless light, so the
    /// beacon stays inside the three-light Quest budget.
    /// </summary>
    [RequireComponent(typeof(Light))]
    public class BeaconFlasher : MonoBehaviour
    {
        [SerializeField] Color colorA = new Color(1f, 0.1f, 0.08f);
        [SerializeField] Color colorB = new Color(0.15f, 0.3f, 1f);
        [SerializeField] float intensity = 2.2f;
        [SerializeField] float period = 0.4f;

        Light m_Light;

        void Awake()
        {
            m_Light = GetComponent<Light>();
            m_Light.shadows = LightShadows.None;
            m_Light.intensity = intensity;
        }

        void Update()
        {
            var phase = Mathf.Repeat(Time.time, period * 2f) < period;
            m_Light.color = phase ? colorA : colorB;
        }
    }
}
