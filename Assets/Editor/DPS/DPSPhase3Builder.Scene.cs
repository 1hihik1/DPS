using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DPS.EditorTools
{
    public static partial class DPSPhase3Builder
    {
        // Desk: world X -2.575..-2.025 (front edge toward road), Z -1.0..+0.35, top Y 0.88.
        const float DeskTopY = 0.88f;

        static void BuildScene(CarController carPrefab)
        {
            foreach (var go in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (go.name == RootName)
                    Object.DestroyImmediate(go);
            }

            var root = new GameObject(RootName).transform;
            var world = GameObject.Find("DPS_World");

            var shiftGo = new GameObject("ShiftManager");
            shiftGo.transform.SetParent(root, false);
            shiftGo.AddComponent<AudioSource>().playOnAwake = false;
            var shift = shiftGo.AddComponent<ShiftManager>();

            var anchors = Group("ToolAnchors", root);
            var tools = Group("Tools", root);

            var waveZone = BuildWaveZone(root);
            var baton = BuildBaton(tools, anchors, waveZone);
            var breath = BuildBreathalyzer(tools, anchors);
            BuildFlashlight(tools, anchors);

            var controls = Group("DeskControls", root);
            var pass = BuildDeskButton(controls, "PassButton", new Vector3(-2.12f, DeskTopY, -0.65f), DeskButton.ButtonKind.Pass,
                "ПРОПУСТИТЬ", new Color(0.15f, 0.8f, 0.25f), new Color(0.04f, 0.25f, 0.08f));
            var detain = BuildDeskButton(controls, "DetainButton", new Vector3(-2.12f, DeskTopY, -0.87f), DeskButton.ButtonKind.Detain,
                "ЗАДЕРЖАТЬ", new Color(0.9f, 0.1f, 0.08f), new Color(0.3f, 0.03f, 0.02f));
            var bribeBox = BuildBribeBox(controls);

            var ui = Group("BoothUI", root);
            TMP_Text plate, crime, status, summary;
            GameObject startRoot, summaryRoot;
            BuildStartBoard(ui, shift, out startRoot, out summaryRoot, out summary);
            BuildWantedBoard(ui, out plate, out crime, out status);
            BuildBounds(root);
            ConfigureXRBody();

            // Wiring.
            Set(shift, "carPrefab", carPrefab);
            SetF(shift, "laneX", 0.35f);
            SetB(shift, "autoStartOnPlay", false);
            Set(shift, "baton", baton);
            Set(shift, "breathalyzer", breath);
            Set(shift, "bribeBox", bribeBox);
            Set(shift, "passButton", pass);
            Set(shift, "detainButton", detain);
            Set(shift, "wantedPlateText", plate);
            Set(shift, "wantedCrimeText", crime);
            Set(shift, "statusText", status);
            Set(shift, "startRoot", startRoot);
            Set(shift, "summaryRoot", summaryRoot);
            Set(shift, "summaryText", summary);

            if (world != null)
            {
                var arm = world.transform.Find("Barrier/ArmPivot");
                if (arm != null)
                    Set(shift, "barrierArm", arm);
                else
                    Debug.LogWarning("[DPS Phase 3] Barrier/ArmPivot not found.");

                var beaconTr = world.transform.Find("Booth/BeaconLight");
                var beacon = beaconTr != null ? beaconTr.GetComponent<Light>() : null;
                if (beacon != null)
                {
                    // A free-running flasher would fight the shift-driven flash.
                    var flasher = beacon.GetComponent<BeaconFlasher>();
                    if (flasher != null)
                        Object.DestroyImmediate(flasher);

                    beacon.shadows = LightShadows.None;
                    beacon.intensity = 0f;
                    beacon.enabled = false;
                    Set(shift, "beaconLight", beacon);
                }
                else
                {
                    Debug.LogWarning("[DPS Phase 3] Booth/BeaconLight not found.");
                }
            }

            // Quest budget: no light in the scene may cast shadows, even disabled template ones.
            foreach (var l in Object.FindObjectsByType<Light>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                l.shadows = LightShadows.None;
        }

        // ---------------------------------------------------------------- tools

        static Transform Anchor(Transform anchors, string name, Vector3 pos, Vector3 euler)
        {
            var a = Group(name, anchors);
            a.position = pos;
            a.eulerAngles = euler;
            return a;
        }

        static GameObject ToolRoot(string name, Transform parent, Vector3 pos, Vector3 euler, float mass)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;
            go.transform.eulerAngles = euler;

            var body = go.AddComponent<Rigidbody>();
            body.mass = mass;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            go.AddComponent<AudioSource>().playOnAwake = false;
            return go;
        }

        static void FinishTool(GameObject go, Transform anchor)
        {
            var grab = go.AddComponent<XRGrabInteractable>();
            GrabTuning.Apply(grab);
            GrabTuning.MakeHoldable(grab, go.GetComponent<Rigidbody>());

            var respawn = go.AddComponent<ToolRespawner>();
            Set(respawn, "respawnAnchor", anchor);
        }

        static Collider BuildWaveZone(Transform root)
        {
            var zone = new GameObject("RoadWaveZone");
            zone.transform.SetParent(root, false);
            zone.transform.position = new Vector3(-1.0f, 1.4f, -1.5f);
            zone.layer = 2; // Ignore Raycast: never blocks teleport/UI rays.
            return AddBox(zone, Vector3.zero, new Vector3(1.4f, 1.8f, 4.5f), true);
        }

        static PoliceBaton BuildBaton(Transform parent, Transform anchors, Collider waveZone)
        {
            // Lies along Z on the desk; the tool's long axis is local Y.
            var pos = new Vector3(-2.47f, DeskTopY + 0.02f, 0.05f);
            var euler = new Vector3(90f, 0f, 0f);
            var go = ToolRoot("PoliceBaton", parent, pos, euler, 0.3f);

            Group("Tip", go.transform, new Vector3(0f, 0.25f, 0f)); // child 0 - PoliceBaton.m_Tip
            var v = Visuals(go.transform);
            Cyl("Handle", v, new Vector3(0f, -0.2f, 0f), 0.036f, 0.1f, M("BatonBlack"));
            for (var i = 0; i < 4; i++)
            {
                var y = -0.125f + 0.1f * i;
                Cyl("StripeW_" + i, v, new Vector3(0f, y + 0.025f, 0f), 0.034f, 0.05f, M("BatonWhite"));
                Cyl("StripeB_" + i, v, new Vector3(0f, y + 0.075f, 0f), 0.034f, 0.05f, M("BatonBlack"));
            }

            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 1;
            cap.radius = 0.02f;
            cap.height = 0.5f;

            FinishTool(go, Anchor(anchors, "BatonAnchor", pos, euler));
            var baton = go.AddComponent<PoliceBaton>();
            Set(baton, "grabInteractable", go.GetComponent<XRGrabInteractable>());
            Set(baton, "waveZone", waveZone);
            return baton;
        }

        static BreathalyzerTool BuildBreathalyzer(Transform parent, Transform anchors)
        {
            var size = new Vector3(0.07f, 0.03f, 0.15f);
            var pos = new Vector3(-2.3f, DeskTopY + size.y * 0.5f + 0.003f, -0.28f);
            var go = ToolRoot("Breathalyzer", parent, pos, Vector3.zero, 0.25f);

            var v = Visuals(go.transform);
            Box("Body", v, Vector3.zero, size, M("DeviceYellow"));
            Box("ScreenBg", v, new Vector3(0f, size.y * 0.5f + 0.001f, -0.02f), new Vector3(0.055f, 0.002f, 0.05f), M("ScreenBg"));
            var led = Box("Led", v, new Vector3(0f, size.y * 0.5f + 0.003f, 0.025f), new Vector3(0.02f, 0.006f, 0.012f), M("Led"));
            Cyl("NozzleMesh", v, new Vector3(0f, 0f, size.z * 0.5f + 0.03f), 0.015f, 0.06f, M("DeviceDark"), new Vector3(90f, 0f, 0f));

            var nozzle = Group("Nozzle", go.transform, new Vector3(0f, 0f, size.z * 0.5f + 0.06f));
            var screen = Label("Screen", go.transform, new Vector3(0f, size.y * 0.5f + 0.0025f, -0.02f), Face(Vector3.up, Vector3.right),
                "--- ‰", 0.2f, new Color(0.4f, 1f, 0.5f), new Vector2(0.05f, 0.045f));

            AddBox(go, new Vector3(0f, 0f, 0.03f), new Vector3(size.x, size.y, size.z + 0.06f));
            FinishTool(go, Anchor(anchors, "BreathalyzerAnchor", pos, Vector3.zero));

            var tool = go.AddComponent<BreathalyzerTool>();
            Set(tool, "grabInteractable", go.GetComponent<XRGrabInteractable>());
            Set(tool, "nozzle", nozzle);
            Set(tool, "readout", screen);
            Set(tool, "ledRenderer", led.GetComponent<Renderer>());
            var so = new SerializedObject(tool);
            so.FindProperty("idleText").stringValue = "--- ‰";
            so.ApplyModifiedPropertiesWithoutUndo();
            return tool;
        }

        static FlashlightTool BuildFlashlight(Transform parent, Transform anchors)
        {
            var pos = new Vector3(-2.4f, DeskTopY + 0.026f, -0.52f);
            var go = ToolRoot("Flashlight", parent, pos, Vector3.zero, 0.2f);

            var v = Visuals(go.transform);
            Cyl("Body", v, Vector3.zero, 0.04f, 0.2f, M("DeviceDark"), new Vector3(90f, 0f, 0f));
            Cyl("Head", v, new Vector3(0f, 0f, 0.11f), 0.052f, 0.04f, M("DeviceDark"), new Vector3(90f, 0f, 0f));
            Cyl("Lens", v, new Vector3(0f, 0f, 0.131f), 0.044f, 0.004f, M("Lens"), new Vector3(90f, 0f, 0f));

            var lightGo = Group("SpotLight", go.transform, new Vector3(0f, 0f, 0.14f));
            var spot = lightGo.gameObject.AddComponent<Light>();
            spot.type = LightType.Spot;
            spot.shadows = LightShadows.None;
            spot.range = 22f;
            spot.spotAngle = 55f;
            spot.intensity = 8f;
            spot.enabled = false;

            var cap = go.AddComponent<CapsuleCollider>();
            cap.direction = 2;
            cap.radius = 0.026f;
            cap.height = 0.27f;
            cap.center = new Vector3(0f, 0f, 0.02f);

            FinishTool(go, Anchor(anchors, "FlashlightAnchor", pos, Vector3.zero));
            var tool = go.AddComponent<FlashlightTool>();
            Set(tool, "grabInteractable", go.GetComponent<XRGrabInteractable>());
            Set(tool, "spotLight", spot);
            SetB(tool, "lightWhenIdle", false);
            return tool;
        }

        // ---------------------------------------------------------- desk stuff

        static DeskButton BuildDeskButton(Transform parent, string name, Vector3 pos, DeskButton.ButtonKind kind,
                                          string label, Color idle, Color pressed)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.position = pos;

            var v = Visuals(go.transform);
            Box("Housing", v, new Vector3(0f, 0.02f, 0f), new Vector3(0.13f, 0.04f, 0.13f), M("ButtonHousing"));
            var cap = Cyl("Cap", v, new Vector3(0f, 0.055f, 0f), 0.095f, 0.03f, M("ButtonCap"));

            AddBox(go, new Vector3(0f, 0.05f, 0f), new Vector3(0.13f, 0.1f, 0.13f), true);

            var target = Group("RayTarget", go.transform);
            AddBox(target.gameObject, new Vector3(0f, 0.055f, 0f), new Vector3(0.1f, 0.035f, 0.1f));
            target.gameObject.AddComponent<XRSimpleInteractable>();

            go.AddComponent<AudioSource>().playOnAwake = false;
            var button = go.AddComponent<DeskButton>();
            SetI(button, "kind", (int)kind);
            Set(button, "cap", cap.transform);
            Set(button, "capRenderer", cap.GetComponent<Renderer>());
            SetColor(button, "idleColor", idle);
            SetColor(button, "pressedColor", pressed);

            // Flat on the desk on the player's side of the button, reading toward the road.
            Label("Label", go.transform, new Vector3(-0.11f, 0.003f, 0f), Face(Vector3.up, Vector3.right),
                label, 0.32f, idle, new Vector2(0.2f, 0.05f));
            return button;
        }

        static BribeBox BribeBoxRoot(Transform parent, out GameObject go)
        {
            go = new GameObject("BribeBox");
            go.transform.SetParent(parent, false);
            go.transform.position = new Vector3(-1.93f, 0.76f, -0.35f);
            AddBox(go, Vector3.zero, new Vector3(0.2f, 0.2f, 0.32f), true);
            go.AddComponent<AudioSource>().playOnAwake = false;
            return go.AddComponent<BribeBox>();
        }

        static BribeBox BuildBribeBox(Transform parent)
        {
            GameObject go;
            var box = BribeBoxRoot(parent, out go);

            var v = Visuals(go.transform);
            Box("Bottom", v, new Vector3(0f, -0.095f, 0f), new Vector3(0.2f, 0.01f, 0.32f), M("BribeWood"));
            Box("Wall_Road", v, new Vector3(0.095f, 0f, 0f), new Vector3(0.01f, 0.2f, 0.32f), M("BribeWood"));
            Box("Wall_Desk", v, new Vector3(-0.095f, 0f, 0f), new Vector3(0.01f, 0.2f, 0.32f), M("BribeWood"));
            Box("Wall_N", v, new Vector3(0f, 0f, 0.155f), new Vector3(0.2f, 0.2f, 0.01f), M("BribeWood"));
            Box("Wall_S", v, new Vector3(0f, 0f, -0.155f), new Vector3(0.2f, 0.2f, 0.01f), M("BribeWood"));

            var rim = Group("Rim", v);
            Box("Rim_Road", rim, new Vector3(0.1f, 0.1f, 0f), new Vector3(0.015f, 0.015f, 0.33f), M("BribeRim"));
            Box("Rim_Desk", rim, new Vector3(-0.1f, 0.1f, 0f), new Vector3(0.015f, 0.015f, 0.33f), M("BribeRim"));
            Box("Rim_N", rim, new Vector3(0f, 0.1f, 0.16f), new Vector3(0.21f, 0.015f, 0.015f), M("BribeRim"));
            var rimS = Box("Rim_S", rim, new Vector3(0f, 0.1f, -0.16f), new Vector3(0.21f, 0.015f, 0.015f), M("BribeRim"));

            // Solid floor so a dropped bundle settles inside instead of falling through.
            var floor = Group("Floor", go.transform);
            AddBox(floor.gameObject, new Vector3(0f, -0.095f, 0f), new Vector3(0.2f, 0.01f, 0.32f));

            Set(box, "rimRenderer", rim.GetChild(0).GetComponent<Renderer>());

            Label("Label", go.transform, new Vector3(-0.15f, 0.125f, 0f), Face(Vector3.up, Vector3.right),
                "ЯЩИК ДЛЯ ВЗЯТОК", 0.28f, new Color(1f, 0.85f, 0.3f), new Vector2(0.3f, 0.05f));
            Label("LabelRoad", go.transform, new Vector3(0.102f, 0f, 0f), Face(Vector3.right, Vector3.up),
                "ЯЩИК ДЛЯ ВЗЯТОК", 0.28f, new Color(1f, 0.85f, 0.3f), new Vector2(0.3f, 0.15f));
            return box;
        }

        // ------------------------------------------------------------- bounds

        /// <summary>Invisible walls framing the InspectionZone so the player cannot leave the shoulders.</summary>
        static void BuildBounds(Transform root)
        {
            const float minX = -4.05f, maxX = 1.85f, minZ = -4.0f, maxZ = 3.0f, h = 3f, t = 0.2f;
            var bounds = Group("Bounds", root);
            var cx = (minX + maxX) * 0.5f;
            var cz = (minZ + maxZ) * 0.5f;
            var w = maxX - minX + 2f * t;
            var d = maxZ - minZ + 2f * t;

            Wall(bounds, "Wall_West", new Vector3(minX - t * 0.5f, h * 0.5f, cz), new Vector3(t, h, d));
            Wall(bounds, "Wall_East", new Vector3(maxX + t * 0.5f, h * 0.5f, cz), new Vector3(t, h, d));
            Wall(bounds, "Wall_South", new Vector3(cx, h * 0.5f, minZ - t * 0.5f), new Vector3(w, h, t));
            Wall(bounds, "Wall_North", new Vector3(cx, h * 0.5f, maxZ + t * 0.5f), new Vector3(w, h, t));
        }

        static void Wall(Transform parent, string name, Vector3 pos, Vector3 size)
        {
            var go = Group(name, parent, pos).gameObject;
            go.layer = 2; // Ignore Raycast: never blocks teleport or UI rays, still blocks the body.
            AddBox(go, Vector3.zero, size);
        }

        /// <summary>Capsule follows the HMD so the body collides with walls and the parked car.</summary>
        static void ConfigureXRBody()
        {
            var origin = GameObject.Find("XR Origin Hands (XR Rig)");
            if (origin == null)
            {
                Debug.LogWarning("[DPS Phase 3] XR Origin Hands (XR Rig) not found - body collision skipped.");
                return;
            }

            var cc = origin.GetComponent<CharacterController>();
            if (cc == null)
                cc = origin.AddComponent<CharacterController>();
            cc.radius = 0.25f;
            cc.height = Mathf.Clamp(cc.height, 0.5f, 2.2f);
            cc.center = new Vector3(cc.center.x, cc.height * 0.5f + cc.skinWidth, cc.center.z);

            // XRI 3.x: XRBodyTransformer already fits the capsule to the HMD every frame; the
            // deprecated CharacterControllerDriver would fight it, so only make sure it is used.
            var body = origin.GetComponentInChildren<UnityEngine.XR.Interaction.Toolkit.Locomotion.XRBodyTransformer>(true);
            if (body != null)
                body.useCharacterControllerIfExists = true;
            else
                Debug.LogWarning("[DPS Phase 3] XRBodyTransformer not found - capsule will not follow the HMD.");

            EditorUtility.SetDirty(origin);
        }

        // ------------------------------------------------------------- boards

        static void BuildStartBoard(Transform parent, ShiftManager shift, out GameObject startRoot,
                                    out GameObject summaryRoot, out TMP_Text summaryText)
        {
            var canvas = WorldCanvas("StartBoard", parent, new Vector3(-2.8f, 1.55f, -1.12f),
                Face(Vector3.forward, Vector3.up), new Vector2(1000f, 820f), true);
            var t = canvas.transform;

            UIPanel("Background", t, Vector2.zero, new Vector2(1000f, 820f), new Color(0.05f, 0.07f, 0.12f, 0.96f));
            UIPanel("TitleBar", t, new Vector2(0f, 350f), new Vector2(1000f, 120f), new Color(0.1f, 0.2f, 0.55f, 1f));
            UIText("Title", t, new Vector2(0f, 350f), new Vector2(960f, 110f), "ТИПИЧНЫЙ ДПС VR", 78f, Color.white);

            var start = new GameObject("StartPanel", typeof(RectTransform));
            start.transform.SetParent(t, false);
            ((RectTransform)start.transform).sizeDelta = new Vector2(1000f, 700f);
            ((RectTransform)start.transform).anchoredPosition = new Vector2(0f, -60f);
            UIText("Intro", start.transform, new Vector2(0f, 130f), new Vector2(900f, 300f),
                "Ночь. Трасса. Полосатая палочка.\nОстанови 5 машин, проверь водителей\nи прими решение: пропустить или задержать.\nВзятки не брать! (Наверное.)",
                40f, new Color(0.9f, 0.92f, 1f));
            UIButton("StartButton", start.transform, new Vector2(0f, -160f), new Vector2(640f, 160f),
                "НАЧАТЬ СМЕНУ", new Color(0.12f, 0.6f, 0.2f), shift.BeginShift);

            var summary = new GameObject("SummaryPanel", typeof(RectTransform));
            summary.transform.SetParent(t, false);
            ((RectTransform)summary.transform).sizeDelta = new Vector2(1000f, 700f);
            ((RectTransform)summary.transform).anchoredPosition = new Vector2(0f, -60f);
            summaryText = UIText("SummaryText", summary.transform, new Vector2(0f, 70f), new Vector2(920f, 520f),
                "ИТОГИ СМЕНЫ\nЗадержано: 0\nОшибок: 0\nВзяток (₽): 0\nВЕРДИКТ: -", 34f, Color.white, TextAlignmentOptions.TopLeft);
            UIButton("NewShiftButton", summary.transform, new Vector2(0f, -270f), new Vector2(560f, 120f),
                "НОВАЯ СМЕНА", new Color(0.15f, 0.35f, 0.75f), shift.BeginShift);
            summary.SetActive(false);

            startRoot = start;
            summaryRoot = summary;
        }

        static void BuildWantedBoard(Transform parent, out TMP_Text plate, out TMP_Text crime, out TMP_Text status)
        {
            var canvas = WorldCanvas("WantedBoard", parent, new Vector3(-1.65f, 2.2f, 0.45f),
                Face(Vector3.left, Vector3.up), new Vector2(900f, 500f), false);
            var t = canvas.transform;

            UIPanel("Background", t, Vector2.zero, new Vector2(900f, 500f), new Color(0.06f, 0.06f, 0.07f, 0.97f));
            UIPanel("Header", t, new Vector2(0f, 200f), new Vector2(900f, 100f), new Color(0.65f, 0.05f, 0.05f, 1f));
            UIText("Title", t, new Vector2(0f, 200f), new Vector2(880f, 95f), "В РОЗЫСКЕ", 70f, Color.white);

            UIPanel("PlateBg", t, new Vector2(0f, 70f), new Vector2(620f, 130f), new Color(0.92f, 0.92f, 0.9f, 1f));
            plate = UIText("WantedPlateText", t, new Vector2(0f, 70f), new Vector2(600f, 120f), "---", 84f, Color.black);
            crime = UIText("WantedCrimeText", t, new Vector2(0f, -60f), new Vector2(860f, 90f), "ОЖИДАНИЕ СМЕНЫ", 46f, new Color(1f, 0.75f, 0.3f));

            UIPanel("StatusBar", t, new Vector2(0f, -195f), new Vector2(900f, 110f), new Color(0.1f, 0.14f, 0.25f, 1f));
            status = UIText("StatusText", t, new Vector2(0f, -195f), new Vector2(880f, 100f), "Машина 1/5   04:00   0 ₽", 50f, Color.white);
        }
    }
}
