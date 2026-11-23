using UnityEngine;
using UnityEngine.UI;

namespace DPS
{
    /// <summary>
    /// Lets a bare VR hand press a world-space UI button by touching it. The rig has no hand
    /// colliders, so the button rect (thickened by <see cref="touchDepth"/>) is tested against the
    /// tracked hand points. Ray clicks still go through the normal Button path.
    /// </summary>
    [RequireComponent(typeof(RectTransform))]
    public class HandTouchButton : MonoBehaviour
    {
        [SerializeField] Button button;
        [SerializeField] float touchDepth = 0.06f;
        [SerializeField] float cooldown = 1f;

        readonly Vector3[] m_Corners = new Vector3[4];
        RectTransform m_Rect;
        bool m_WasInside;
        float m_LastPress = float.NegativeInfinity;

        void Awake()
        {
            m_Rect = (RectTransform)transform;
            if (button == null)
                button = GetComponent<Button>();
        }

        void OnDisable()
        {
            m_WasInside = false;
        }

        void Update()
        {
            if (button == null || !button.interactable)
                return;

            m_Rect.GetWorldCorners(m_Corners);
            var bounds = new Bounds(m_Corners[0], Vector3.zero);
            for (var i = 1; i < 4; i++)
                bounds.Encapsulate(m_Corners[i]);

            var inside = HandProximity.IsHandInside(bounds, touchDepth);
            if (inside && !m_WasInside && Time.unscaledTime - m_LastPress >= cooldown)
            {
                m_LastPress = Time.unscaledTime;
                button.onClick.Invoke();
            }

            m_WasInside = inside;
        }
    }
}
