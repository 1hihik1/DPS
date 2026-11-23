using System.Collections.Generic;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TextCore.LowLevel;
using UnityEngine.UI;

namespace DPS.EditorTools
{
    /// <summary>
    /// Phase 3: Cyrillic TMP font, gameplay prefabs and every gameplay object in DPS_Game.
    /// Idempotent - rebuilds the DPS_Gameplay root and overwrites the prefabs in place (GUIDs kept).
    /// </summary>
    public static partial class DPSPhase3Builder
    {
        const string ScenePath = "Assets/Scenes/DPS_Game.unity";
        const string FontTtfPath = "Assets/Fonts/Arial_Cyrillic.ttf";
        const string FontAssetPath = "Assets/Fonts/Arial_Cyrillic SDF.asset";
        const string MatFolder = "Assets/Materials/DPS";
        const string PrefabFolder = "Assets/Prefabs";
        const string RootName = "DPS_Gameplay";

        static TMP_FontAsset s_Font;
        static readonly Dictionary<string, Material> Mats = new Dictionary<string, Material>();

        [MenuItem("DPS/Phase 3 - Build Gameplay & Prefabs")]
        public static void BuildFromMenu()
        {
            Build();
        }

        public static void Build()
        {
            EnsureScene();
            s_Font = BuildFontAsset();
            BuildMaterials();
            EnsureFolder(PrefabFolder);

            var cash = BuildCashPrefab();
            var cargo = BuildCargoPrefab();
            var car = BuildCarPrefab(cash, cargo);
            BuildScene(car);
            EnsureBuildSceneFirst();

            AssetDatabase.SaveAssets();
            EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
            Debug.Log("[DPS Phase 3] Font, prefabs and DPS_Gameplay built; scene saved.");
        }

        static void EnsureBuildSceneFirst()
        {
            // Quest boots build index 0; the template SampleScene must never be first.
            var list = new System.Collections.Generic.List<EditorBuildSettingsScene>();
            list.Add(new EditorBuildSettingsScene(ScenePath, true));
            foreach (var s in EditorBuildSettings.scenes)
                if (s.path != ScenePath)
                    list.Add(s);
            EditorBuildSettings.scenes = list.ToArray();
        }

        static void EnsureScene()
        {
            var active = SceneManager.GetActiveScene();
            if (active.path == ScenePath)
                return;

            if (active.isDirty)
                EditorSceneManager.SaveScene(active);

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        // ------------------------------------------------------------------ font

        [MenuItem("DPS/Build Cyrillic TMP Font")]
        public static void BuildFontFromMenu()
        {
            BuildFontAsset();
            AssetDatabase.SaveAssets();
        }

        public static TMP_FontAsset BuildFontAsset()
        {
            var fa = AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(FontAssetPath);
            if (fa != null && (fa.material == null || fa.atlasTexture == null))
            {
                AssetDatabase.DeleteAsset(FontAssetPath);
                fa = null;
            }

            if (fa == null)
            {
                AssetDatabase.ImportAsset(FontTtfPath);
                var font = AssetDatabase.LoadAssetAtPath<Font>(FontTtfPath);
                if (font == null)
                    throw new System.Exception("Missing " + FontTtfPath);

                fa = TMP_FontAsset.CreateFontAsset(font, 64, 6, GlyphRenderMode.SDFAA, 2048, 2048,
                    AtlasPopulationMode.Dynamic, true);
                fa.name = "Arial_Cyrillic SDF";
                AssetDatabase.CreateAsset(fa, FontAssetPath);

                foreach (var tex in fa.atlasTextures)
                {
                    if (tex == null)
                        continue;
                    tex.name = fa.name + " Atlas";
                    AssetDatabase.AddObjectToAsset(tex, fa);
                }

                var mat = fa.material;
                mat.name = fa.name + " Material";
                var mobile = Shader.Find("TextMeshPro/Mobile/Distance Field");
                if (mobile != null)
                    mat.shader = mobile;
                AssetDatabase.AddObjectToAsset(mat, fa);
            }

            // Not public in this TMP version - set the serialized field directly.
            var fso = new SerializedObject(fa);
            var clear = fso.FindProperty("m_ClearDynamicDataOnBuild");
            if (clear != null)
            {
                clear.boolValue = false;
                fso.ApplyModifiedPropertiesWithoutUndo();
            }
            else
            {
                Debug.LogWarning("[DPS Phase 3] m_ClearDynamicDataOnBuild not found on TMP_FontAsset.");
            }

            var sb = new System.Text.StringBuilder();
            for (var c = 0x20; c <= 0x7E; c++) sb.Append((char)c);
            for (var c = 0x400; c <= 0x4FF; c++) sb.Append((char)c);
            sb.Append("\u20BD\u2030\u00AB\u00BB\u2014\u2013\u2026\u2116\u00A0\u00B0");

            string missing;
            fa.TryAddCharacters(sb.ToString(), out missing);
            if (!string.IsNullOrEmpty(missing))
                Debug.Log("[DPS Phase 3] Glyphs not in Arial: " + missing.Length);

            // Atlas textures added by a resize must also live inside the asset.
            foreach (var tex in fa.atlasTextures)
            {
                if (tex == null)
                    continue;
                if (string.IsNullOrEmpty(AssetDatabase.GetAssetPath(tex)))
                    AssetDatabase.AddObjectToAsset(tex, fa);
                EditorUtility.SetDirty(tex);
            }

            EditorUtility.SetDirty(fa);

            var settings = TMP_Settings.instance;
            if (settings != null)
            {
                var sso = new SerializedObject(settings);
                var list = sso.FindProperty("m_fallbackFontAssets");
                if (list != null)
                {
                    var found = false;
                    for (var i = 0; i < list.arraySize; i++)
                        found |= list.GetArrayElementAtIndex(i).objectReferenceValue == fa;

                    if (!found)
                    {
                        list.arraySize++;
                        list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = fa;
                        sso.ApplyModifiedPropertiesWithoutUndo();
                        EditorUtility.SetDirty(settings);
                    }
                }
            }

            AssetDatabase.SaveAssets();
            return fa;
        }

        // ------------------------------------------------------------- materials

        static void BuildMaterials()
        {
            Mats.Clear();
            EnsureFolder(MatFolder);

            Lit("CarBody", Color.white, 0.75f, 0.35f);
            Lit("CarGlass", new Color(0.05f, 0.08f, 0.11f, 0.55f), 0.9f, 0f, transparent: true);
            Lit("CarTrim", new Color(0.04f, 0.04f, 0.045f), 0.35f, 0.1f);
            Lit("Tire", new Color(0.03f, 0.03f, 0.03f), 0.15f, 0f);
            Lit("TrunkCarpet", new Color(0.06f, 0.06f, 0.065f), 0.05f, 0f);
            Lit("PlateWhite", new Color(0.9f, 0.9f, 0.88f), 0.3f, 0f);
            Lit("Skin", new Color(0.8f, 0.6f, 0.45f), 0.3f, 0f);
            Lit("Shirt", new Color(0.18f, 0.25f, 0.55f), 0.2f, 0f);
            Lit("Cardboard", new Color(0.85f, 0.85f, 0.85f), 0.1f, 0f);
            Lit("Cash", new Color(0.35f, 0.6f, 0.35f), 0.2f, 0f, emission: new Color(0.05f, 0.12f, 0.05f));
            Lit("CashBand", new Color(0.95f, 0.9f, 0.7f), 0.2f, 0f);
            Lit("BatonBlack", new Color(0.03f, 0.03f, 0.03f), 0.5f, 0f);
            Lit("BatonWhite", new Color(0.95f, 0.95f, 0.95f), 0.5f, 0f, emission: new Color(0.15f, 0.15f, 0.15f));
            Lit("DeviceYellow", new Color(0.95f, 0.7f, 0.05f), 0.5f, 0f);
            Lit("DeviceDark", new Color(0.08f, 0.08f, 0.09f), 0.5f, 0.3f);
            Lit("Led", new Color(0.15f, 0.15f, 0.15f), 0.6f, 0f, emission: new Color(0.1f, 0.1f, 0.1f));
            Lit("ButtonHousing", new Color(0.12f, 0.12f, 0.13f), 0.5f, 0.6f);
            Lit("ButtonCap", Color.white, 0.6f, 0f);
            Lit("BribeWood", new Color(0.3f, 0.2f, 0.1f), 0.35f, 0f);
            Lit("BribeRim", new Color(0.35f, 0.28f, 0.1f), 0.6f, 0f, emission: new Color(0.35f, 0.28f, 0.1f));
            Lit("ZoneDebug", new Color(1f, 1f, 1f), 0f, 0f);

            Unlit("Headlight", new Color(2.4f, 2.3f, 2.0f));
            Unlit("Taillight", new Color(2.2f, 0.08f, 0.05f));
            Unlit("Lens", new Color(2.5f, 2.4f, 2.0f));
            Unlit("ScreenBg", new Color(0.02f, 0.05f, 0.03f));
        }

        static Material Lit(string name, Color color, float smoothness, float metallic,
                            Color? emission = null, bool transparent = false)
        {
            var mat = LoadOrCreate(name, Shader.Find("Universal Render Pipeline/Lit"));
            mat.SetColor("_BaseColor", color);
            mat.SetFloat("_Smoothness", smoothness);
            mat.SetFloat("_Metallic", metallic);

            if (emission.HasValue)
            {
                mat.EnableKeyword("_EMISSION");
                mat.SetColor("_EmissionColor", emission.Value);
                mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
            }
            else
            {
                mat.DisableKeyword("_EMISSION");
            }

            if (transparent)
            {
                mat.SetFloat("_Surface", 1f);
                mat.SetFloat("_Blend", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.SrcAlpha);
                mat.SetFloat("_DstBlend", (float)BlendMode.OneMinusSrcAlpha);
                mat.SetFloat("_ZWrite", 0f);
                mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = (int)RenderQueue.Transparent;
            }
            else
            {
                mat.SetFloat("_Surface", 0f);
                mat.SetFloat("_SrcBlend", (float)BlendMode.One);
                mat.SetFloat("_DstBlend", (float)BlendMode.Zero);
                mat.SetFloat("_ZWrite", 1f);
                mat.DisableKeyword("_SURFACE_TYPE_TRANSPARENT");
                mat.renderQueue = -1;
            }

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
                throw new System.Exception("URP shader missing for " + name);

            var path = MatFolder + "/M_P3_" + name + ".mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (mat == null)
            {
                mat = new Material(shader) { name = "M_P3_" + name };
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

        static Material M(string key) => Mats[key];

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
    }
}
