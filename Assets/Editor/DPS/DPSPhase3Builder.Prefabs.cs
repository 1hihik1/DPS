using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DPS.EditorTools
{
    public static partial class DPSPhase3Builder
    {
        // --------------------------------------------------------- cash bundle

        static CashBundle BuildCashPrefab()
        {
            var root = new GameObject("CashBundle");
            var v = Visuals(root.transform);
            Box("Stack", v, new Vector3(0f, 0f, 0f), new Vector3(0.16f, 0.035f, 0.075f), M("Cash"));
            Box("Band", v, new Vector3(0f, 0f, 0f), new Vector3(0.03f, 0.037f, 0.077f), M("CashBand"));
            Label("Label", root.transform, new Vector3(0f, 0.0185f, 0f), Face(Vector3.up, Vector3.forward),
                "ВЗЯТКА 5000₽", 0.16f, new Color(1f, 0.95f, 0.6f), new Vector2(0.15f, 0.06f));

            AddBox(root, Vector3.zero, new Vector3(0.16f, 0.035f, 0.075f));

            var body = root.AddComponent<Rigidbody>();
            body.mass = 0.2f;
            body.isKinematic = true;
            body.useGravity = true;
            body.interpolation = RigidbodyInterpolation.Interpolate;

            root.AddComponent<AudioSource>().playOnAwake = false;

            var grab = root.AddComponent<XRGrabInteractable>();
            GrabTuning.Apply(grab);

            var cash = root.AddComponent<CashBundle>();
            Set(cash, "grabInteractable", grab);

            return SavePrefab(root, "CashBundle").GetComponent<CashBundle>();
        }

        // ----------------------------------------------------------- cargo box

        static GameObject BuildCargoPrefab()
        {
            var size = new Vector3(0.35f, 0.25f, 0.3f);
            var root = new GameObject("CargoBox");
            var v = Visuals(root.transform);
            Box("Crate", v, Vector3.zero, size, M("Cardboard"));
            Box("Tape", v, new Vector3(0f, size.y * 0.5f + 0.001f, 0f), new Vector3(0.06f, 0.002f, size.z + 0.002f), M("CashBand"));

            Label("LabelTop", root.transform, new Vector3(0f, size.y * 0.5f + 0.004f, 0f), Face(Vector3.up, Vector3.right),
                "КОРОБКА", 0.4f, Color.black, new Vector2(0.28f, 0.1f));
            Label("LabelSide", root.transform, new Vector3(0f, 0f, -size.z * 0.5f - 0.003f), Face(Vector3.back, Vector3.up),
                "КОРОБКА", 0.4f, Color.black, new Vector2(0.32f, 0.1f));
            Label("LabelSide2", root.transform, new Vector3(-size.x * 0.5f - 0.003f, 0f, 0f), Face(Vector3.left, Vector3.up),
                "КОРОБКА", 0.35f, Color.black, new Vector2(0.28f, 0.1f));

            AddBox(root, Vector3.zero, size);

            var body = root.AddComponent<Rigidbody>();
            body.mass = 3f;
            body.useGravity = true;
            body.isKinematic = false;

            var grab = root.AddComponent<XRGrabInteractable>();
            GrabTuning.Apply(grab);
            GrabTuning.MakeHoldable(grab, body);

            return SavePrefab(root, "CargoBox");
        }

        // ----------------------------------------------------------------- car

        // Car faces +Z. Driver window on -X (left-hand drive), trunk at -Z.
        // Length 4.2 (Z -2.1..2.1), width 1.8 (X -0.9..0.9). Trunk occupies Z -2.1..-1.2.
        static CarController BuildCarPrefab(CashBundle cash, GameObject cargo)
        {
            var root = new GameObject("CarPrefab");
            var v = Visuals(root.transform);

            var body = Box("CarBody", v, new Vector3(0f, 0.625f, 0.45f), new Vector3(1.8f, 0.65f, 3.3f), M("CarBody"));

            // Trunk shell: floor, side walls, rear wall, bulkhead. Hollow inside.
            var trunkFloor = Box("TrunkFloor", v, new Vector3(0f, 0.375f, -1.65f), new Vector3(1.8f, 0.15f, 0.9f), M("CarBody"));
            var trunkL = Box("TrunkWall_L", v, new Vector3(-0.87f, 0.7f, -1.65f), new Vector3(0.06f, 0.5f, 0.9f), M("CarBody"));
            var trunkR = Box("TrunkWall_R", v, new Vector3(0.87f, 0.7f, -1.65f), new Vector3(0.06f, 0.5f, 0.9f), M("CarBody"));
            var trunkRear = Box("TrunkWall_Rear", v, new Vector3(0f, 0.7f, -2.07f), new Vector3(1.8f, 0.5f, 0.06f), M("CarBody"));
            Box("TrunkCarpet", v, new Vector3(0f, 0.452f, -1.65f), new Vector3(1.68f, 0.004f, 0.78f), M("TrunkCarpet"));

            // Cabin.
            var roof = Box("Roof", v, new Vector3(0f, 1.42f, -0.05f), new Vector3(1.62f, 0.05f, 1.95f), M("CarBody"));
            var pillars = new System.Collections.Generic.List<Renderer> { trunkFloor.GetComponent<Renderer>(), trunkL.GetComponent<Renderer>(),
                trunkR.GetComponent<Renderer>(), trunkRear.GetComponent<Renderer>(), roof.GetComponent<Renderer>() };
            foreach (var sx in new[] { -0.8f, 0.8f })
            {
                foreach (var z in new[] { 0.95f, -0.03f, -1.03f })
                    pillars.Add(Box("Pillar", v, new Vector3(sx, 1.18f, z), new Vector3(0.05f, 0.48f, 0.05f), M("CarBody")).GetComponent<Renderer>());
            }

            Box("Windshield", v, new Vector3(0f, 1.18f, 1.1f), new Vector3(1.58f, 0.55f, 0.02f), M("CarGlass"), new Vector3(-35f, 0f, 0f));
            Box("RearWindow", v, new Vector3(0f, 1.18f, -1.12f), new Vector3(1.58f, 0.5f, 0.02f), M("CarGlass"), new Vector3(30f, 0f, 0f));
            Box("Window_FR", v, new Vector3(0.81f, 1.165f, 0.46f), new Vector3(0.02f, 0.43f, 0.92f), M("CarGlass"));
            Box("Window_RR", v, new Vector3(0.81f, 1.165f, -0.53f), new Vector3(0.02f, 0.43f, 0.95f), M("CarGlass"));
            Box("Window_RL", v, new Vector3(-0.81f, 1.165f, -0.53f), new Vector3(0.02f, 0.43f, 0.95f), M("CarGlass"));

            var window = Box("DriverWindow", v, new Vector3(-0.81f, 1.165f, 0.46f), new Vector3(0.02f, 0.43f, 0.92f), M("CarGlass"));

            // Wheels and lamps (emissive only - no extra real-time lights).
            foreach (var sx in new[] { -0.82f, 0.82f })
                foreach (var z in new[] { 1.35f, -1.35f })
                    Cyl("Wheel", v, new Vector3(sx, 0.3f, z), 0.6f, 0.24f, M("Tire"), new Vector3(0f, 0f, 90f));

            foreach (var sx in new[] { -0.6f, 0.6f })
            {
                Box("Headlight", v, new Vector3(sx, 0.75f, 2.105f), new Vector3(0.36f, 0.12f, 0.02f), M("Headlight"));
                Box("Taillight", v, new Vector3(sx * 1.08f, 0.82f, -2.105f), new Vector3(0.3f, 0.1f, 0.02f), M("Taillight"));
            }

            Box("Bumper_F", v, new Vector3(0f, 0.4f, 2.12f), new Vector3(1.84f, 0.14f, 0.06f), M("CarTrim"));
            Box("Bumper_R", v, new Vector3(0f, 0.4f, -2.12f), new Vector3(1.84f, 0.14f, 0.06f), M("CarTrim"));

            // Driver.
            Box("Driver_Torso", v, new Vector3(-0.4f, 1.0f, 0.3f), new Vector3(0.4f, 0.45f, 0.25f), M("Shirt"));
            Prim(PrimitiveType.Sphere, "Driver_Head", v, new Vector3(-0.4f, 1.3f, 0.32f), Vector3.one * 0.22f, M("Skin"));
            Cyl("SteeringWheel", v, new Vector3(-0.4f, 1.05f, 0.62f), 0.32f, 0.03f, M("CarTrim"), new Vector3(70f, 0f, 0f));

            // Trunk lid hinged at the front edge of the trunk; +75 deg about X lifts the rear edge.
            var lidPivot = Group("TrunkLidPivot", root.transform, new Vector3(0f, 0.97f, -1.2f));
            var lidVis = Visuals(lidPivot);
            var lid = Box("TrunkLid", lidVis, new Vector3(0f, 0f, -0.45f), new Vector3(1.78f, 0.04f, 0.92f), M("CarBody"));

            // Plates.
            Box("PlateFront", v, new Vector3(0f, 0.55f, 2.16f), new Vector3(0.52f, 0.12f, 0.015f), M("PlateWhite"));
            Box("PlateRear", v, new Vector3(0f, 0.55f, -2.16f), new Vector3(0.52f, 0.12f, 0.015f), M("PlateWhite"));
            var frontPlate = Label("FrontPlateText", root.transform, new Vector3(0f, 0.55f, 2.169f), Face(Vector3.forward, Vector3.up),
                "А000АА 77", 0.75f, Color.black, new Vector2(0.5f, 0.11f));
            var rearPlate = Label("RearPlateText", root.transform, new Vector3(0f, 0.55f, -2.169f), Face(Vector3.back, Vector3.up),
                "А000АА 77", 0.75f, Color.black, new Vector2(0.5f, 0.11f));

            // Colliders: solid front/cabin block plus a hollow trunk tub.
            AddBox(root, new Vector3(0f, 0.875f, 0.45f), new Vector3(1.8f, 1.15f, 3.3f));
            var interior = Group("TrunkInterior", root.transform);
            AddBox(Group("Floor", interior).gameObject, new Vector3(0f, 0.375f, -1.65f), new Vector3(1.8f, 0.15f, 0.9f));
            AddBox(Group("Wall_L", interior).gameObject, new Vector3(-0.87f, 0.7f, -1.65f), new Vector3(0.06f, 0.5f, 0.9f));
            AddBox(Group("Wall_R", interior).gameObject, new Vector3(0.87f, 0.7f, -1.65f), new Vector3(0.06f, 0.5f, 0.9f));
            AddBox(Group("Wall_Rear", interior).gameObject, new Vector3(0f, 0.7f, -2.07f), new Vector3(1.8f, 0.5f, 0.06f));

            var cargoPoints = new Transform[3];
            for (var i = 0; i < 3; i++)
                cargoPoints[i] = Group("CargoPoint_" + i, interior, new Vector3(-0.5f + 0.5f * i, 0.6f, -1.85f));
            var contrabandPoint = Group("ContrabandPoint", interior, new Vector3(0f, 0.62f, -1.44f));

            // Trunk handle zone at the rear, below the lid edge.
            var handle = Group("TrunkHandleZone", root.transform, new Vector3(0f, 0.86f, -2.13f));
            var hv = Visuals(handle);
            Box("Handle", hv, Vector3.zero, new Vector3(0.3f, 0.04f, 0.03f), M("CarTrim"));
            AddBox(handle.gameObject, Vector3.zero, new Vector3(0.5f, 0.16f, 0.1f));
            handle.gameObject.AddComponent<AudioSource>().playOnAwake = false;
            var trunk = handle.gameObject.AddComponent<TrunkInteractable>();
            Set(trunk, "lidPivot", lidPivot);
            SetV3(trunk, "openEuler", new Vector3(75f, 0f, 0f));
            handle.gameObject.AddComponent<XRSimpleInteractable>();

            // Anchors.
            var breath = Group("DriverBreathZone", root.transform, new Vector3(-1.05f, 1.15f, 0.45f));
            var cashSpawn = Group("CashSpawn", root.transform, new Vector3(-1.0f, 1.0f, 0.45f), new Vector3(0f, 90f, 0f));

            // Dialogue canvas above the car, facing the booth (-X).
            var canvas = WorldCanvas("DialogueCanvas", root.transform, new Vector3(-0.7f, 2.0f, 0.4f),
                Face(Vector3.left, Vector3.up), new Vector2(1100f, 260f), false);
            UIPanel("Bubble", canvas.transform, Vector2.zero, new Vector2(1100f, 200f), new Color(0.97f, 0.97f, 0.92f, 0.92f));
            var dialogue = UIText("DialogueText", canvas.transform, Vector2.zero, new Vector2(1040f, 190f),
                "Добрый вечер, командир.", 56f, new Color(0.08f, 0.08f, 0.1f));

            var bribe = UIPanel("BribeBanner", canvas.transform, new Vector2(0f, 175f), new Vector2(900f, 120f), new Color(0.1f, 0.45f, 0.12f, 0.95f));
            UIText("BribeText", bribe.transform, Vector2.zero, new Vector2(880f, 110f),
                "ВЗЯТКА 5000 ₽ - в ящик для взяток?", 48f, new Color(1f, 0.95f, 0.5f));

            // Root components.
            var rb = root.AddComponent<Rigidbody>();
            rb.isKinematic = true;
            rb.useGravity = false;
            root.AddComponent<AudioSource>().playOnAwake = false;

            var car = root.AddComponent<CarController>();
            Set(car, "visuals", v);
            Set(car, "bodyRenderer", body.GetComponent<Renderer>());
            SetArr(car, "bodyPanels", pillars.ToArray());
            SetArr(car, "extraBodyRenderers", new Object[] { lid.GetComponent<Renderer>() });
            Set(car, "driverWindow", window.transform);
            SetV3(car, "windowClosedLocalPos", new Vector3(-0.81f, 1.165f, 0.46f));
            SetV3(car, "windowOpenLocalPos", new Vector3(-0.81f, 0.76f, 0.46f));
            Set(car, "frontPlate", frontPlate);
            Set(car, "rearPlate", rearPlate);
            Set(car, "dialogueText", dialogue);
            Set(car, "dialogueRoot", canvas.gameObject);
            Set(car, "bribeRoot", bribe.gameObject);
            Set(car, "breathZone", breath);
            Set(car, "cashSpawn", cashSpawn);
            Set(car, "trunk", trunk);
            Set(car, "cashPrefab", cash);

            var spawner = root.AddComponent<TrunkCargoSpawner>();
            Set(spawner, "car", car);
            Set(spawner, "trunk", trunk);
            Set(spawner, "cargoPrefab", cargo);
            SetArr(spawner, "cargoPoints", cargoPoints);
            Set(spawner, "contrabandPoint", contrabandPoint);

            canvas.gameObject.SetActive(false);

            return SavePrefab(root, "CarPrefab").GetComponent<CarController>();
        }
    }
}
