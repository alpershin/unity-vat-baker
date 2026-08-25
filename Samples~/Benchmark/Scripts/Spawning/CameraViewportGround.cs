using UnityEngine;

namespace Alpershin.Vat.Samples.Spawning
{
    /// <summary>
    /// Maps a camera viewport, inset by a normalized padding, onto a ground plane.
    /// </summary>
    public sealed class CameraViewportGround
    {
        private const float MaxPadding = 0.45f;

        private readonly Camera _camera;
        private readonly Plane _plane;
        private readonly Vector2 _padding;
        private readonly float _maxDistance;

        public CameraViewportGround(Camera camera, Plane plane, Vector2 padding, float maxDistance)
        {
            _camera = camera;
            _plane = plane;
            _padding = new Vector2(
                Mathf.Clamp(padding.x, 0f, MaxPadding),
                Mathf.Clamp(padding.y, 0f, MaxPadding));
            _maxDistance = Mathf.Max(1f, maxDistance);
        }

        /// <summary>
        /// Axis-aligned XZ bounds of the four padded viewport corners, expressed in the local space
        /// of <paramref name="space"/>. Wider than the visible trapezoid — use <see cref="Contains"/> to trim.
        /// </summary>
        public Rect GetLocalArea(Transform space)
        {
            var min = new Vector2(float.MaxValue, float.MaxValue);
            var max = new Vector2(float.MinValue, float.MinValue);

            for (var corner = 0; corner < 4; corner++)
            {
                var local = space.InverseTransformPoint(GetCornerOnPlane(corner));
                min = Vector2.Min(min, new Vector2(local.x, local.z));
                max = Vector2.Max(max, new Vector2(local.x, local.z));
            }

            return Rect.MinMaxRect(min.x, min.y, max.x, max.y);
        }

        /// <summary>
        /// Area of the visible ground quad itself, which is a trapezoid under a tilted perspective
        /// camera and therefore smaller than <see cref="GetLocalArea"/>.
        /// </summary>
        public float GetGroundArea(Transform space)
        {
            var a = ToLocalXZ(space, GetCornerOnPlane(0));
            var b = ToLocalXZ(space, GetCornerOnPlane(1));
            var c = ToLocalXZ(space, GetCornerOnPlane(3));
            var d = ToLocalXZ(space, GetCornerOnPlane(2));

            var doubleArea = Cross(a, b) + Cross(b, c) + Cross(c, d) + Cross(d, a);
            return Mathf.Abs(doubleArea) * 0.5f;
        }

        public bool Contains(Vector3 worldPoint)
        {
            var viewport = _camera.WorldToViewportPoint(worldPoint);
            if (viewport.z <= 0f)
            {
                return false;
            }

            return viewport.x >= _padding.x && viewport.x <= 1f - _padding.x
                && viewport.y >= _padding.y && viewport.y <= 1f - _padding.y;
        }

        public Vector3 GetCornerOnPlane(int corner)
        {
            var viewportPoint = new Vector3(
                (corner & 1) == 0 ? _padding.x : 1f - _padding.x,
                (corner & 2) == 0 ? _padding.y : 1f - _padding.y,
                0f);

            var ray = _camera.ViewportPointToRay(viewportPoint);
            var hitsPlane = _plane.Raycast(ray, out var distance);
            var clamped = hitsPlane ? Mathf.Min(distance, _maxDistance) : _maxDistance;

            return _plane.ClosestPointOnPlane(ray.GetPoint(clamped));
        }

        private static Vector2 ToLocalXZ(Transform space, Vector3 worldPoint)
        {
            var local = space.InverseTransformPoint(worldPoint);
            return new Vector2(local.x, local.z);
        }

        private static float Cross(Vector2 from, Vector2 to)
        {
            return from.x * to.y - to.x * from.y;
        }
    }
}
