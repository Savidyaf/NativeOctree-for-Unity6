using System.Runtime.CompilerServices;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace NativeOctree
{
    public unsafe partial struct NativeOctree<T> where T : unmanaged
    {
        /// <summary>
        /// Clear the tree and insert all elements at once using morton code spatial indexing.
        /// This is the primary insertion path and is optimized for Burst compilation.
        /// </summary>
        /// <param name="incomingElements">Elements to insert. Positions should be within the octree bounds.</param>
        public void ClearAndBulkInsert(NativeArray<OctElement<T>> incomingElements)
        {
            Clear();

#if ENABLE_UNITY_COLLECTIONS_CHECKS
            AtomicSafetyHandle.CheckWriteAndBumpSecondaryVersion(m_Safety);
#endif

            if (elements->Capacity < incomingElements.Length)
            {
                elements->Resize(incomingElements.Length);
            }

            var mortonCodes = new NativeArray<int>(incomingElements.Length, Allocator.Temp);
            var depthExtentsScaling = LookupTables.DepthLookup.Data.Values[maxDepth] / bounds.Extents;

            for (var i = 0; i < incomingElements.Length; i++)
            {
                mortonCodes[i] = MortonCodeUtil.EncodeScaled(incomingElements[i].pos, bounds, depthExtentsScaling);
            }

            var mortonCodesPtr = (int*)NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr(mortonCodes);
            ref var depthSizeLookupData = ref LookupTables.DepthSizeLookup.Data;
            var depthSizePtr = (int*)UnsafeUtility.AddressOf(ref depthSizeLookupData);

            var lookupPtr = lookup->Ptr;
            for (var i = 0; i < incomingElements.Length; i++)
            {
                int mortonCode = mortonCodesPtr[i];
                int atIndex = 0;
                for (int depth = 0; depth < maxDepth; depth++)
                {
                    lookupPtr[atIndex]++;
                    atIndex = IncrementIndex(atIndex, mortonCode, maxDepth - 1 - depth, depthSizePtr);
                }
                lookupPtr[atIndex]++;
            }

            RecursivePrepareLeaves(1, 1, depthSizePtr);

            var nodesPtr = nodes->Ptr;
            var elementsPtr = elements->Ptr;
            for (var i = 0; i < incomingElements.Length; i++)
            {
                int mortonCode = mortonCodesPtr[i];
                int atIndex = 0;
                for (int depth = 0; depth <= maxDepth; depth++)
                {
                    ref var node = ref nodesPtr[atIndex];
                    if (node.isLeaf)
                    {
                        elementsPtr[node.firstChildIndex + node.count] = incomingElements[i];
                        node.count++;
                        break;
                    }
                    atIndex = IncrementIndex(atIndex, mortonCode, maxDepth - 1 - depth, depthSizePtr);
                }
            }

            mortonCodes.Dispose();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        static int IncrementIndex(int atIndex, int mortonCode, int remainingDepth, int* depthSizePtr)
        {
            int octant = (mortonCode >> (remainingDepth * 3)) & 0b111;
            return atIndex + depthSizePtr[remainingDepth + 1] * octant + 1;
        }

        void RecursivePrepareLeaves(int prevOffset, int depth, int* depthSizePtr)
        {
            var lookupPtr = lookup->Ptr;
            var nodesPtr = nodes->Ptr;

            var depthSize = depthSizePtr[maxDepth - depth + 1];
            for (int l = 0; l < 8; l++)
            {
                var at = prevOffset + l * depthSize;
                var elementCount = lookupPtr[at];

                if (elementCount > maxLeafElements && depth < maxDepth)
                {
                    RecursivePrepareLeaves(at + 1, depth + 1, depthSizePtr);
                }
                else if (elementCount != 0)
                {
                    nodesPtr[at] = new OctNode
                    {
                        firstChildIndex = elementsCount,
                        count = 0,
                        isLeaf = true
                    };
                    elementsCount += elementCount;
                }
            }
        }
    }
}
