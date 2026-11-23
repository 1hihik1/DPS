using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DPS
{
    /// <summary>
    /// Fills the trunk with grabbable cargo the first time it is opened: a few harmless boxes plus,
    /// for smuggling cars, one bright contraband crate labelled from <see cref="CarData.contrabandLabel"/>.
    /// Everything spawned is destroyed when the car leaves, so cargo never piles up on the road.
    /// </summary>
    public class TrunkCargoSpawner : MonoBehaviour
    {
        [SerializeField] CarController car;
        [SerializeField] TrunkInteractable trunk;

        [Tooltip("Grabbable crate with an XRGrabInteractable, Rigidbody and a TMP label in its children.")]
        [SerializeField] GameObject cargoPrefab;

        [SerializeField] Transform[] cargoPoints;
        [SerializeField] Transform contrabandPoint;

        [SerializeField, Range(0, 3)] int minCargo = 2;
        [SerializeField, Range(0, 3)] int maxCargo = 3;

        [SerializeField] string[] cargoLabels = { "КАРТОШКА", "КОРОБКА", "ЗАПАСКА" };

        [SerializeField] Color cargoColor = new Color(0.45f, 0.32f, 0.18f);
        [SerializeField] Color contrabandColor = new Color(1f, 0.15f, 0.05f);

        readonly List<GameObject> m_Spawned = new List<GameObject>();
        bool m_Filled;

        void Awake()
        {
            if (car == null)
                car = GetComponent<CarController>();

            if (trunk == null)
                trunk = GetComponentInChildren<TrunkInteractable>(true);

            if (trunk != null)
                trunk.Opened += OnTrunkOpened;
        }

        void OnDestroy()
        {
            if (trunk != null)
                trunk.Opened -= OnTrunkOpened;

            Clear();
        }

        void OnTrunkOpened()
        {
            if (m_Filled || cargoPrefab == null)
                return;

            m_Filled = true;

            var data = car != null ? car.Data : null;
            var count = Random.Range(minCargo, maxCargo + 1);

            if (cargoPoints != null)
            {
                for (var i = 0; i < count && i < cargoPoints.Length; i++)
                {
                    if (cargoPoints[i] == null)
                        continue;

                    var label = cargoLabels != null && cargoLabels.Length > 0 ? cargoLabels[i % cargoLabels.Length] : "";
                    Spawn(cargoPoints[i], label, cargoColor, false);
                }
            }

            if (data != null && data.hasContraband && contrabandPoint != null)
                Spawn(contrabandPoint, data.contrabandLabel, contrabandColor, true);
        }

        void Spawn(Transform point, string label, Color color, bool contraband)
        {
            var go = Instantiate(cargoPrefab, point.position, point.rotation);
            go.name = contraband ? "Cargo_Contraband" : "Cargo_" + label;

            foreach (var text in go.GetComponentsInChildren<TMP_Text>(true))
            {
                text.text = label;
                if (contraband)
                    text.color = Color.yellow;
            }

            var block = new MaterialPropertyBlock();
            foreach (var r in go.GetComponentsInChildren<MeshRenderer>(true))
            {
                if (r.GetComponent<TMP_Text>() != null)
                    continue;
                r.GetPropertyBlock(block);
                block.SetColor("_BaseColor", color);
                block.SetColor("_Color", color);
                if (contraband)
                    block.SetColor("_EmissionColor", color * 1.5f);
                r.SetPropertyBlock(block);
            }

            if (contraband)
                go.transform.localScale *= 1.15f;

            m_Spawned.Add(go);
        }

        /// <summary>Destroys every spawned crate, releasing any hand that still holds one.</summary>
        public void Clear()
        {
            foreach (var go in m_Spawned)
            {
                if (go == null)
                    continue;

                var grab = go.GetComponent<XRGrabInteractable>();
                if (grab != null && grab.isSelected && grab.interactionManager != null)
                    grab.interactionManager.CancelInteractableSelection((IXRSelectInteractable)grab);

                Destroy(go);
            }

            m_Spawned.Clear();
            m_Filled = false;
        }
    }
}
