using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.Interactables;

namespace DPS
{
    /// <summary>
    /// Increases XR grab stability in VR. XRGrabInteractable's default "snap" behaviour makes tools
    /// jitter when held at the ends, because the object pivots around its origin. Attaching at the
    /// interactor pose with tracking smoothing off keeps a baton steady in the hand.
    /// </summary>
    public static class GrabTuning
    {
        /// <summary>Applies VR-friendly grab settings. Idempotent.</summary>
        public static void Apply(XRGrabInteractable grab, bool throwable = true)
        {
            if (grab == null)
                return;

            grab.useDynamicAttach = true;
            grab.smoothPosition = false;
            grab.smoothRotation = false;
            grab.throwOnDetach = throwable;
            grab.throwSmoothingDuration = 0.15f;
            grab.throwVelocityScale = 1.2f;
            grab.throwAngularVelocityScale = 0.6f;
            grab.selectMode = InteractableSelectMode.Single;
            grab.movementType = XRBaseInteractable.MovementType.Kinematic;
        }

        /// <summary>
        /// Tools stayed physics-driven while grabbed would fight the hand transform and jitter on
        /// Quest; kinematic while held is the standard VR choice for props.
        /// </summary>
        public static void MakeHoldable(XRGrabInteractable grab, Rigidbody body)
        {
            if (grab == null)
                return;

            grab.movementType = XRBaseInteractable.MovementType.Kinematic;

            if (body != null)
            {
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousSpeculative;
            }
        }
    }
}
