using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Mathematics;

namespace NativeOctree
{
    public unsafe partial struct NativeOctree<T> where T : unmanaged
    {
        struct OctreeRangeQuery
        {
            NativeOctree<T> tree;
            UnsafeList<OctElement<T>>* fastResults;
            int count;
            AABB bounds;

            int* lookupPtr;
            OctNode* nodesPtr;
            OctElement<T>* elementsPtr;

            public void Query(NativeOctree<T> tree, AABB bounds, NativeList<OctElement<T>> results)
            {
                this.tree = tree;
                this.bounds = bounds;
                count = 0;

                fastResults = results.GetUnsafeList();
                lookupPtr = tree.lookup->Ptr;
                nodesPtr = tree.nodes->Ptr;
                elementsPtr = tree.elements->Ptr;

                RecursiveRangeQuery(tree.bounds, false, 1, 1);

                fastResults->Length = count;
            }

            void RecursiveRangeQuery(AABB parentBounds, bool parentContained, int prevOffset, int depth)
            {
                var depthSize = LookupTables.DepthSizeLookup.Data.Values[tree.maxDepth - depth + 1];

                for (int l = 0; l < 8; l++)
                {
                    var at = prevOffset + l * depthSize;
                    var elementCount = lookupPtr[at];
                    if (elementCount == 0) continue;

                    var childBounds = OctreeMath.GetChildBounds(parentBounds, l);

                    var contained = parentContained;
                    if (!contained)
                    {
                        if (OctreeMath.Contains(bounds, childBounds))
                        {
                            contained = true;
                        }
                        else if (!OctreeMath.Intersects(bounds, childBounds))
                        {
                            continue;
                        }
                    }

                    if (elementCount > tree.maxLeafElements && depth < tree.maxDepth)
                    {
                        RecursiveRangeQuery(childBounds, contained, at + 1, depth + 1);
                    }
                    else
                    {
                        var node = nodesPtr[at];

                        var requiredCapacity = count + node.count;
                        if (requiredCapacity > fastResults->Capacity)
                        {
                            fastResults->Resize(math.max(fastResults->Capacity * 2, requiredCapacity));
                        }

                        if (contained)
                        {
                            UnsafeUtility.MemCpy(
                                fastResults->Ptr + count,
                                elementsPtr + node.firstChildIndex,
                                node.count * UnsafeUtility.SizeOf<OctElement<T>>());
                            count += node.count;
                        }
                        else
                        {
                            for (int k = 0; k < node.count; k++)
                            {
                                var element = elementsPtr[node.firstChildIndex + k];
                                if (OctreeMath.Contains(bounds, element.pos))
                                {
                                    fastResults->Ptr[count++] = element;
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
