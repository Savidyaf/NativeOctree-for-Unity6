using System.Runtime.CompilerServices;
using Unity.Mathematics;

namespace NativeOctree
{
    /// <summary>
    /// Utility for encoding 3D positions into morton (Z-order) codes.
    /// Morton codes spatially interleave the bits of quantized X, Y, Z coordinates,
    /// producing a single integer that preserves spatial locality.
    /// </summary>
    public static unsafe class MortonCodeUtil
    {
        /// <summary>
        /// Encode a world-space position into a morton code for the given octree bounds and depth.
        /// Positions are clamped to valid range to prevent out-of-bounds table access.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Encode(float3 worldPos, AABB bounds, int maxDepth)
        {
            var depthExtentsScaling = LookupTables.DepthLookup.Data.Values[maxDepth] / bounds.Extents;
            return EncodeScaled(worldPos, bounds, depthExtentsScaling);
        }

        /// <summary>
        /// Encode with a pre-computed scaling factor (for batch encoding where the scaling
        /// is the same for all elements).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int EncodeScaled(float3 worldPos, AABB bounds, float3 depthExtentsScaling)
        {
            var localPos = worldPos - bounds.Center;
            var pos = (localPos + bounds.Extents) * 0.5f;
            pos *= depthExtentsScaling;

            pos = math.clamp(pos, 0f, 255f);

            ref var morton = ref LookupTables.MortonLookup.Data;
            return (int)(morton.Values[(int)pos.x] |
                         (morton.Values[(int)pos.y] << 1) |
                         (morton.Values[(int)pos.z] << 2));
        }
    }
}
