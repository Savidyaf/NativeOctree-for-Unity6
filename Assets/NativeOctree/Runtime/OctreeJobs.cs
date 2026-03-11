using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace NativeOctree
{
    
    /// <summary>
    /// Burst-compiled jobs for common octree operations.
    /// </summary>
    public static class OctreeJobs
    {
        /// <summary>
        /// Clear the octree and bulk-insert all provided elements.
        /// </summary>
        [BurstCompile]
        public struct AddBulkJob<T> : IJob where T : unmanaged
        {
            [ReadOnly]
            public NativeArray<OctElement<T>> Elements;

            public NativeOctree<T> Octree;

            public void Execute()
            {
                Octree.ClearAndBulkInsert(Elements);
            }
        }

        /// <summary>
        /// Execute a single AABB range query against the octree.
        /// </summary>
        [BurstCompile]
        public struct RangeQueryJob<T> : IJob where T : unmanaged
        {
            [ReadOnly]
            public AABB Bounds;

            [ReadOnly]
            public NativeOctree<T> Octree;

            public NativeList<OctElement<T>> Results;

            public void Execute()
            {
                Octree.RangeQuery(Bounds, Results);
            }
        }
    }
}
