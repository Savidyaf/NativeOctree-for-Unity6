using System;
using System.Runtime.CompilerServices;
using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace NativeOctree
{
    /// <summary>
    /// A Burst-compatible octree native container for fast spatial queries on point data.
    /// <para>
    /// Supports bulk insertion via <see cref="ClearAndBulkInsert"/> and AABB range queries
    /// via <see cref="RangeQuery"/>. Uses morton codes for cache-friendly bulk insertion.
    /// </para>
    /// <para>
    /// Thread safety: a single writer OR multiple readers, enforced by the safety system in
    /// development builds. Pass into jobs just like any other native container.
    /// </para>
    /// <para>
    /// Elements are stored contiguously per leaf node. This layout is optimal for bulk
    /// insert + query workloads. Future individual add/remove should use a pending-add
    /// buffer and tombstone marking with periodic rebuild via ClearAndBulkInsert.
    /// </para>
    /// </summary>
    /// <typeparam name="T">Unmanaged payload type stored alongside each point position.</typeparam>
    public unsafe partial struct NativeOctree<T> : IDisposable where T : unmanaged
    {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
        AtomicSafetyHandle m_Safety;
        static readonly int s_staticSafetyId = AtomicSafetyHandle.NewStaticSafetyId<NativeOctree<T>>();
#endif

        [NoAlias] [NativeDisableUnsafePtrRestriction]
        UnsafeList<OctElement<T>>* elements;

        [NoAlias] [NativeDisableUnsafePtrRestriction]
        UnsafeList<int>* lookup;

        [NoAlias] [NativeDisableUnsafePtrRestriction]
        UnsafeList<OctNode>* nodes;

        int elementsCount;
        int maxDepth;
        int maxLeafElements;

        AABB bounds;

        int TotalNodeCount
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => LookupTables.DepthSizeLookup.Data.Values[maxDepth + 1];
        }

        /// <summary>
        /// Create a new octree.
        /// </summary>
        /// <param name="bounds">
        /// World-space AABB. Should be uniform (equal extents on all axes) and tightly fit the data.
        /// Oversized bounds degrade bucket quality.
        /// </param>
        /// <param name="allocator">Memory allocator. Use TempJob for jobs, Persistent for long-lived trees.</param>
        /// <param name="maxDepth">Maximum subdivision depth (1-8). Higher values increase memory overhead exponentially.</param>
        /// <param name="maxLeafElements">Maximum elements per leaf before subdivision.</param>
        /// <param name="initialElementsCapacity">Initial capacity for the elements buffer.</param>
        public NativeOctree(AABB bounds, Allocator allocator = Allocator.Temp, int maxDepth = 6,
            int maxLeafElements = 16, int initialElementsCapacity = 256) : this()
        {
            if (maxDepth < 1 || maxDepth > 8)
                throw new InvalidOperationException("Max depth must be between 1 and 8 (morton code table limit).");
            if (maxLeafElements < 1)
                throw new InvalidOperationException("Max leaf elements must be at least 1.");

            LookupTables.Initialize();

            this.bounds = bounds;
            this.maxDepth = maxDepth;
            this.maxLeafElements = maxLeafElements;
            elementsCount = 0;

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            m_Safety = AtomicSafetyHandle.Create();
            AtomicSafetyHandle.SetStaticSafetyId(ref m_Safety, s_staticSafetyId);
            AtomicSafetyHandle.SetBumpSecondaryVersionOnScheduleWrite(m_Safety, true);
#endif

            var totalSize = TotalNodeCount;

            lookup = UnsafeList<int>.Create(totalSize, allocator, NativeArrayOptions.ClearMemory);
            nodes = UnsafeList<OctNode>.Create(totalSize, allocator, NativeArrayOptions.ClearMemory);
            elements = UnsafeList<OctElement<T>>.Create(initialElementsCapacity, allocator);
        }

        /// <summary>
        /// Perform an AABB range query, collecting all elements whose positions fall within the given bounds.
        /// </summary>
        /// <param name="queryBounds">The AABB to query.</param>
        /// <param name="results">List to receive matching elements. Not cleared before use -- caller should clear if needed.</param>
        public void RangeQuery(AABB queryBounds, NativeList<OctElement<T>> results)
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckReadAndThrow(m_Safety);
#endif
            new OctreeRangeQuery().Query(this, queryBounds, results);
        }

        /// <summary>
        /// Clear all elements and node data. Called automatically by <see cref="ClearAndBulkInsert"/>.
        /// Clears the full lookup and nodes arrays (all pre-allocated nodes); elements buffer is not
        /// cleared since it is fully overwritten during bulk insert.
        /// </summary>
        public void Clear()
        {
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif
            var totalSize = TotalNodeCount;
            UnsafeUtility.MemClear(lookup->Ptr, totalSize * UnsafeUtility.SizeOf<int>());
            UnsafeUtility.MemClear(nodes->Ptr, totalSize * UnsafeUtility.SizeOf<OctNode>());
            elementsCount = 0;
        }

        /// <summary>
        /// The number of elements currently stored in the octree.
        /// Useful for pre-sizing result lists before calling <see cref="RangeQuery"/>.
        /// </summary>
        public int Count => elementsCount;

        internal AABB Bounds => bounds;
        internal int NodeCount => TotalNodeCount;
        internal OctNode* NodesPtr => nodes->Ptr;
        internal OctElement<T>* ElementsPtr => elements->Ptr;
        internal int* LookupPtr => lookup->Ptr;
        internal int MaxDepth => maxDepth;
        internal int MaxLeafElements => maxLeafElements;

        /// <summary>
        /// Dispose all native memory. Must be called when the octree is no longer needed.
        /// </summary>
        public void Dispose()
        {
            UnsafeList<OctElement<T>>.Destroy(elements);
            elements = null;
            UnsafeList<int>.Destroy(lookup);
            lookup = null;
            UnsafeList<OctNode>.Destroy(nodes);
            nodes = null;
#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckDeallocateAndThrow(m_Safety);
            AtomicSafetyHandle.Release(m_Safety);
#endif
        }
    }
}
