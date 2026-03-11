using System.Diagnostics;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace NativeOctree.Demo
{
    public enum PointDistribution
    {
        Uniform,
        Clustered,
        SphereSurface,
        Spiral
    }

    public class OctreeDemo : MonoBehaviour
    {
        [Header("Octree")]
        [SerializeField] float boundsSize = 100f;
        [SerializeField, Range(1, 6)] int maxDepth = 4;
        [SerializeField, Range(1, 64)] int maxLeafElements = 16;

        [Header("Elements")]
        [SerializeField, Range(100, 50000)] int elementCount = 5000;
        [SerializeField] PointDistribution distribution = PointDistribution.Clustered;
        [SerializeField] uint seed = 42;

        [Header("Animation")]
        [SerializeField] bool animate;
        [SerializeField] float animationSpeed = 5f;

        [Header("Query")]
        [SerializeField] Transform queryCenter;
        [SerializeField] Vector3 queryHalfExtents = new Vector3(20f, 20f, 20f);

        [Header("Visualization")]
        [SerializeField] bool showNodes = true;
        [SerializeField] bool showElements = true;
        [SerializeField] bool showQuery = true;
        [SerializeField, Range(1, 6)] int maxVisibleDepth = 6;
        [SerializeField, Range(0.1f, 1.5f)] float elementSize = 0.4f;

        [Header("Colors")]
        [SerializeField] Color boundsColor = new Color(1f, 1f, 1f, 0.25f);
        [SerializeField] Color elementColor = new Color(1f, 1f, 1f, 0.5f);
        [SerializeField] Color nodeColorShallow = new Color(0.15f, 0.4f, 0.95f, 0.6f);
        [SerializeField] Color nodeColorDeep = new Color(0.1f, 0.95f, 0.3f, 0.25f);
        [SerializeField] Color queryFillColor = new Color(1f, 0.9f, 0.2f, 0.08f);
        [SerializeField] Color queryWireColor = new Color(1f, 0.9f, 0.2f, 0.7f);
        [SerializeField] Color queryResultColor = new Color(0.2f, 1f, 0.3f, 0.9f);

        [Header("Stats (Read Only)")]
        [SerializeField] int statElementCount;
        [SerializeField] int statQueryResultCount;
        [SerializeField] float statInsertMs;
        [SerializeField] float statQueryMs;

        NativeOctree<int> octree;
        NativeArray<OctElement<int>> elements;
        NativeList<OctElement<int>> queryResults;
        NativeArray<float3> velocities;
        bool initialized;

        void OnEnable()
        {
            Rebuild();
        }

        void OnDisable()
        {
            Cleanup();
        }

        void OnValidate()
        {
            if (!Application.isPlaying) return;
            if (initialized) Rebuild();
        }

        void Update()
        {
            if (!animate || !initialized) return;

            float dt = Time.deltaTime * animationSpeed;
            for (int i = 0; i < elements.Length; i++)
            {
                var el = elements[i];
                el.pos += velocities[i] * dt;

                // Bounce off octree bounds
                for (int axis = 0; axis < 3; axis++)
                {
                    if (math.abs(el.pos[axis]) > boundsSize)
                    {
                        var v = velocities[i];
                        v[axis] = -v[axis];
                        velocities[i] = v;
                        el.pos[axis] = math.clamp(el.pos[axis], -boundsSize, boundsSize);
                    }
                }
                elements[i] = el;
            }

            var sw = Stopwatch.StartNew();
            octree.ClearAndBulkInsert(elements);
            sw.Stop();
            statInsertMs = (float)sw.Elapsed.TotalMilliseconds;
        }

        void Rebuild()
        {
            Cleanup();

            var bounds = new AABB { Center = float3.zero, Extents = boundsSize };
            octree = new NativeOctree<int>(bounds, Allocator.Persistent, maxDepth, maxLeafElements, elementCount);
            queryResults = new NativeList<OctElement<int>>(256, Allocator.Persistent);

            GenerateElements();

            var sw = Stopwatch.StartNew();
            octree.ClearAndBulkInsert(elements);
            sw.Stop();
            statInsertMs = (float)sw.Elapsed.TotalMilliseconds;
            statElementCount = elementCount;

            initialized = true;
        }

        void Cleanup()
        {
            bool wasInitialized = initialized;
            initialized = false;
            if (elements.IsCreated) elements.Dispose();
            if (velocities.IsCreated) velocities.Dispose();
            if (queryResults.IsCreated) queryResults.Dispose();
            if (wasInitialized) octree.Dispose();
        }

        void GenerateElements()
        {
            elements = new NativeArray<OctElement<int>>(elementCount, Allocator.Persistent);
            velocities = new NativeArray<float3>(elementCount, Allocator.Persistent);
            var rng = new Unity.Mathematics.Random(seed == 0 ? 1 : seed);

            switch (distribution)
            {
                case PointDistribution.Uniform:
                    GenerateUniform(ref rng);
                    break;
                case PointDistribution.Clustered:
                    GenerateClustered(ref rng);
                    break;
                case PointDistribution.SphereSurface:
                    GenerateSphereSurface(ref rng);
                    break;
                case PointDistribution.Spiral:
                    GenerateSpiral(ref rng);
                    break;
            }

            if (animate)
            {
                for (int i = 0; i < elementCount; i++)
                    velocities[i] = rng.NextFloat3Direction() * rng.NextFloat(1f, 8f);
            }
        }

        void GenerateUniform(ref Unity.Mathematics.Random rng)
        {
            for (int i = 0; i < elementCount; i++)
            {
                elements[i] = new OctElement<int>
                {
                    pos = rng.NextFloat3(-boundsSize, boundsSize),
                    element = i
                };
            }
        }

        void GenerateClustered(ref Unity.Mathematics.Random rng)
        {
            int clusterCount = rng.NextInt(5, 9);
            var centers = new NativeArray<float3>(clusterCount, Allocator.Temp);
            var radii = new NativeArray<float>(clusterCount, Allocator.Temp);

            for (int c = 0; c < clusterCount; c++)
            {
                centers[c] = rng.NextFloat3(-boundsSize * 0.7f, boundsSize * 0.7f);
                radii[c] = rng.NextFloat(boundsSize * 0.05f, boundsSize * 0.25f);
            }

            for (int i = 0; i < elementCount; i++)
            {
                int cluster = rng.NextInt(0, clusterCount);
                float3 offset = rng.NextFloat3Direction() * radii[cluster] * math.sqrt(rng.NextFloat());
                float3 pos = math.clamp(centers[cluster] + offset, -boundsSize, boundsSize);
                elements[i] = new OctElement<int> { pos = pos, element = i };
            }

            centers.Dispose();
            radii.Dispose();
        }

        void GenerateSphereSurface(ref Unity.Mathematics.Random rng)
        {
            float radius = boundsSize * 0.8f;
            for (int i = 0; i < elementCount; i++)
            {
                float3 dir = rng.NextFloat3Direction();
                float shellOffset = rng.NextFloat(-boundsSize * 0.03f, boundsSize * 0.03f);
                elements[i] = new OctElement<int>
                {
                    pos = dir * (radius + shellOffset),
                    element = i
                };
            }
        }

        void GenerateSpiral(ref Unity.Mathematics.Random rng)
        {
            float maxRadius = boundsSize * 0.8f;
            float height = boundsSize * 1.6f;
            float turns = 5f;

            for (int i = 0; i < elementCount; i++)
            {
                float t = (float)i / elementCount;
                float angle = t * turns * 2f * math.PI;
                float r = t * maxRadius;
                float y = (t - 0.5f) * height;
                float3 pos = new float3(math.cos(angle) * r, y, math.sin(angle) * r);
                float3 jitter = rng.NextFloat3(-boundsSize * 0.015f, boundsSize * 0.015f);
                elements[i] = new OctElement<int>
                {
                    pos = math.clamp(pos + jitter, -boundsSize, boundsSize),
                    element = i
                };
            }
        }

        unsafe void OnDrawGizmos()
        {
            if (!initialized) return;

            var treeBounds = octree.Bounds;

            // Root bounds
            Gizmos.color = boundsColor;
            Gizmos.DrawWireCube(treeBounds.Center, (Vector3)(float3)treeBounds.Extents * 2f);

            // All elements (base layer, drawn first)
            if (showElements)
            {
                Gizmos.color = elementColor;
                for (int i = 0; i < elements.Length; i++)
                    Gizmos.DrawCube(elements[i].pos, Vector3.one * elementSize);
            }

            // Octree node wireframes
            if (showNodes)
            {
                DrawNodesRecursive(
                    treeBounds,
                    1, 1,
                    octree.LookupPtr,
                    octree.NodesPtr,
                    octree.ElementsPtr,
                    octree.MaxDepth,
                    octree.MaxLeafElements
                );
            }

            // Range query
            if (showQuery && queryCenter != null)
            {
                var sw = Stopwatch.StartNew();
                queryResults.Clear();
                var qBounds = new AABB
                {
                    Center = (float3)queryCenter.position,
                    Extents = (float3)queryHalfExtents
                };
                octree.RangeQuery(qBounds, queryResults);
                sw.Stop();
                statQueryMs = (float)sw.Elapsed.TotalMilliseconds;
                statQueryResultCount = queryResults.Length;

                // Draw query box
                Gizmos.color = queryFillColor;
                Gizmos.DrawCube(queryCenter.position, (Vector3)(float3)qBounds.Extents * 2f);
                Gizmos.color = queryWireColor;
                Gizmos.DrawWireCube(queryCenter.position, (Vector3)(float3)qBounds.Extents * 2f);

                // Draw query results on top
                if (showElements)
                {
                    Gizmos.color = queryResultColor;
                    for (int i = 0; i < queryResults.Length; i++)
                        Gizmos.DrawCube(queryResults[i].pos, Vector3.one * elementSize * 1.4f);
                }
            }
        }

        unsafe void DrawNodesRecursive(
            AABB parentBounds, int prevOffset, int depth,
            int* lookupPtr, OctNode* nodesPtr, OctElement<int>* elementsPtr,
            int treeMaxDepth, int treeMaxLeaf)
        {
            int depthSize = LookupTables.DepthSizeLookup.Data.Values[treeMaxDepth - depth + 1];

            for (int l = 0; l < 8; l++)
            {
                int at = prevOffset + l * depthSize;
                int elementCount = lookupPtr[at];
                if (elementCount == 0) continue;

                var childBounds = OctreeMath.GetChildBounds(parentBounds, l);

                if (depth <= maxVisibleDepth)
                {
                    float t = (float)(depth - 1) / math.max(treeMaxDepth - 1, 1);
                    Gizmos.color = Color.Lerp(nodeColorShallow, nodeColorDeep, t);
                    Gizmos.DrawWireCube(childBounds.Center, (Vector3)(float3)childBounds.Extents * 2f);
                }

                if (elementCount > treeMaxLeaf && depth < treeMaxDepth)
                {
                    DrawNodesRecursive(childBounds, at + 1, depth + 1,
                        lookupPtr, nodesPtr, elementsPtr, treeMaxDepth, treeMaxLeaf);
                }
            }
        }
    }
}
