using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;
using UnityEngine.Serialization;

namespace DPS
{
    /// <summary>End-of-shift tally shown on the summary board.</summary>
    public struct ShiftResult
    {
        public int carsHandled;
        public int carsCorrect;
        public int carsWrong;
        public int cleanReleased;
        public int carsDetained;
        public int violatorsReleased;
        public int innocentDetained;
        public int bribesTaken;
        public int bribeMoney;
        public int money;
        public float seconds;

        public int AccuracyPercent => carsHandled == 0 ? 0 : Mathf.RoundToInt(carsCorrect * 100f / carsHandled);
    }

    /// <summary>
    /// Runs one shift: spawns cars, hands each stopped car to the player, listens to the baton,
    /// the desk buttons, the breathalyzer, the trunk and the bribe box, then scores the player.
    ///
    /// Every reference is optional. Anything left unwired in the Inspector is simply skipped, so
    /// the script is safe to drop into the scene before the Phase 3 props exist.
    /// </summary>
    public class ShiftManager : MonoBehaviour
    {
        public static ShiftManager Instance { get; private set; }

        [Header("Prefab")]
        [Tooltip("Car prefab. Its CarController drives itself once Initialise is called.")]
        [SerializeField] CarController carPrefab;

        [Header("Route")]
        [Tooltip("Lane centre where cars are spawned.")]
        [SerializeField] float laneX = 0.6f;

        [SerializeField] float spawnZ = -35f;

        [Tooltip("Cars face +Z, i.e. toward the booth and the barrier.")]
        [SerializeField] float spawnYaw = 0f;

        [Header("Shift")]
        [Tooltip("Start immediately on Play. Off when a start board drives BeginShift.")]
        [FormerlySerializedAs("autoStart")]
        [SerializeField] bool autoStartOnPlay = false;

        [SerializeField, Min(1)] int carsPerShift = 5;
        [SerializeField, Min(10f)] float shiftSeconds = 240f;
        [SerializeField] int seed;

        [Tooltip("Delay between a cleared car and the next spawn.")]
        [SerializeField] float spawnInterval = 1.2f;

        [Header("Economy")]
        [SerializeField] int detainCorrectReward = 1000;
        [SerializeField] int releaseCorrectReward = 500;
        [SerializeField] int releaseViolatorPenalty = 1500;
        [SerializeField] int detainInnocentPenalty = 800;
        [Tooltip("Bribe value added to the player's pocket. Taking it is always a mistake.")]
        [SerializeField] int bribeValue = 5000;

        [Header("Scene wiring")]
        [SerializeField] PoliceBaton baton;
        [SerializeField] BreathalyzerTool breathalyzer;
        [SerializeField] BribeBox bribeBox;
        [SerializeField] DeskButton passButton;
        [SerializeField] DeskButton detainButton;

        [Tooltip("Barrier arm pivot. Rotates up while a released car drives away.")]
        [SerializeField] Transform barrierArm;

        [SerializeField] float barrierOpenAngle = -78f;

        [Tooltip("Local hinge axis. The Phase 1 arm extends along -X, so it lifts around Z.")]
        [SerializeField] Vector3 barrierOpenAxis = Vector3.forward;
        [SerializeField] float barrierDuration = 0.7f;

        [Header("Beacon")]
        [Tooltip("Roof beacon light. Off while idle, flashes red/blue briefly on a detain.")]
        [SerializeField] Light beaconLight;

        [SerializeField] float beaconFlashSeconds = 3f;
        [SerializeField] float beaconPeriod = 0.18f;
        [SerializeField] float beaconIntensity = 3f;

        [Header("UI")]
        [Tooltip("Wanted board - shows the plate and crime to look out for.")]
        [SerializeField] TMP_Text wantedPlateText;

        [SerializeField] TMP_Text wantedCrimeText;

        [Tooltip("Desk HUD: cars remaining, timer, money.")]
        [SerializeField] TMP_Text statusText;

        [Tooltip("Seconds a hint (e.g. a missed car) stays on the status line.")]
        [SerializeField] float hintSeconds = 3f;

        [Tooltip("Start panel, hidden once a shift begins.")]
        [SerializeField] GameObject startRoot;

        [Tooltip("Root of the shift summary panel.")]
        [SerializeField] GameObject summaryRoot;

        [SerializeField] TMP_Text summaryText;

        [Header("Audio")]
        [SerializeField] AudioSource audioSource;
        [SerializeField] AudioClip sirenClip;
        [SerializeField] AudioClip cashClip;

        readonly List<CarData> m_Plan = new List<CarData>();
        System.Random m_Rng;
        int m_Spawned;
        int m_Handled;
        int m_Correct;
        int m_Wrong;
        int m_CleanReleased;
        int m_ViolatorsReleased;
        int m_InnocentDetained;
        int m_Bribes;
        int m_BribeMoney;
        int m_Detained;
        int m_Money;
        Coroutine m_BeaconRoutine;
        int m_Releasing;
        float m_ShiftStart;
        float m_NextSpawnTime;
        bool m_Running;
        bool m_SummaryShown;
        string m_WantedPlate;
        Quaternion m_BarrierHome;
        int m_LastStatusSeconds = -1;
        int m_LastStatusCars = -1;
        int m_LastStatusMoney = int.MinValue;
        float m_HintUntil;

        public CarController ActiveCar { get; private set; }
        public bool IsRunning => m_Running;
        public int CarsRemaining => Mathf.Max(0, carsPerShift - m_Handled);
        public int Money => m_Money;

        Quaternion BarrierOpenRotation => m_BarrierHome * Quaternion.AngleAxis(barrierOpenAngle, barrierOpenAxis);

        void Awake()
        {
            Instance = this;

            if (audioSource == null)
                audioSource = GetComponent<AudioSource>();

            if (audioSource == null)
                audioSource = gameObject.AddComponent<AudioSource>();

            audioSource.playOnAwake = false;
            audioSource.spatialBlend = 0.4f;

            if (barrierArm != null)
                m_BarrierHome = barrierArm.localRotation;

            if (summaryRoot != null)
                summaryRoot.SetActive(false);

            SetBeacon(false);
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            Unsubscribe();
        }

        void Start()
        {
            if (autoStartOnPlay)
                BeginShift();
            else if (startRoot != null)
                startRoot.SetActive(true);
        }

        // ------------------------------------------------------------ shift setup

        /// <summary>Builds the shift plan and starts spawning. Idempotent.</summary>
        public void BeginShift()
        {
            if (m_Running)
                return;

            Unsubscribe();

            m_Rng = new System.Random(seed == 0 ? System.Environment.TickCount : seed);
            m_Plan.Clear();

            BuildPlan();

            m_Spawned = 0;
            m_Handled = 0;
            m_Correct = 0;
            m_Wrong = 0;
            m_CleanReleased = 0;
            m_ViolatorsReleased = 0;
            m_InnocentDetained = 0;
            m_Bribes = 0;
            m_BribeMoney = 0;
            m_Detained = 0;
            m_Money = 0;
            m_SummaryShown = false;
            m_Running = true;
            m_ShiftStart = Time.time;
            m_NextSpawnTime = Time.time;
            m_LastStatusSeconds = -1;
            m_LastStatusCars = -1;
            m_LastStatusMoney = int.MinValue;
            m_HintUntil = 0f;
            StopAllCoroutines();
            m_BeaconRoutine = null;
            m_Releasing = 0;

            if (barrierArm != null)
                barrierArm.localRotation = BarrierOpenRotation;

            if (summaryRoot != null)
                summaryRoot.SetActive(false);

            if (startRoot != null)
                startRoot.SetActive(false);

            Subscribe();
            UpdateWantedBoard();
            UpdateStatus();
            StartCoroutine(SpawnLoop());
        }

        /// <summary>
        /// One wanted car is placed in the plan, always at slot 2 or later, so the board is never
        /// trivially legible from the first car alone.
        /// </summary>
        void BuildPlan()
        {
            var wantedIndex = carsPerShift >= 3
                ? 2 + m_Rng.Next(carsPerShift - 2)
                : Mathf.Max(0, carsPerShift - 1);

            m_WantedPlate = CarData.NewPlate(m_Rng);

            for (var i = 0; i < carsPerShift; i++)
            {
                var isWanted = i == wantedIndex;
                m_Plan.Add(CarData.Create(i, isWanted, m_WantedPlate, m_Rng));
            }
        }

        void Subscribe()
        {
            if (baton != null)
                baton.Signalled += OnBatonSignalled;

            if (passButton != null)
                passButton.Pressed += OnButtonPressed;

            if (detainButton != null)
                detainButton.Pressed += OnButtonPressed;

            if (bribeBox != null)
                bribeBox.Collected += OnBribeCollected;
        }

        void Unsubscribe()
        {
            if (baton != null)
                baton.Signalled -= OnBatonSignalled;

            if (passButton != null)
                passButton.Pressed -= OnButtonPressed;

            if (detainButton != null)
                detainButton.Pressed -= OnButtonPressed;

            if (bribeBox != null)
                bribeBox.Collected -= OnBribeCollected;
        }

        // -------------------------------------------------------------- spawning

        IEnumerator SpawnLoop()
        {
            // Outer loop: a missed car re-queues its slot, possibly after the last spawn.
            while (m_Running)
            {
                while (m_Running && m_Spawned < m_Plan.Count)
                {
                    var wait = m_NextSpawnTime - Time.time;
                    if (wait > 0f)
                        yield return new WaitForSeconds(wait);

                    // Never stack two cars: wait for the previous one to be cleared.
                    while (m_Running && ActiveCar != null)
                        yield return null;

                    if (!m_Running)
                        yield break;

                    if (m_Spawned >= m_Plan.Count)
                        break;

                    SpawnCar(m_Plan[m_Spawned]);
                    m_Spawned++;
                }

                // All cars out; the shift ends once the last one is resolved and has left the booth,
                // otherwise EndShift's StopAllCoroutines would kill the beacon flash or strand a
                // released car.
                while (m_Running && m_Spawned >= m_Plan.Count && (ActiveCar != null || m_BeaconRoutine != null || m_Releasing > 0))
                    yield return null;

                if (m_Spawned >= m_Plan.Count)
                    break;
            }

            if (m_Running)
                EndShift();
        }

        void SpawnCar(CarData data)
        {
            if (carPrefab == null)
            {
                // Without a prefab there is nothing to inspect; count the slot so the shift still
                // terminates instead of hanging forever.
                m_Handled++;
                m_Spawned = m_Plan.Count;
                return;
            }

            var pose = new CarController.Pose
            {
                position = new Vector3(laneX, 0f, spawnZ),
                rotation = Quaternion.Euler(0f, spawnYaw, 0f),
            };

            var car = Instantiate(carPrefab, pose.position, pose.rotation);
            car.name = "Car_" + data.plate;

            car.Arrived += OnCarArrived;
            car.Missed += OnCarMissed;
            car.Initialise(data, pose);

            ActiveCar = car;

            if (breathalyzer != null)
                breathalyzer.ResetDisplay();
        }

        void OnCarArrived(CarController car)
        {
            // Nothing to log yet; the arrival is what unlocks the player's tools.
        }

        // ------------------------------------------------------------- decisions

        void OnBatonSignalled()
        {
            if (ActiveCar == null || !ActiveCar.SignalToStop())
                return;

            // The lane is blocked in front of the stopping car until the player decides.
            StartCoroutine(RaiseBarrier(open: false));
        }

        void OnCarMissed(CarController car)
        {
            if (car != ActiveCar)
                return;

            car.Arrived -= OnCarArrived;
            car.Missed -= OnCarMissed;
            ActiveCar = null;

            // Re-queue the same plan slot: an unchecked car never counts as handled.
            m_Spawned = Mathf.Max(0, m_Spawned - 1);
            m_NextSpawnTime = Time.time + spawnInterval;
            ShowHint("Пропустили машину! Махните жезлом");
        }

        void ShowHint(string text)
        {
            m_HintUntil = Time.time + hintSeconds;
            if (statusText != null)
                statusText.text = text;
        }

        void OnButtonPressed(DeskButton.ButtonKind kind)
        {
            if (kind == DeskButton.ButtonKind.Pass)
                Judge(release: true);
            else
                Judge(release: false);
        }

        void OnBribeCollected(int amount)
        {
            if (ActiveCar == null || ActiveCar.Data == null || !ActiveCar.Data.offersBribe)
                return;

            m_Bribes++;
            var value = amount > 0 ? amount : bribeValue;
            m_BribeMoney += value;
            m_Money += value;
            ActiveCar.MarkBribeTaken();

            SimpleAudioSynth.Play(audioSource, cashClip, SynthSound.Cash, 0.8f);
            UpdateStatus();
        }

        /// <summary>
        /// Resolves the active car. Releasing or detaining a car that is still driving in is
        /// ignored, so a mis-press before the stop line cannot score.
        /// </summary>
        void Judge(bool release)
        {
            var car = ActiveCar;
            if (car == null || car.Data == null)
                return;

            if (car.State != CarController.CarState.Waiting)
                return;

            var data = car.Data;
            var violation = data.HasViolation;
            var correct = release ? !violation : violation;

            ActiveCar = null;
            car.Arrived -= OnCarArrived;
            car.Missed -= OnCarMissed;

            m_Handled++;

            if (correct)
            {
                m_Correct++;
                m_Money += release ? releaseCorrectReward : detainCorrectReward;
            }
            else
            {
                m_Wrong++;

                if (release)
                {
                    m_ViolatorsReleased++;
                    m_Money -= releaseViolatorPenalty;
                }
                else
                {
                    m_InnocentDetained++;
                    m_Money -= detainInnocentPenalty;
                }
            }

            if (release && !violation)
                m_CleanReleased++;

            if (release)
            {
                StartCoroutine(ReleaseWithBarrier(car));
            }
            else
            {
                // Detained cars are impounded behind the barrier, so give the player the siren
                // cue and let it drive off camera rather than blinking out of existence.
                SimpleAudioSynth.Play(audioSource, sirenClip, SynthSound.Siren, 0.7f);
                m_Detained++;
                FlashBeacon();
                car.Impound();
                StartCoroutine(RaiseBarrier(open: true));
            }

            if (breathalyzer != null)
                breathalyzer.ResetDisplay();

            m_NextSpawnTime = Time.time + spawnInterval;
            UpdateStatus();
        }

        IEnumerator ReleaseWithBarrier(CarController car)
        {
            m_Releasing++;
            yield return RaiseBarrier(open: true);

            if (car != null)
            {
                car.Release();

                // Keep the shift alive until the car clears the arm; the barrier stays open
                // so the next car can flow through unless it is waved down.
                const float clearOfBarrierZ = 7f;
                while (car != null && car.transform.position.z < clearOfBarrierZ)
                    yield return null;
            }

            m_Releasing--;
        }

        IEnumerator RaiseBarrier(bool open)
        {
            if (barrierArm == null)
                yield break;

            var target = open ? BarrierOpenRotation : m_BarrierHome;
            var start = barrierArm.localRotation;
            var t = 0f;

            while (t < barrierDuration)
            {
                t += Time.deltaTime;
                var k = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / barrierDuration));
                barrierArm.localRotation = Quaternion.Slerp(start, target, k);
                yield return null;
            }

            barrierArm.localRotation = target;
        }

        // ------------------------------------------------------------- shift end

        void Update()
        {
            if (!m_Running)
                return;

            if (m_SummaryShown)
                return;

            var elapsed = Time.time - m_ShiftStart;
            if (elapsed < shiftSeconds)
            {
                UpdateStatus();
                return;
            }

            // Time is up. Abandon whatever car is on the line so the shift cannot stall.
            if (ActiveCar != null && ActiveCar.Data != null)
            {
                ActiveCar.Arrived -= OnCarArrived;
                ActiveCar.Impound();
                ActiveCar = null;
            }

            EndShift();
        }

        void EndShift()
        {
            if (!m_Running)
                return;

            m_Running = false;
            m_SummaryShown = true;
            StopAllCoroutines();
            m_BeaconRoutine = null;
            m_Releasing = 0;
            SetBeacon(false);

            if (ActiveCar != null)
            {
                ActiveCar.Impound();
                ActiveCar = null;
            }

            if (barrierArm != null)
                barrierArm.localRotation = BarrierOpenRotation;

            var result = new ShiftResult
            {
                carsHandled = m_Handled,
                carsCorrect = m_Correct,
                carsWrong = m_Wrong,
                cleanReleased = m_CleanReleased,
                carsDetained = m_Detained,
                violatorsReleased = m_ViolatorsReleased,
                innocentDetained = m_InnocentDetained,
                bribesTaken = m_Bribes,
                bribeMoney = m_BribeMoney,
                money = m_Money,
                seconds = Time.time - m_ShiftStart,
            };

            if (summaryRoot != null)
                summaryRoot.SetActive(true);

            if (summaryText != null)
                summaryText.text = FormatSummary(result);

            if (startRoot != null)
                startRoot.SetActive(false);

            if (wantedPlateText != null)
                wantedPlateText.text = "\u0421\u041C\u0415\u041D\u0410 \u0417\u0410\u041A\u041E\u041D\u0427\u0415\u041D\u0410";

            if (statusText != null)
                statusText.text = "";

            if (breathalyzer != null)
                breathalyzer.ResetDisplay();
        }

        /// <summary>Plain ASCII thousands separator: TMP fonts often lack the NBSP that "N0" emits.</summary>
        static string FormatRub(int value)
        {
            var s = Mathf.Abs(value).ToString("#,0", System.Globalization.CultureInfo.InvariantCulture).Replace(',', ' ');
            return value < 0 ? "-" + s : s;
        }

        /// <summary>Russian shift report, also used by the Phase 3 summary board.</summary>
        public static string FormatSummary(ShiftResult result)
        {
            var minutes = Mathf.FloorToInt(result.seconds / 60f);
            var seconds = Mathf.FloorToInt(result.seconds % 60f);

            var sb = new System.Text.StringBuilder(320);
            sb.AppendLine("ИТОГИ СМЕНЫ");
            sb.AppendLine();
            sb.Append("Осмотрено машин: ").AppendLine(result.carsHandled.ToString());
            sb.Append("Задержано: ").AppendLine(result.carsDetained.ToString());
            sb.Append("Верных решений: ").Append(result.carsCorrect).Append(" (").Append(result.AccuracyPercent).AppendLine("%)");
            sb.Append("Ошибок: ").AppendLine(result.carsWrong.ToString());
            sb.Append("  пропущено нарушителей: ").AppendLine(result.violatorsReleased.ToString());
            sb.Append("  задержано невиновных: ").AppendLine(result.innocentDetained.ToString());
            sb.Append("Взяток (₽): ").Append(result.bribesTaken).Append(" шт. / ").Append(FormatRub(result.bribeMoney)).AppendLine(" ₽");
            sb.Append("Итого в кармане: ").Append(FormatRub(result.money)).AppendLine(" ₽");
            sb.Append("Время: ").Append(minutes.ToString("00")).Append(":").AppendLine(seconds.ToString("00"));
            sb.AppendLine();
            sb.Append("ВЕРДИКТ: ").AppendLine(Verdict(result));
            return sb.ToString();
        }

        /// <summary>Funny one-line verdict for the summary board.</summary>
        public static string Verdict(ShiftResult result)
        {
            if (result.carsHandled == 0)
                return "Проспал всю смену в будке. Чай остыл.";
            if (result.bribesTaken >= 2)
                return "Купил дачу. Служебное расследование уже едет.";
            if (result.bribesTaken == 1)
                return "Немного испачкал погоны. Начальство «не заметило».";
            if (result.AccuracyPercent >= 100)
                return "Гроза трассы! Портрет на доску почёта.";
            if (result.AccuracyPercent >= 80)
                return "Образцовый инспектор. Премия - банка огурцов.";
            if (result.AccuracyPercent >= 50)
                return "Сойдёт. Полосатая палочка вами довольна.";
            return "Переведён регулировать движение у коровника.";
        }


        // ------------------------------------------------------------------- UI

        void UpdateWantedBoard()
        {
            if (wantedPlateText != null)
                wantedPlateText.text = string.IsNullOrEmpty(m_WantedPlate) ? "ОРИЕНТИРОВКИ НЕТ" : m_WantedPlate;

            if (wantedCrimeText != null)
            {
                var crime = "НАРУШЕНИЙ НЕТ";
                foreach (var car in m_Plan)
                {
                    if (!car.isWanted)
                        continue;

                    crime = car.crimeDescription;
                    break;
                }

                wantedCrimeText.text = crime;
            }
        }

        void UpdateStatus()
        {
            if (statusText == null)
                return;

            if (Time.time < m_HintUntil)
                return;

            var remaining = Mathf.Max(0f, shiftSeconds - (Time.time - m_ShiftStart));
            var remainingSeconds = Mathf.CeilToInt(remaining);
            var carNumber = Mathf.Clamp(m_Handled + 1, 1, carsPerShift);

            // Rebuilt only when a displayed value actually changes; this runs every frame
            // otherwise and string allocation is the expensive part on Quest.
            if (remainingSeconds == m_LastStatusSeconds && carNumber == m_LastStatusCars && m_Money == m_LastStatusMoney)
                return;

            m_LastStatusSeconds = remainingSeconds;
            m_LastStatusCars = carNumber;
            m_LastStatusMoney = m_Money;

            statusText.text = "Машина " + carNumber + "/" + carsPerShift +
                              "   " + (remainingSeconds / 60).ToString("00") + ":" +
                              (remainingSeconds % 60).ToString("00") +
                              "   " + FormatRub(m_Money) + " ₽";
        }

        // --------------------------------------------------------------- beacon

        void FlashBeacon()
        {
            if (beaconLight == null)
                return;

            if (m_BeaconRoutine != null)
                StopCoroutine(m_BeaconRoutine);

            m_BeaconRoutine = StartCoroutine(BeaconRoutine());
        }

        IEnumerator BeaconRoutine()
        {
            SetBeacon(true);
            var t = 0f;
            while (t < beaconFlashSeconds)
            {
                var red = Mathf.Repeat(t, beaconPeriod * 2f) < beaconPeriod;
                beaconLight.color = red ? new Color(1f, 0.1f, 0.08f) : new Color(0.15f, 0.3f, 1f);
                t += Time.deltaTime;
                yield return null;
            }

            SetBeacon(false);
            m_BeaconRoutine = null;
        }

        void SetBeacon(bool on)
        {
            if (beaconLight == null)
                return;

            beaconLight.shadows = LightShadows.None;
            beaconLight.intensity = on ? beaconIntensity : 0f;
            beaconLight.enabled = on;
        }
    }
}
