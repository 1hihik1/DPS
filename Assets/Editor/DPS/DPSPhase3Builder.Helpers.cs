using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

namespace DPS.EditorTools
{
    public static partial class DPSPhase3Builder
    {
        // -------------------------------------------------------------- geometry

        static GameObject Prim(PrimitiveType type, string name, Transform parent, Vector3 localPos, Vector3 scale,
                               Material mat, Vector3? euler = null, bool keepCollider = false)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localScale = scale;
            if (euler.HasValue)
                go.transform.localEulerAngles = euler.Value;

            var col = go.GetComponent<Collider>();
            if (col != null && !keepCollider)
                Object.DestroyImmediate(col);

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                if (mat != null)
                    r.sharedMaterial = mat;
                r.shadowCastingMode = ShadowCastingMode.Off;
                r.receiveShadows = false;
            }

            return go;
        }

        static GameObject Box(string name, Transform parent, Vector3 localPos, Vector3 size, Material mat,
                              Vector3? euler = null)
            => Prim(PrimitiveType.Cube, name, parent, localPos, size, mat, euler);

        /// <summary>Cylinder sized by diameter and full length; default axis is local Y.</summary>
        static GameObject Cyl(string name, Transform parent, Vector3 localPos, float diameter, float length,
                              Material mat, Vector3? euler = null)
            => Prim(PrimitiveType.Cylinder, name, parent, localPos, new Vector3(diameter, length * 0.5f, diameter), mat, euler);

        static Transform Group(string name, Transform parent, Vector3 localPos = default, Vector3? euler = null)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            if (euler.HasValue)
                go.transform.localEulerAngles = euler.Value;
            return go.transform;
        }

        static Transform Visuals(Transform parent) => Group("Visuals", parent);

        /// <summary>Local euler that makes a TMP/canvas front face <paramref name="dir"/> with text up = <paramref name="up"/>.</summary>
        static Vector3 Face(Vector3 dir, Vector3 up) => Quaternion.LookRotation(-dir, up).eulerAngles;

        static BoxCollider AddBox(GameObject go, Vector3 center, Vector3 size, bool trigger = false)
        {
            var c = go.AddComponent<BoxCollider>();
            c.center = center;
            c.size = size;
            c.isTrigger = trigger;
            return c;
        }

        // ------------------------------------------------------------------ text

        /// <summary>World-space 3D TMP label. Rotation is local euler; text faces local -Z by default.</summary>
        static TextMeshPro Label(string name, Transform parent, Vector3 localPos, Vector3 euler, string text,
                                 float fontSize, Color color, Vector2 rect)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localEulerAngles = euler;

            var tmp = go.AddComponent<TextMeshPro>();
            tmp.font = s_Font;
            tmp.fontSharedMaterial = s_Font.material;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = TextAlignmentOptions.Center;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.enableAutoSizing = true;
            tmp.fontSizeMax = fontSize;
            tmp.fontSizeMin = fontSize * 0.3f;
            tmp.rectTransform.sizeDelta = rect;

            var r = go.GetComponent<MeshRenderer>();
            r.shadowCastingMode = ShadowCastingMode.Off;
            r.receiveShadows = false;
            return tmp;
        }

        static TextMeshProUGUI UIText(string name, Transform parent, Vector2 anchoredPos, Vector2 size, string text,
                                      float fontSize, Color color, TextAlignmentOptions align = TextAlignmentOptions.Center)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var tmp = go.AddComponent<TextMeshProUGUI>();
            tmp.font = s_Font;
            tmp.fontSharedMaterial = s_Font.material;
            tmp.text = text;
            tmp.fontSize = fontSize;
            tmp.color = color;
            tmp.alignment = align;
            tmp.textWrappingMode = TextWrappingModes.Normal;
            tmp.raycastTarget = false;
            return tmp;
        }

        static Image UIPanel(string name, Transform parent, Vector2 anchoredPos, Vector2 size, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            var rt = (RectTransform)go.transform;
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = anchoredPos;
            rt.sizeDelta = size;

            var img = go.AddComponent<Image>();
            img.color = color;
            img.raycastTarget = false;
            return img;
        }

        /// <summary>World-space canvas in metres-per-pixel scale 0.001 (1000 px = 1 m).</summary>
        static Canvas WorldCanvas(string name, Transform parent, Vector3 worldPos, Vector3 worldEuler, Vector2 sizePx,
                                  bool interactive)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(parent, false);
            go.transform.position = worldPos;
            go.transform.eulerAngles = worldEuler;
            go.transform.localScale = Vector3.one * 0.001f;

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizePx;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = Camera.main;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 10f;

            if (interactive)
                go.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();

            return canvas;
        }

        static Button UIButton(string name, Transform parent, Vector2 anchoredPos, Vector2 size, string label,
                               Color color, UnityEngine.Events.UnityAction onClick)
        {
            var img = UIPanel(name, parent, anchoredPos, size, color);
            img.raycastTarget = true;

            var btn = img.gameObject.AddComponent<Button>();
            btn.targetGraphic = img;
            var colors = btn.colors;
            colors.highlightedColor = new Color(1f, 1f, 0.75f);
            colors.pressedColor = new Color(0.6f, 0.6f, 0.6f);
            btn.colors = colors;

            if (onClick != null)
                UnityEditor.Events.UnityEventTools.AddPersistentListener(btn.onClick, onClick);

            UIText("Label", img.transform, Vector2.zero, size, label, size.y * 0.42f, Color.white);

            var touch = img.gameObject.AddComponent<HandTouchButton>();
            Set(touch, "button", btn);
            return btn;
        }

        // --------------------------------------------------------- serialization

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[DPS Phase 3] No field " + field + " on " + target.GetType().Name); return; }
            p.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetF(Object target, string field, float value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[DPS Phase 3] No field " + field); return; }
            p.floatValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetB(Object target, string field, bool value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[DPS Phase 3] No field " + field); return; }
            p.boolValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetI(Object target, string field, int value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[DPS Phase 3] No field " + field); return; }
            if (p.propertyType == SerializedPropertyType.Enum) p.enumValueIndex = value; else p.intValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetV3(Object target, string field, Vector3 value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[DPS Phase 3] No field " + field); return; }
            p.vector3Value = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetColor(Object target, string field, Color value)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[DPS Phase 3] No field " + field); return; }
            p.colorValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetArr(Object target, string field, Object[] values)
        {
            var so = new SerializedObject(target);
            var p = so.FindProperty(field);
            if (p == null) { Debug.LogWarning("[DPS Phase 3] No field " + field); return; }
            p.arraySize = values.Length;
            for (var i = 0; i < values.Length; i++)
                p.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static GameObject SavePrefab(GameObject temp, string name)
        {
            var path = PrefabFolder + "/" + name + ".prefab";
            var asset = PrefabUtility.SaveAsPrefabAsset(temp, path);
            Object.DestroyImmediate(temp);
            return asset;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            foreach (Transform t in go.transform)
                SetLayerRecursive(t.gameObject, layer);
        }
    }
}
