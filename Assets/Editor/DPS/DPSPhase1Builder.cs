using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace DPS.EditorTools
{
    /// <summary>
    /// Phase 1: night highway checkpoint environment.
    /// Idempotent - safe to re-run, rebuilds the DPS_World root from scratch.
    /// </summary>
    public static class DPSPhase1Builder
    {
        const string ScenePath = "Assets/Scenes/DPS_Game.unity";
        const string SourceScenePath = "Assets/Scenes/SampleScene.unity";
        const string MatFolder = "Assets/Materials/DPS";

        // Road runs along Z. Carriageway is 3.6 m wide so the 2x2 m booth at X = -2.8
        // sits flush against the curb without standing in a traffic lane.
        const float RoadHalfWidth = 1.8f;
        const float RoadLength = 80f;
        const float BoothX = -2.8f;
        const float BoothSize = 2.5f;
        const float BoothHalf = BoothSize * 0.5f;

        // Inspection zone the player may walk/teleport in: the booth plus the road
        // strip around the stop line, so they can reach the driver window and trunk.
        const float ZoneMinZ = -4f;
        const float ZoneMaxZ = 3f;

        [MenuItem("DPS/Phase 1 - Build Environment")]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static void Build()
        {
            EnsureScene();
            CleanupTemplate();
            BuildMaterials();

            var world = new GameObject("DPS_World").transform;
            BuildHighway(world);
            BuildBooth(world);
            BuildBarrier(world);
            BuildStreetLamp(world);
            BuildRain(world);
            BuildInspectionZone(world);
            ApplyLighting();
            PlaceXrOrigin();
            EnsureInteractionManager();
            RelocateTeleportArea();

            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[DPS Phase 1] Environment built and scene saved.");
        }

        // ------------------------------------------------------------------ scene

        static void EnsureScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path == ScenePath)
                return;

            if (active.isDirty)
                EditorSceneManager.SaveScene(active);

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
                    throw new System.Exception("Could not copy " + SourceScenePath + " to " + ScenePath);
                AssetDatabase.Refresh();
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        /// <summary>Disables template demo content. Never deletes the XR rig or EventSystem.</summary>
        static void CleanupTemplate()
        {
            DisablePath("Environment/Template Environment");
            DisablePath("Environment/Arrows");
            DisablePath("Environment/Torus Cut");
            DisablePath("Environment/Grid");
            DisablePath("Interactables");
            DisablePath("UI");
            DisablePath("Lighting/Directional Light");

            // Stale build from a previous run.
            var old = GameObject.Find("DPS_World");
            if (old != null)
                Object.DestroyImmediate(old);
        }

        static void DisablePath(string path)
        {
            var t = FindPath(path);
            if (t != null)
                t.gameObject.SetActive(false);
        }

        static Transform FindPath(string path)
        {
            var current = (Transform)null;
            foreach (var root in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                if (root.name == path.Split('/')[0])
                {
                    current = root.transform;
                    break;
                }
            }
            if (current == null)
                return null;

            var parts = path.Split('/');
            for (var i = 1; i < parts.Length; i++)
            {
                current = current.Find(parts[i]);
                if (current == null)
                    return null;
            }
            return current;
        }

        // -------------------------------------------------------------- materials

        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        static void BuildMaterials()
        {
            Mats.Clear();
            EnsureFolder("Assets/Materials/DPS");

            Lit("Asphalt",        new Color(0.045f, 0.048f, 0.055f), 0.85f, 0.15f);
            Lit("RoadPaint",      new Color(0.82f, 0.82f, 0.78f),    0.55f, 0.0f);
            Lit("Ground",         new Color(0.085f, 0.08f, 0.068f),  0.15f, 0.0f);
            Lit("Concrete",       new Color(0.16f, 0.16f, 0.17f),    0.28f, 0.0f);
            Lit("BoothMetal",     new Color(0.105f, 0.135f, 0.185f), 0.60f, 0.70f);
            Lit("DeskWood",       new Color(0.20f, 0.13f, 0.08f),    0.45f, 0.0f);
            Lit("BarrierRed",     new Color(0.55f, 0.05f, 0.05f),    0.50f, 0.0f);
            Lit("BarrierWhite",   new Color(0.85f, 0.85f, 0.85f),    0.50f, 0.0f);
            Lit("PoleMetal",      new Color(0.115f, 0.115f, 0.125f), 0.50f, 0.80f);
            Lit("Rubber",         new Color(0.03f, 0.03f, 0.035f),   0.20f, 0.0f);

            Unlit("EmissiveWarm", new Color(2.2f, 1.75f, 1.10f));
            Unlit("BeaconRed",    new Color(3.0f, 0.10f, 0.08f));
            Unlit("BeaconBlue",   new Color(0.12f, 0.35f, 3.0f));
            Unlit("Reflector",    new Color(1.6f, 1.45f, 0.70f));
            Unlit("BoothGlass",   new Color(0.04f, 0.065f, 0.10f));

            // Rain streaks need alpha blending.
            var rain = Shader.Find("Universal Render Pipeline/Particles/Unlit");
            if (rain == null)
                rain = Shader.Find("Universal Render Pipeline/Unlit");
            var rainMat = LoadOrCreate("RainParticle", rain);
            rainMat.SetColor("_BaseColor", new Color(0.62f, 0.72f, 0.92f, 0.45f));
            MakeTransparent(rainMat);
            Mats["RainParticle"] = rainMat;
        }

        /// <summary>Creates every missing level of a folder path, one segment at a time.</summary>
        static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder))
                return;

            var parts = folder.Split('/');
            var current = parts[0];
            for (var i = 1; i < parts.Length; i++)
            {
                var next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        static Material Lit(string name, Color color, float smoothness, float metallic)
        {
            var mat = LoadOrCreate(name, Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);
            mat.SetFloat("_Surface", 0f);
            Mats[name] = mat;
            return mat;
        }

        static Material Unlit(string name, Color color)
        {
            var mat = LoadOrCreate(name, Shader.Find("Universal Render Pipeline/Unlit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Surface", 0f);
            Mats[name] = mat;
            return mat;
        }

        static Material LoadOrCreate(string name, Shader shader)
        {
            if (shader == null)
                throw new System.Exception("Required URP shader not found for material " + name);

            var path = MatFolder + "/M_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = "M_" + name };
                AssetDatabase.CreateAsset(mat, path);
            }
            else if (mat.shader != shader)
            {
                mat.shader = shader;
            }

            mat.enableInstancing = true;
            EditorUtility.SetDirty(mat);
            return mat;
        }

        static void MakeTransparent(Material mat)
        {
            mat.SetFloat("_Surface", 1f);
            mat.SetFloat("_Blend", 0f);
            mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
            mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
            mat.SetFloat("_ZWrite", 0f);
            mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
            mat.DisableKeyword("_ALPHATEST_ON");
            mat.renderQueue = (int)RenderQueue.Transparent;
            EditorUtility.SetDirty(mat);
        }

        static Material M(string key) => Mats[key];

        // --------------------------------------------------------------- geometry

        static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat,
                              bool keepCollider = false, Vector3? localEuler = null)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = size;
            if (localEuler.HasValue)
                go.transform.localEulerAngles = localEuler.Value;

            var col = go.GetComponent<Collider>();
            if (col != null && !keepCollider)
                Object.DestroyImmediate(col);

            var r = go.GetComponent<Renderer>();
            if (r != null && mat != null)
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            return go;
        }

        static Transform Group(string name, Transform parent, Vector3 localPos = default)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            return go.transform;
        }

        static Transform Visuals(Transform parent, Vector3 localPos = default) => Group("Visuals", parent, localPos);

        // --------------------------------------------------------------- highway

        static void BuildHighway(Transform world)
        {
            var root = Group("Highway", world);
            var v = Visuals(root);

            // Carriageway, top surface at Y = 0.
            Box("Asphalt", v, new Vector3(0f, -0.05f, 0f), new Vector3(RoadHalfWidth * 2f, 0.1f, RoadLength), M("Asphalt"), true);

            // Curb strips.
            Box("Curb_Left", v, new Vector3(-RoadHalfWidth - 0.05f, -0.02f, 0f), new Vector3(0.1f, 0.14f, RoadLength), M("Concrete"));
            Box("Curb_Right", v, new Vector3(RoadHalfWidth + 0.05f, -0.02f, 0f), new Vector3(0.1f, 0.14f, RoadLength), M("Concrete"));

            // Shoulders. Colliders are required: the inspection zone lets the player walk and
            // teleport here, so every surface inside it must actually support them.
            // Centres are chosen so the shoulder edge abuts the road edge with no gap.
            Box("Shoulder_Left", v, new Vector3(-RoadHalfWidth - 1.2f, -0.06f, 0f), new Vector3(2.4f, 0.08f, RoadLength), M("Ground"), true);
            Box("Shoulder_Right", v, new Vector3(RoadHalfWidth + 1.2f, -0.06f, 0f), new Vector3(2.4f, 0.08f, RoadLength), M("Ground"), true);

            // Stop line at Z = 0.
            Box("StopLine", v, new Vector3(0f, 0.011f, 0f), new Vector3(RoadHalfWidth * 2f, 0.02f, 0.35f), M("RoadPaint"));

            // Edge lines.
            Box("EdgeLine_Left", v, new Vector3(-RoadHalfWidth + 0.18f, 0.011f, 0f), new Vector3(0.1f, 0.02f, RoadLength), M("RoadPaint"));
            Box("EdgeLine_Right", v, new Vector3(RoadHalfWidth - 0.18f, 0.011f, 0f), new Vector3(0.1f, 0.02f, RoadLength), M("RoadPaint"));

            // Dashed centre line.
            var dashes = Group("CentreLine", v);
            for (var z = -36f; z <= 36f; z += 6f)
            {
                if (Mathf.Abs(z) < 1.2f)
                    continue; // keep the stop line readable
                Box("Dash", dashes, new Vector3(0f, 0.011f, z), new Vector3(0.12f, 0.02f, 2.4f), M("RoadPaint"));
            }

            // Roadside reflector posts for night depth.
            var posts = Group("ReflectorPosts", v);
            for (var i = 0; i < 6; i++)
            {
                var z = -30f + i * 12f;
                Box("Post", posts, new Vector3(RoadHalfWidth + 0.6f, 0.45f, z), new Vector3(0.07f, 0.9f, 0.07f), M("PoleMetal"));
                Box("Reflector", posts, new Vector3(RoadHalfWidth + 0.6f, 0.86f, z), new Vector3(0.09f, 0.1f, 0.09f), M("Reflector"));
            }
        }

        // ----------------------------------------------------------------- booth

        static void BuildBooth(Transform world)
        {
            var root = Group("Booth", world, new Vector3(BoothX, 0f, 0f));
            var v = Visuals(root);

            // Top face flush with the road surface so the CharacterController never steps up.
            Box("Base", v, new Vector3(0f, -0.01f, 0f), new Vector3(BoothSize, 0.02f, BoothSize), M("Concrete"), true);

            // Back and one side are enclosed. The front is completely open (no wall, no glass,
            // no posts) and the +Z side has a doorway, so the player can walk straight out to
            // the stopped car without climbing over or squeezing past anything.
            Box("BackWall", v, new Vector3(-1.20f, 1.2f, 0f), new Vector3(0.1f, 2.4f, BoothSize), M("BoothMetal"), true);
            Box("SideWall_Closed", v, new Vector3(0f, 1.2f, -1.20f), new Vector3(BoothSize, 2.4f, 0.1f), M("BoothMetal"), true);
            Box("SideWall_Doorway", v, new Vector3(-0.55f, 1.2f, 1.20f), new Vector3(1.4f, 2.4f, 0.1f), M("BoothMetal"), true);
            Box("Roof", v, new Vector3(0f, 2.42f, 0f), new Vector3(2.7f, 0.12f, 2.7f), M("BoothMetal"), true);

            // Ceiling light fixture.
            Box("LampFixture", v, new Vector3(0f, 2.34f, 0f), new Vector3(0.6f, 0.03f, 0.6f), M("EmissiveWarm"));

            // Police beacon on the roof.
            var beacon = Group("Beacon", v, new Vector3(0f, 2.62f, 0f));
            var bv = Visuals(beacon);
            Box("BeaconBase", bv, new Vector3(0f, -0.06f, 0f), new Vector3(0.44f, 0.06f, 0.26f), M("PoleMetal"));
            Box("BeaconRed", bv, new Vector3(0f, 0f, 0.11f), new Vector3(0.28f, 0.16f, 0.16f), M("BeaconRed"));
            Box("BeaconBlue", bv, new Vector3(0f, 0f, -0.11f), new Vector3(0.28f, 0.16f, 0.16f), M("BeaconBlue"));

            BuildDesk(root);
        }

        static void BuildDesk(Transform booth)
        {
            // Desk runs along the open front but stops short of the +Z doorway, leaving a
            // clear walking lane out of the booth toward the driver window.
            var desk = Group("Desk", booth, new Vector3(0.5f, 0f, -0.325f));
            var v = Visuals(desk);

            Box("DeskTop", v, new Vector3(0f, 0.85f, 0f), new Vector3(0.55f, 0.06f, 1.35f), M("DeskWood"), true);
            Box("DeskCabinet", v, new Vector3(-0.02f, 0.42f, 0f), new Vector3(0.46f, 0.78f, 1.25f), M("BoothMetal"), true);
            Box("DeskKick", v, new Vector3(0f, 0.03f, 0f), new Vector3(0.5f, 0.06f, 1.3f), M("Rubber"));
        }

        /// <summary>
        /// Teleport/locomotion surface covering the booth plus the road strip around the stop
        /// line. Trigger-only, so it never collides with the CharacterController, and its top
        /// face sits a hair above the road so teleport rays hit it instead of the asphalt.
        /// </summary>
        static void BuildInspectionZone(Transform world)
        {
            var root = Group("InspectionZone", world, new Vector3(0f, 0f, 0f));

            var minX = BoothX - BoothHalf;
            var maxX = RoadHalfWidth;
            var width = maxX - minX;
            var depth = ZoneMaxZ - ZoneMinZ;

            var zone = root.gameObject;
            zone.transform.localPosition = new Vector3(minX + width * 0.5f, 0.0025f, ZoneMinZ + depth * 0.5f);

            var box = zone.AddComponent<BoxCollider>();
            box.size = new Vector3(width, 0.005f, depth);
            box.center = Vector3.zero;
            box.isTrigger = true;

            var area = zone.AddComponent<UnityEngine.XR.Interaction.Toolkit.Locomotion.Teleportation.TeleportationArea>();
            area.interactionLayers = -1;

            // Teleport rays ignore triggers by default, which would make this area unusable.
            foreach (var ray in Object.FindObjectsByType<UnityEngine.XR.Interaction.Toolkit.Interactors.XRRayInteractor>(FindObjectsSortMode.None))
                ray.raycastTriggerInteraction = QueryTriggerInteraction.Collide;
        }

        // --------------------------------------------------------------- barrier

        static void BuildBarrier(Transform world)
        {
            // Barrier at Z = +3.5, hinge on the right-hand post.
            var root = Group("Barrier", world, new Vector3(0f, 0f, 3.5f));
            var v = Visuals(root);

            Box("Post_Base", v, new Vector3(RoadHalfWidth + 0.15f, 0.15f, 0f), new Vector3(0.4f, 0.3f, 0.4f), M("Concrete"), true);
            Box("Post", v, new Vector3(RoadHalfWidth + 0.15f, 0.75f, 0f), new Vector3(0.16f, 1.2f, 0.16f), M("PoleMetal"), true);

            var pivot = Group("ArmPivot", root, new Vector3(RoadHalfWidth + 0.15f, 1.0f, 0f));
            var av = Visuals(pivot);
            // Arm extends across the carriageway in -X.
            Box("Arm", av, new Vector3(-1.85f, 0f, 0f), new Vector3(3.6f, 0.12f, 0.16f), M("BarrierWhite"));
            Box("Stripe_A", av, new Vector3(-0.55f, 0f, 0f), new Vector3(0.5f, 0.14f, 0.18f), M("BarrierRed"));
            Box("Stripe_B", av, new Vector3(-1.55f, 0f, 0f), new Vector3(0.5f, 0.14f, 0.18f), M("BarrierRed"));
            Box("Stripe_C", av, new Vector3(-2.55f, 0f, 0f), new Vector3(0.5f, 0.14f, 0.18f), M("BarrierRed"));
        }

        // ------------------------------------------------------------ streetlamp

        static void BuildStreetLamp(Transform world)
        {
            var root = Group("StreetLamp", world, new Vector3(RoadHalfWidth + 1.6f, 0f, 6f));
            var v = Visuals(root);

            Box("Pole", v, new Vector3(0f, 3.25f, 0f), new Vector3(0.14f, 6.5f, 0.14f), M("PoleMetal"));
            Box("Arm", v, new Vector3(-1.1f, 6.4f, 0f), new Vector3(2.2f, 0.1f, 0.1f), M("PoleMetal"));
            Box("Head", v, new Vector3(-2.05f, 6.3f, 0f), new Vector3(0.7f, 0.14f, 0.34f), M("EmissiveWarm"));
        }

        // ------------------------------------------------------------------ rain

        static void BuildRain(Transform world)
        {
            var go = new GameObject("Rain");
            go.transform.SetParent(world, false);
            go.transform.localPosition = new Vector3(0f, 8f, 0f);

            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop();

            var main = ps.main;
            main.loop = true;
            main.duration = 1f;
            main.startLifetime = 0.9f;
            main.startSpeed = 16f;
            main.startSize = 0.015f;
            main.startColor = new Color(0.62f, 0.72f, 0.92f, 0.45f);
            main.maxParticles = 150;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            main.gravityModifier = 0.25f;

            var emission = ps.emission;
            emission.rateOverTime = 160f;

            var shape = ps.shape;
            shape.enabled = true;
            shape.shapeType = ParticleSystemShapeType.Box;
            // Offset outward in +X so rain never spawns above the booth roof,
            // otherwise streaks visibly fall through the ceiling into the interior.
            shape.position = new Vector3(5f, 0f, 0f);
            shape.scale = new Vector3(12f, 0.1f, 24f);
            shape.rotation = new Vector3(90f, 0f, 0f);

            var collision = ps.collision;
            collision.enabled = false; // no particle collisions on Quest

            var renderer = go.GetComponent<ParticleSystemRenderer>();
            renderer.renderMode = ParticleSystemRenderMode.Stretch;
            renderer.lengthScale = 5f;
            renderer.velocityScale = 0.06f;
            renderer.sharedMaterial = M("RainParticle");
            renderer.shadowCastingMode = ShadowCastingMode.Off;
            renderer.receiveShadows = false;

            ps.Play();
        }

        // -------------------------------------------------------------- lighting

        static void ApplyLighting()
        {
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Exponential;
            RenderSettings.fogDensity = 0.022f;
            RenderSettings.fogColor = new Color(0.028f, 0.036f, 0.055f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.035f, 0.045f, 0.075f);
            RenderSettings.skybox = null;

            // Exactly three real-time lights, none of them cast shadows.
            RemoveStrayLights();

            var world = FindPath("DPS_World");
            var booth = world.Find("Booth");

            // 1. Warm booth light.
            PointLight("BoothLight", booth, new Vector3(0f, 2.15f, 0f),
                new Color(1f, 0.78f, 0.5f), 2.6f, 6f);

            // 2. Cold street lamp.
            var lamp = world.Find("StreetLamp");
            SpotLight("StreetLampLight", lamp, new Vector3(-2.05f, 6.15f, 0f),
                new Color(0.58f, 0.72f, 1f), 7f, 18f, 80f);

            // Police beacon sits over the roof centre.
            PointLight("BeaconLight", booth, new Vector3(0f, 2.85f, 0f),
                new Color(1f, 0.1f, 0.08f), 0f, 9f);

            // Night camera clear.
            var cam = Object.FindAnyObjectByType<Camera>();
            if (cam != null)
            {
                cam.clearFlags = CameraClearFlags.SolidColor;
                cam.backgroundColor = new Color(0.012f, 0.016f, 0.028f, 1f);
                cam.farClipPlane = 150f;
            }
        }

        static void RemoveStrayLights()
        {
            // Only the three lights we create below may stay active.
            foreach (var light in Object.FindObjectsByType<Light>(FindObjectsSortMode.None))
            {
                if (light.gameObject.name == "BoothLight" ||
                    light.gameObject.name == "StreetLampLight" ||
                    light.gameObject.name == "BeaconLight")
                    continue;
                light.gameObject.SetActive(false);
            }
        }

        static void PointLight(string name, Transform parent, Vector3 localPos, Color color, float intensity, float range)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;

            var light = go.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
        }

        static void SpotLight(string name, Transform parent, Vector3 localPos, Color color, float intensity, float range, float angle)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);

            var light = go.AddComponent<Light>();
            light.type = LightType.Spot;
            light.color = color;
            light.intensity = intensity;
            light.range = range;
            light.spotAngle = angle;
            light.innerSpotAngle = angle * 0.45f;
            light.shadows = LightShadows.None;
            light.renderMode = LightRenderMode.Auto;
        }

        // ------------------------------------------------------------------- rig

        static void PlaceXrOrigin()
        {
            var origin = FindPath("XR Origin Hands (XR Rig)");
            if (origin == null)
            {
                Debug.LogWarning("[DPS Phase 1] XR Origin not found - skipped repositioning.");
                return;
            }

            origin.position = new Vector3(BoothX, 0f, 0f);
            origin.rotation = Quaternion.Euler(0f, 90f, 0f); // face +X, toward the road
        }

        static void EnsureInteractionManager()
        {
            if (Object.FindAnyObjectByType<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>() != null)
                return;

            var go = new GameObject("XR Interaction Manager");
            go.AddComponent<UnityEngine.XR.Interaction.Toolkit.XRInteractionManager>();
            Debug.Log("[DPS Phase 1] No XR Interaction Manager in scene - added one.");
        }

        static void RelocateTeleportArea()
        {
            // The template's own "Teleport Area" object carries no collider (the Floor supplied
            // the surface), so it is left parked out of the play zone. Teleporting is handled by
            // the InspectionZone TeleportationArea built above.
            var area = FindPath("Teleport Area Setup/Teleport Area");
            if (area != null)
                area.gameObject.SetActive(false);

            // Keep the template's anchor as the default landing spot inside the booth, and make
            // sure it does not sit on a trigger the teleport ray would hit first.
            var anchor = FindPath("Teleport Area Setup/Teleport Anchor");
            if (anchor == null)
                return;

            anchor.position = new Vector3(BoothX, 0.03f, 0f);
            anchor.rotation = Quaternion.identity;

            foreach (var col in anchor.GetComponentsInChildren<Collider>(true))
                col.enabled = false;
        }
    }
}
