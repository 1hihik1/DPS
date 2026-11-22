using System.Collections;
using UnityEngine;

namespace DPS
{
    /// <summary>
    /// Moves a grabbed tool back to its desk slot when it is dropped out of the world, so a fumbled
    /// baton never soft-locks the shift. Requires a Rigidbody on the same object.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public class ToolRespawner : MonoBehaviour
    {
        [Header("Respawn")]
        [Tooltip("World Y below which the tool is considered lost.")]
        [SerializeField] float respawnBelowY = -2f;

        [Tooltip("Where the tool returns to. Defaults to its position when the scene started.")]
        [SerializeField] Transform respawnAnchor;

        [Tooltip("Seconds to wait before teleporting the tool back.")]
        [SerializeField] float delay = 0.35f;

        Rigidbody m_Body;
        Vector3 m_HomePosition;
        Quaternion m_HomeRotation;
        bool m_Pending;

        void Awake()
        {
            m_Body = GetComponent<Rigidbody>();
            m_HomePosition = respawnAnchor != null ? respawnAnchor.position : transform.position;
            m_HomeRotation = respawnAnchor != null ? respawnAnchor.rotation : transform.rotation;
        }

        void FixedUpdate()
        {
            if (m_Pending || transform.position.y >= respawnBelowY)
                return;

            StartCoroutine(RespawnRoutine());
        }

        IEnumerator RespawnRoutine()
        {
            m_Pending = true;
            yield return new WaitForSeconds(delay);

            if (m_Body != null)
            {
                m_Body.linearVelocity = Vector3.zero;
                m_Body.angularVelocity = Vector3.zero;
                m_Body.position = m_HomePosition;
                m_Body.rotation = m_HomeRotation;
            }
            else
            {
                transform.SetPositionAndRotation(m_HomePosition, m_HomeRotation);
            }

            m_Pending = false;
        }

        /// <summary>Records the current pose as the respawn point.</summary>
        public void SetHome(Vector3 position, Quaternion rotation)
        {
            m_HomePosition = position;
            m_HomeRotation = rotation;
        }
    }
}
