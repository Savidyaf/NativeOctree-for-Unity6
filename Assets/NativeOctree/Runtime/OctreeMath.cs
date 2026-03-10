using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace NativeOctree
{
    /// <summary>
    /// Shared spatial math utilities for octree queries.
    /// All methods are static, Burst-compatible, and marked for aggressive inlining.
    /// New query types (raycast, kNN, frustum) should use these instead of duplicating geometry logic.
    /// </summary>
    public static class OctreeMath
    {
        /// <summary>
        /// Compute the AABB of one of the 8 children of a uniform parent node.
        /// Child index bits map directly to spatial direction (0=negative, 1=positive):
        /// bit 0 = X, bit 1 = Y, bit 2 = Z.
        /// This matches the morton code bit layout so no axis inversion is needed.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static AABB GetChildBounds(AABB parentBounds, int childZIndex)
        {
            var half = parentBounds.Extents.x * 0.5f;
            var offset = new float3(
                math.select(-half, half, (childZIndex & 1) != 0),
                math.select(-half, half, (childZIndex & 2) != 0),
                math.select(-half, half, (childZIndex & 4) != 0)
            );
            return new AABB { Center = parentBounds.Center + offset, Extents = half };
        }

        /// <summary>
        /// Test whether two AABBs overlap (exclusive on boundary).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Intersects(AABB a, AABB b)
        {
            return math.all(math.abs(a.Center - b.Center) < (a.Extents + b.Extents));
        }

        /// <summary>
        /// Test whether AABB <paramref name="outer"/> fully contains <paramref name="inner"/>.
        /// Delegates to Unity.Mathematics AABB.Contains.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(AABB outer, AABB inner)
        {
            return outer.Contains(inner);
        }

        /// <summary>
        /// Test whether an AABB contains a point.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool Contains(AABB bounds, float3 point)
        {
            return bounds.Contains(point);
        }
    }
}
