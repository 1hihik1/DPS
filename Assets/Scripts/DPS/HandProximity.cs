using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactors;

namespace DPS
{
    /// <summary>
    /// Locates the player's hands/controllers as world-space points.
    ///
    /// This exists because the VR Template rig ships with NO colliders on hands or controllers -
    /// only a CharacterController on the XR Origin. Any design that relies on hand
    /// <c>OnTriggerEnter</c> would silently never fire on this project, so "touch" is detected by
    /// measuring distance to each interactor's attach transform instead.
    ///
    /// Interactor attach transforms are cached; call <see cref="Refresh"/> after the rig changes.
    /// </summary>
    public static class HandProximity
    {
        static readonly List<Transform> HandPoints = new List<Transform>();
        static bool s_Initialised;

        /// <summary>Hands found on the last refresh. Zero means the rig is missing.</summary>
        public static int HandCount => HandPoints.Count;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        static void ResetStatics()
        {
            HandPoints.Clear();
            s_Initialised = false;
        }

        /// <summary>Rebuilds the cached hand list from the scene's interactors.</summary>
        public static void Refresh()
        {
            HandPoints.Clear();

            var interactors = Object.FindObjectsByType<XRBaseInteractor>(FindObjectsSortMode.None);
            foreach (var interactor in interactors)
            {
                if (interactor == null)
                    continue;

                // Only hands and controllers belong to the player; skip UI/teleport gizmos.
                var attach = interactor.GetAttachTransform(null);
                if (attach == null)
                    continue;

                if (!IsPlayerHand(attach, interactor))
                    continue;

                if (!HandPoints.Contains(attach))
                    HandPoints.Add(attach);
            }

            s_Initialised = HandPoints.Count > 0;
        }

        static bool IsPlayerHand(Transform attach, XRBaseInteractor interactor)
        {
            // Walk up from the attach transform. Only an ancestor named for a hand or controller
            // counts; the gaze-stabilized head attach and the teleport-stabilized origins live
            // under the same "Camera Offset" root and must NOT be treated as hands, or proximity
            // would trigger off the player's head.
            for (var t = attach; t != null; t = t.parent)
            {
                var n = t.name;

                if (n.StartsWith("Gaze Stabilized") || n.StartsWith("Climb") ||
                    n.Contains("Teleport Stabilized"))
                    return false;

                if (n.StartsWith("Left Hand") || n.StartsWith("Right Hand") ||
                    n.StartsWith("Left Controller") || n.StartsWith("Right Controller"))
                    return true;

                if (n == "Camera Offset")
                    return false;
            }

            return false;
        }

        static void EnsureInitialised()
        {
            if (s_Initialised)
            {
                // A domain reload or rig swap invalidates the cached transforms.
                for (var i = 0; i < HandPoints.Count; i++)
                {
                    if (HandPoints[i] != null)
                        continue;
                    s_Initialised = false;
                    break;
                }
            }

            if (!s_Initialised)
                Refresh();
        }

        /// <summary>Shortest distance from <paramref name="worldPoint"/> to any hand.</summary>
        public static float DistanceToNearestHand(Vector3 worldPoint)
        {
            EnsureInitialised();

            var best = float.PositiveInfinity;
            for (var i = 0; i < HandPoints.Count; i++)
            {
                var hand = HandPoints[i];
                if (hand == null)
                    continue;
                best = Mathf.Min(best, Vector3.Distance(worldPoint, hand.position));
            }

            return best;
        }

        /// <summary>True when any hand is within <paramref name="radius"/> of the point.</summary>
        public static bool IsHandNear(Vector3 worldPoint, float radius)
        {
            return DistanceToNearestHand(worldPoint) <= radius;
        }

        /// <summary>True when a hand lies inside the bounds (expanded by <paramref name="padding"/>).</summary>
        public static bool IsHandInside(Bounds bounds, float padding)
        {
            EnsureInitialised();

            for (var i = 0; i < HandPoints.Count; i++)
            {
                var hand = HandPoints[i];
                if (hand == null)
                    continue;

                if (bounds.SqrDistance(hand.position) <= padding * padding)
                    return true;
            }

            return false;
        }
    }
}
