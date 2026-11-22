using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using TMPro;

namespace DPS
{
    /// <summary>End-of-shift tally shown on the summary board.</summary>
    public struct ShiftResult
    {
        public int carsHandled;
        public int carsCorrect;
        public int carsWrong;
        public int cleanReleased;
        public int violatorsReleased;
        public int innocentDetained;
        public int bribesTaken;
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
        [SerializeField] float barrierDuration = 0.7f;

        [Header("UI")]
        [Tooltip("Wanted board - shows the plate and crime to look out for.")]
        [SerializeField] TMP_Text wantedPlateText;

        [SerializeField] TMP_Text wantedCrimeText;

        [Tooltip("Desk HUD: cars remaining, timer, money.")]
        [SerializeField] TMP_Text statusText;

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
        int m_Money;
        float m_ShiftStart;
        float m_NextSpawnTime;
        bool m_Running;
        bool m_SummaryShown;
        string m_WantedPlate;
        Quaternion m_BarrierHome;
        int m_LastStatusSeconds = -1;
        int m_LastStatusCars = -1;
        int m_LastStatusMoney = int.MinValue;

        public CarController ActiveCar { get; private set; }
        public bool IsRunning => m_Running;
        public int CarsRemaining => Mathf.Max(0, carsPerShift - m_Handled);
        public int Money => m_Money;

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
        }

        void OnDestroy()
        {
            if (Instance == this)
                Instance = null;

            Unsubscribe();
        }

        void Start()
        {
            BeginShift();
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
            m_Money = 0;
            m_SummaryShown = false;
            m_Running = true;
            m_ShiftStart = Time.time;
            m_NextSpawnTime = Time.time;
            m_LastStatusSeconds = -1;
            m_LastStatusCars = -1;
            m_LastStatusMoney = int.MinValue;

            if (summaryRoot != null)
                summaryRoot.SetActive(false);

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

                SpawnCar(m_Plan[m_Spawned]);
                m_Spawned++;
            }

            // All cars out; the shift ends once the last one is resolved.
            while (m_Running && ActiveCar != null)
                yield return null;

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
            if (ActiveCar != null)
                ActiveCar.SignalToStop();
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
            m_Money += amount > 0 ? amount : bribeValue;
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
                car.Impound();
            }

            if (breathalyzer != null)
                breathalyzer.ResetDisplay();

            m_NextSpawnTime = Time.time + spawnInterval;
            UpdateStatus();
        }

        IEnumerator ReleaseWithBarrier(CarController car)
        {
            yield return RaiseBarrier(open: true);

            if (car != null)
            {
                car.Release();

                // Wait for the car to clear the arm before dropping it again.
                const float clearOfBarrierZ = 7f;
                while (car != null && car.transform.position.z < clearOfBarrierZ)
                    yield return null;
            }

            yield return RaiseBarrier(open: false);
        }

        IEnumerator RaiseBarrier(bool open)
        {
            if (barrierArm == null)
                yield break;

            var target = open ? m_BarrierHome * Quaternion.Euler(barrierOpenAngle, 0f, 0f) : m_BarrierHome;
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

            if (ActiveCar != null)
            {
                ActiveCar.Impound();
                ActiveCar = null;
            }

            if (barrierArm != null)
                barrierArm.localRotation = m_BarrierHome;

            var result = new ShiftResult
            {
                carsHandled = m_Handled,
                carsCorrect = m_Correct,
                carsWrong = m_Wrong,
                cleanReleased = m_CleanReleased,
                violatorsReleased = m_ViolatorsReleased,
                innocentDetained = m_InnocentDetained,
                bribesTaken = m_Bribes,
                money = m_Money,
                seconds = Time.time - m_ShiftStart,
            };

            if (summaryRoot != null)
                summaryRoot.SetActive(true);

            if (summaryText != null)
                summaryText.text = FormatSummary(result);

            if (wantedPlateText != null)
                wantedPlateText.text = "\u0421\u041C\u0415\u041D\u0410 \u0417\u0410\u041A\u041E\u041D\u0427\u0415\u041D\u0410";

            if (statusText != null)
                statusText.text = "";

            if (breathalyzer != null)
                breathalyzer.ResetDisplay();
        }

        /// <summary>Russian shift report, also used by the Phase 3 summary board.</summary>
        public static string FormatSummary(ShiftResult result)
        {
            var minutes = Mathf.FloorToInt(result.seconds / 60f);
            var seconds = Mathf.FloorToInt(result.seconds % 60f);

            var sb = new System.Text.StringBuilder(256);
            sb.AppendLine("\u0418\u0422\u041E\u0413\u0418 \u0421\u041C\u0415\u041D\u042B");
            sb.AppendLine();
            sb.Append("\u041E\u0441\u043C\u043E\u0442\u0440\u0435\u043D\u043E \u043C\u0430\u0448\u0438\u043D: ").AppendLine(result.carsHandled.ToString());
            sb.Append("\u0412\u0435\u0440\u043D\u044B\u0445 \u0440\u0435\u0448\u0435\u043D\u0438\u0439: ").Append(result.carsCorrect).Append("  (")
              .Append(result.AccuracyPercent).AppendLine("%)");
            sb.Append("\u041E\u0448\u0438\u0431\u043E\u043A: ").AppendLine(result.carsWrong.ToString());
            sb.AppendLine();
            sb.Append("\u041F\u0440\u043E\u043F\u0443\u0449\u0435\u043D\u043E \u043D\u0430\u0440\u0443\u0448\u0438\u0442\u0435\u043B\u0435\u0439: ").AppendLine(result.violatorsReleased.ToString());
            sb.Append("\u0417\u0430\u0434\u0435\u0440\u0436\u0430\u043D\u043E \u043D\u0435\u0432\u0438\u043D\u043E\u0432\u043D\u044B\u0445: ").AppendLine(result.innocentDetained.ToString());
            sb.Append("\u0412\u0437\u044F\u0442\u043E \u0432\u0437\u044F\u0442\u043E\u043A: ").AppendLine(result.bribesTaken.ToString());
            sb.AppendLine();
            sb.Append("\u0414\u0415\u041D\u042C\u0413\u0418: ").Append(result.money.ToString("N0")).AppendLine(" \u20BD");
            sb.Append("\u0412\u0440\u0435\u043C\u044F: ").Append(minutes.ToString("00")).Append(":").AppendLine(seconds.ToString("00"));

            // Clean vs dirty shifts read differently, so the summary is a verdict rather than a
            // bare scoreboard.
            sb.AppendLine();
            if (result.bribesTaken > 0)
                sb.AppendLine("\u0421\u041C\u0415\u041D\u0410 \u041D\u0415\u0427\u0418\u0421\u0422\u0410\u042F");
            else if (result.AccuracyPercent >= 80)
                sb.AppendLine("\u0421\u041B\u0423\u0416\u0411\u0410 \u041E\u0422\u041B\u0418\u0427\u041D\u0410\u042F");
            else if (result.AccuracyPercent >= 50)
                sb.AppendLine("\u0421\u041B\u0423\u0416\u0411\u0410 \u0423\u0414\u041E\u0412\u041B\u0415\u0422\u0412\u041E\u0420\u0418\u0422\u0415\u041B\u042C\u041D\u0410\u042F");
            else
                sb.AppendLine("\u0421\u041B\u0423\u0416\u0411\u0410 \u041F\u0420\u041E\u0412\u0410\u041B\u0415\u041D\u0410");

            return sb.ToString();
        }

        // ------------------------------------------------------------------- UI

        void UpdateWantedBoard()
        {
            if (wantedPlateText != null)
                wantedPlateText.text = string.IsNullOrEmpty(m_WantedPlate)
                    ? "\u041E\u0420\u0418\u0415\u041D\u0422\u0418\u0420\u041E\u0412\u041A\u0418 \u041D\u0415\u0422"
                    : m_WantedPlate;

            if (wantedCrimeText != null)
            {
                var crime = "\u041D\u0410\u0420\u0423\u0428\u0415\u041D\u0418\u0419 \u041D\u0415\u0422";
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

            var remaining = Mathf.Max(0f, shiftSeconds - (Time.time - m_ShiftStart));
            var remainingSeconds = Mathf.CeilToInt(remaining);
            var cars = CarsRemaining;

            // Rebuilt only when a displayed value actually changes; this runs every frame
            // otherwise and string allocation is the expensive part on Quest.
            if (remainingSeconds == m_LastStatusSeconds && cars == m_LastStatusCars && m_Money == m_LastStatusMoney)
                return;

            m_LastStatusSeconds = remainingSeconds;
            m_LastStatusCars = cars;
            m_LastStatusMoney = m_Money;

            statusText.text = "\u041C\u0410\u0428\u0418\u041D: " + cars +
                              "   " + (remainingSeconds / 60).ToString("00") + ":" +
                              (remainingSeconds % 60).ToString("00") +
                              "   " + m_Money.ToString("N0") + " \u20BD";
        }
    }
}
