using System.Diagnostics;
using NUnit.Framework;
using NativeOctree;
using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace NativeOctree.Tests
{
    public class OctreeBenchmarkTests
    {
        static AABB DefaultBounds => new AABB { Center = 0, Extents = 1000 };

        NativeArray<OctElement<int>> CreateRandomElements(int count, uint seed = 0)
        {
            var rng = new Unity.Mathematics.Random(seed == 0 ? 42u : seed);
            var elements = new NativeArray<OctElement<int>>(count, Allocator.TempJob);
            for (int i = 0; i < count; i++)
            {
                elements[i] = new OctElement<int>
                {
                    pos = rng.NextFloat3(-900, 900),
                    element = i
                };
            }
            return elements;
        }

        [BurstCompile]
        struct BenchmarkRangeQueryJob : IJob
        {
            [ReadOnly] public NativeOctree<int> Octree;
            [ReadOnly] public AABB Bounds;
            public NativeList<OctElement<int>> Results;
            public int Iterations;

            public void Execute()
            {
                for (int i = 0; i < Iterations; i++)
                {
                    Octree.RangeQuery(Bounds, Results);
                    Results.Clear();
                }
                Octree.RangeQuery(Bounds, Results);
            }
        }

        [Test]
        public void Benchmark_BulkInsert_20k()
        {
            var elements = CreateRandomElements(20000);
            var job = new OctreeJobs.AddBulkJob<int>
            {
                Elements = elements,
                Octree = new NativeOctree<int>(DefaultBounds, Allocator.TempJob)
            };

            var sw = Stopwatch.StartNew();
            job.Run();
            sw.Stop();
            Debug.Log($"Bulk insert 20k: {sw.Elapsed.TotalMilliseconds:F3}ms");

            job.Octree.Dispose();
            elements.Dispose();
        }

        [Test]
        public void Benchmark_RangeQuery_1kIterations()
        {
            var elements = CreateRandomElements(20000);
            var octree = new NativeOctree<int>(DefaultBounds, Allocator.TempJob);
            octree.ClearAndBulkInsert(elements);

            var queryJob = new BenchmarkRangeQueryJob
            {
                Octree = octree,
                Bounds = new AABB { Center = 100, Extents = new float3(200, 1000, 200) },
                Results = new NativeList<OctElement<int>>(1000, Allocator.TempJob),
                Iterations = 1000
            };

            var sw = Stopwatch.StartNew();
            queryJob.Run();
            sw.Stop();
            Debug.Log($"1k range queries: {sw.Elapsed.TotalMilliseconds:F3}ms, results: {queryJob.Results.Length}");

            queryJob.Results.Dispose();
            octree.Dispose();
            elements.Dispose();
        }
    }
}
