using System.Collections;
using UnityEngine;
using TMPro;

namespace DPS
{
    /// <summary>
    /// One approaching car. Drives from spawn toward the stop line, reacts to the baton, rolls its
    /// window down, offers a bribe when configured, and drives away or is impounded on a decision.
    ///
    /// Motion is scripted rather than rigidbody-driven: a physics car on Quest is a constant source
    /// of jitter and tunnelling, and the player can never push it anyway.
    /// </summary>
    public class CarController : MonoBehaviour
    {
        public enum CarState
        {
            Approaching,
            Braking,
            Waiting,
            Leaving,
            Done,
        }

        [Header("Route")]
        // Spawn Z is owned by ShiftManager (it builds the CarController.Pose), so it is not
        // duplicated here; a second copy would silently disagree with the spawner.
        [Tooltip("Z where the car must stop for inspection.")]
        [SerializeField] float stopZ = 0f;

        [Tooltip("Z where a released car is despawned.")]
        [SerializeField] float despawnZ = 40f;

        [SerializeField] float approachSpeed = 5.5f;
        [SerializeField] float leaveSpeed = 9f;

        [Tooltip("A signal is ignored once the car is closer than this to the stop line.")]
        [SerializeField] float minSignalDistance = 2f;

        [Tooltip("An unsignalled car past stopZ + this counts as missed.")]
        [SerializeField] float missedZOffset = 4f;

        [Header("Visuals")]
        [Tooltip("Container the artists swap meshes inside.")]
        [SerializeField] Transform visuals;

        [SerializeField] Renderer bodyRenderer;

        [Tooltip("Extra panels (trunk walls, lid) tinted with the body colour.")]
        [SerializeField] Renderer[] bodyPanels;

        [Tooltip("Any further renderers (e.g. the trunk lid) tinted together with the body.")]
        [SerializeField] Renderer[] extraBodyRenderers;

        [SerializeField] Transform driverWindow;
        [SerializeField] Vector3 windowClosedLocalPos;
        [SerializeField] Vector3 windowOpenLocalPos;
        [SerializeField] float windowDuration = 0.8f;

        [Header("TMP labels")]
        [SerializeField] TMP_Text frontPlate;
        [SerializeField] TMP_Text rearPlate;
        [SerializeField] TMP_Text dialogueText;

        [Tooltip("Canvas holding the dialogue bubble; hidden until the car stops.")]
        [SerializeField] GameObject dialogueRoot;

        [Tooltip("Canvas holding the bribe offer; hidden unless a bribe is offered.")]
        [SerializeField] GameObject bribeRoot;

        [Header("Interaction anchors")]
        [Tooltip("Point the breathalyzer must reach.")]
        [SerializeField] Transform breathZone;

        [Tooltip("Where the cash bundle is spawned.")]
        [SerializeField] Transform cashSpawn;

        [SerializeField] TrunkInteractable trunk;

        [Tooltip("Contraband cargo in the trunk, shown only for smuggling cars.")]
        [SerializeField] GameObject contrabandRoot;

        [SerializeField] TMP_Text contrabandText;

        [Header("Lights")]
        [Tooltip("Headlights, which are kept shadowless for Quest.")]
        [SerializeField] Light[] headlights;

        [Header("Audio")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip hornClip;
        [SerializeField] AudioClip engineClip;

        [Header("Bribe")]
        [SerializeField] CashBundle cashPrefab;

        [Header("Behaviour")]
        [Tooltip("Seconds to wait at the line before the player is nudged with a horn.")]
        [SerializeField] float patienceSeconds = 8f;

        CarState m_State = CarState.Approaching;
        float m_Speed;
        float m_BrakeDecel;
        Coroutine m_WindowAnim;
        float m_WaitStartedAt;
        bool m_Honked;
        bool m_BribeTaken;
        bool m_InspectionDone;
        bool m_WindowOpen;
        CashBundle m_Cash;

        public CarData Data { get; private set; }
        public CarState State => m_State;
        public bool IsWindowOpen => m_WindowOpen;
        public Transform BreathZone => breathZone;
        public TrunkInteractable Trunk => trunk;
        public bool BribeTaken => m_BribeTaken;
        public bool WasInspected => m_InspectionDone;
        public bool IsActive => m_State != CarState.Done;

        /// <summary>Raised when the car reaches the stop line and is ready to inspect.</summary>
        public event System.Action<CarController> Arrived;

        /// <summary>Raised when the car drives past the booth because nobody waved it down.</summary>
        public event System.Action<CarController> Missed;

        void Awake()
        {
            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 1f;

            // Quest budget: headlights must never cast shadows.
            if (headlights != null)
            {
                foreach (var light in headlights)
                {
                    if (light == null)
                        continue;
                    light.shadows = LightShadows.None;
                }
            }

            if (windowClosedLocalPos == Vector3.zero && driverWindow != null)
                windowClosedLocalPos = driverWindow.localPosition;

            HideBubble();
        }

        /// <summary>Sets up the car for one shift slot and starts it moving.</summary>
        public void Initialise(CarData data, CarController.Pose spawn)
        {
            Data = data;
            m_State = CarState.Approaching;
            m_Speed = approachSpeed;
            m_Honked = false;
            m_BribeTaken = false;
            m_InspectionDone = false;
            m_WindowOpen = false;

            transform.position = spawn.position;
            transform.rotation = spawn.rotation;

            ApplyAppearance();

            if (dialogueRoot != null)
                dialogueRoot.SetActive(false);

            if (bribeRoot != null)
                bribeRoot.SetActive(false);

            if (trunk != null)
                trunk.Close();

            if (driverWindow != null)
                driverWindow.localPosition = windowClosedLocalPos;

            if (engineClip != null && audioSource != null)
            {
                audioSource.clip = engineClip;
                audioSource.loop = true;
                audioSource.volume = 0.35f;
                audioSource.Play();
            }
        }

        /// <summary>Simple struct so the spawn transform can be passed without a Transform ref.</summary>
        public struct Pose
        {
            public Vector3 position;
            public Quaternion rotation;
        }

        void ApplyAppearance()
        {
            if (Data == null)
                return;

            if (frontPlate != null)
                frontPlate.text = Data.plate;

            if (rearPlate != null)
                rearPlate.text = Data.plate;

            if (bodyRenderer != null)
            {
                // MaterialPropertyBlock keeps every car sharing one material, so the whole
                // fleet renders from a single draw setup instead of one material per car.
                var block = new MaterialPropertyBlock();
                bodyRenderer.GetPropertyBlock(block);
                block.SetColor("_BaseColor", Data.bodyColor);
                block.SetColor("_Color", Data.bodyColor);
                bodyRenderer.SetPropertyBlock(block);

                if (bodyPanels != null)
                {
                    foreach (var panel in bodyPanels)
                    {
                        if (panel != null)
                            panel.SetPropertyBlock(block);
                    }
                }

                if (extraBodyRenderers != null)
                {
                    foreach (var extra in extraBodyRenderers)
                    {
                        if (extra != null)
                            extra.SetPropertyBlock(block);
                    }
                }
            }

            if (contrabandRoot != null)
                contrabandRoot.SetActive(Data.hasContraband);

            if (contrabandText != null)
                contrabandText.text = Data.contrabandLabel;

            if (dialogueText != null)
            {
                dialogueText.text = Data.offersBribe
                    ? "\u041A\u043E\u043C\u0430\u043D\u0434\u0438\u0440, \u0434\u0430\u0432\u0430\u0439 \u0434\u043E\u0433\u043E\u0432\u043E\u0440\u0438\u043C\u0441\u044F..."
                    : Data.driverLine;
            }
        }

        void Update()
        {
            switch (m_State)
            {
                case CarState.Approaching:
                    UpdateApproaching();
                    break;
                case CarState.Braking:
                    UpdateBraking();
                    break;
                case CarState.Waiting:
                    UpdateWaiting();
                    break;
                case CarState.Leaving:
                    UpdateLeaving();
                    break;
            }
        }

        void UpdateApproaching()
        {
            // Unsignalled traffic keeps highway speed and drives straight past the booth.
            m_Speed = Mathf.MoveTowards(m_Speed, approachSpeed, 4f * Time.deltaTime);
            Advance();

            if (transform.position.z < stopZ + missedZOffset)
                return;

            m_State = CarState.Leaving;
            Missed?.Invoke(this);
        }

        /// <summary>
        /// Called by the baton when the player signals this car down. Returns false when the car
        /// is already too close to the line to stop smoothly, or is not approaching.
        /// </summary>
        public bool SignalToStop()
        {
            if (m_State != CarState.Approaching)
                return false;

            var distance = stopZ - transform.position.z;
            if (distance < minSignalDistance)
                return false;

            // Constant deceleration that brings the current speed to zero exactly at the line.
            m_BrakeDecel = Mathf.Max(0.5f, m_Speed * m_Speed / (2f * distance));
            m_State = CarState.Braking;
            return true;
        }

        void UpdateBraking()
        {
            var distance = Mathf.Max(0f, stopZ - transform.position.z);
            var targetSpeed = Mathf.Min(approachSpeed, Mathf.Sqrt(2f * m_BrakeDecel * distance));
            m_Speed = Mathf.Max(0.25f, targetSpeed);

            var p = transform.position;
            p.z = Mathf.Min(stopZ, p.z + m_Speed * Time.deltaTime);
            transform.position = p;

            if (stopZ - p.z <= 0.01f)
                BeginWait();
        }

        void Advance()
        {
            var p = transform.position;
            p.z += m_Speed * Time.deltaTime;
            transform.position = p;
        }

        void BeginWait()
        {
            m_State = CarState.Waiting;
            m_WaitStartedAt = Time.time;
            m_Speed = 0f;

            var p = transform.position;
            p.z = stopZ;
            transform.position = p;

            OpenWindow();
            ShowBubble();

            if (Data != null && Data.offersBribe)
                SpawnBribe();

            Arrived?.Invoke(this);
        }

        void UpdateWaiting()
        {
            if (m_Honked || Time.time - m_WaitStartedAt < patienceSeconds)
                return;

            m_Honked = true;
            SimpleAudioSynth.Play(audioSource, hornClip, SynthSound.BeepRed, 0.5f, 0.6f);
        }

        void UpdateLeaving()
        {
            m_Speed = Mathf.MoveTowards(m_Speed, leaveSpeed, 6f * Time.deltaTime);
            Advance();

            if (transform.position.z < despawnZ)
                return;

            m_State = CarState.Done;
            Destroy(gameObject);
        }

        // ---------------------------------------------------------------- window

        void OpenWindow()
        {
            if (driverWindow == null || m_WindowOpen)
                return;

            m_WindowOpen = true;

            if (m_WindowAnim != null)
                StopCoroutine(m_WindowAnim);

            m_WindowAnim = StartCoroutine(WindowRoutine(windowOpenLocalPos));
        }

        IEnumerator WindowRoutine(Vector3 target)
        {
            var start = driverWindow.localPosition;
            var t = 0f;

            while (t < windowDuration)
            {
                t += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / windowDuration));
                driverWindow.localPosition = Vector3.Lerp(start, target, k);
                yield return null;
            }

            driverWindow.localPosition = target;
            m_WindowAnim = null;
        }

        // --------------------------------------------------------------- bubbles

        void ShowBubble()
        {
            if (dialogueRoot == null)
                return;

            dialogueRoot.SetActive(true);

            if (bribeRoot != null && Data != null && Data.offersBribe)
                bribeRoot.SetActive(true);
        }

        void HideBubble()
        {
            if (dialogueRoot != null)
                dialogueRoot.SetActive(false);

            if (bribeRoot != null)
                bribeRoot.SetActive(false);
        }

        // ---------------------------------------------------------------- bribe

        void SpawnBribe()
        {
            if (cashPrefab == null || cashSpawn == null)
                return;

            var bundle = Instantiate(cashPrefab, cashSpawn.position, cashSpawn.rotation);
            bundle.Configure(5000);
            bundle.name = "CashBundle";
            m_Cash = bundle;
        }

        /// <summary>An offer nobody banked leaves with the driver instead of hanging in the air.</summary>
        void RetractBribe()
        {
            // Consume also cancels any hand still holding it, so a stale bundle can never be
            // credited to the next car.
            if (m_Cash != null && m_Cash.CanBeCollected)
                m_Cash.Consume();

            m_Cash = null;
        }

        void ClearCargo()
        {
            var spawner = GetComponent<TrunkCargoSpawner>();
            if (spawner != null)
                spawner.Clear();
        }

        void OnDestroy()
        {
            RetractBribe();
            ClearCargo();
        }

        /// <summary>Called by the bribe box when this car's money is banked.</summary>
        public void MarkBribeTaken()
        {
            m_BribeTaken = true;

            if (bribeRoot != null)
                bribeRoot.SetActive(false);
        }

        /// <summary>Called by the breathalyzer after a reading.</summary>
        public void OnInspectedWithTool()
        {
            m_InspectionDone = true;
        }

        // -------------------------------------------------------------- decisions

        /// <summary>Barrier up: the car drives off toward +Z.</summary>
        public void Release()
        {
            if (m_State == CarState.Leaving || m_State == CarState.Done)
                return;

            HideBubble();
            RetractBribe();
            ClearCargo();

            if (trunk != null && trunk.IsOpen)
                trunk.Close();

            if (audioSource != null)
                audioSource.Stop();

            m_State = CarState.Leaving;
            m_Speed = 1.5f;
        }

        /// <summary>Impounded: remove the car immediately.</summary>
        public void Impound()
        {
            m_State = CarState.Done;
            RetractBribe();
            ClearCargo();

            if (audioSource != null)
                audioSource.Stop();

            Destroy(gameObject);
        }
    }
}
