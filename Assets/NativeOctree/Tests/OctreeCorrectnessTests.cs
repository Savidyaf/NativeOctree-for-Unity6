using NUnit.Framework;
using NativeOctree;
using Unity.Collections;
using Unity.Mathematics;

namespace NativeOctree.Tests
{
    public class OctreeCorrectnessTests
    {
        static AABB DefaultBounds => new AABB { Center = 0, Extents = 1000 };

        NativeArray<OctElement<int>> CreateElements(float3[] positions, Allocator allocator = Allocator.TempJob)
        {
            var elements = new NativeArray<OctElement<int>>(positions.Length, allocator);
            for (int i = 0; i < positions.Length; i++)
                elements[i] = new OctElement<int> { pos = positions[i], element = i };
            return elements;
        }

        [Test]
        public void BulkInsert_ThenQueryAll_ReturnsAllElements()
        {
            var rng = new Unity.Mathematics.Random(42);
            var positions = new float3[500];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = rng.NextFloat3(-900, 900);

            var elements = CreateElements(positions);
            var octree = new NativeOctree<int>(DefaultBounds, Allocator.TempJob);
            octree.ClearAndBulkInsert(elements);

            var results = new NativeList<OctElement<int>>(500, Allocator.TempJob);
            octree.RangeQuery(DefaultBounds, results);

            Assert.AreEqual(positions.Length, results.Length, "Query over full bounds should return all elements.");

            results.Dispose();
            octree.Dispose();
            elements.Dispose();
        }

        [Test]
        public void BulkInsert_KnownPositions_QuerySmallRegion()
        {
            var positions = new float3[]
            {
                new float3(10, 10, 10),
                new float3(20, 20, 20),
                new float3(-500, -500, -500),
                new float3(900, 900, 900),
            };

            var elements = CreateElements(positions);
            var octree = new NativeOctree<int>(DefaultBounds, Allocator.TempJob);
            octree.ClearAndBulkInsert(elements);

            var queryBounds = new AABB { Center = new float3(15, 15, 15), Extents = 15 };
            var results = new NativeList<OctElement<int>>(10, Allocator.TempJob);
            octree.RangeQuery(queryBounds, results);

            Assert.AreEqual(2, results.Length, "Should find exactly the two elements at (10,10,10) and (20,20,20).");

            results.Dispose();
            octree.Dispose();
            elements.Dispose();
        }

        [Test]
        public void EmptyOctree_QueryReturnsNothing()
        {
            var octree = new NativeOctree<int>(DefaultBounds, Allocator.TempJob);
            var elements = new NativeArray<OctElement<int>>(0, Allocator.TempJob);
            octree.ClearAndBulkInsert(elements);

            var results = new NativeList<OctElement<int>>(10, Allocator.TempJob);
            octree.RangeQuery(DefaultBounds, results);

            Assert.AreEqual(0, results.Length);

            results.Dispose();
            octree.Dispose();
            elements.Dispose();
        }

        [Test]
        public void SingleElement_InsertAndQuery()
        {
            var positions = new float3[] { new float3(100, 200, 300) };
            var elements = CreateElements(positions);
            var octree = new NativeOctree<int>(DefaultBounds, Allocator.TempJob);
            octree.ClearAndBulkInsert(elements);

            var results = new NativeList<OctElement<int>>(10, Allocator.TempJob);
            octree.RangeQuery(DefaultBounds, results);

            Assert.AreEqual(1, results.Length);
            Assert.AreEqual(0, results[0].element);

            results.Dispose();
            octree.Dispose();
            elements.Dispose();
        }

        [Test]
        public void MultipleBulkInserts_CleanState()
        {
            var octree = new NativeOctree<int>(DefaultBounds, Allocator.TempJob);

            var pos1 = new float3[] { new float3(10, 10, 10), new float3(20, 20, 20) };
            var elem1 = CreateElements(pos1);
            octree.ClearAndBulkInsert(elem1);

            var results = new NativeList<OctElement<int>>(10, Allocator.TempJob);
            octree.RangeQuery(DefaultBounds, results);
            Assert.AreEqual(2, results.Length);
            results.Clear();

            var pos2 = new float3[] { new float3(50, 50, 50) };
            var elem2 = CreateElements(pos2);
            octree.ClearAndBulkInsert(elem2);

            octree.RangeQuery(DefaultBounds, results);
            Assert.AreEqual(1, results.Length, "Second bulk insert should replace the first, not accumulate.");

            results.Dispose();
            octree.Dispose();
            elem1.Dispose();
            elem2.Dispose();
        }

        [Test]
        public void BulkInsert_LargeCount_NoErrors()
        {
            var rng = new Unity.Mathematics.Random(123);
            var positions = new float3[20000];
            for (int i = 0; i < positions.Length; i++)
                positions[i] = rng.NextFloat3(-900, 900);

            var elements = CreateElements(positions);
            var octree = new NativeOctree<int>(DefaultBounds, Allocator.TempJob);
            octree.ClearAndBulkInsert(elements);

            var results = new NativeList<OctElement<int>>(20000, Allocator.TempJob);
            octree.RangeQuery(DefaultBounds, results);

            Assert.AreEqual(positions.Length, results.Length);

            results.Dispose();
            octree.Dispose();
            elements.Dispose();
        }

        [Test]
        public void Query_NoOverlap_ReturnsEmpty()
        {
            var positions = new float3[]
            {
                new float3(100, 100, 100),
                new float3(200, 200, 200),
            };
            var elements = CreateElements(positions);
            var octree = new NativeOctree<int>(DefaultBounds, Allocator.TempJob);
            octree.ClearAndBulkInsert(elements);

            var queryBounds = new AABB { Center = new float3(-500, -500, -500), Extents = 10 };
            var results = new NativeList<OctElement<int>>(10, Allocator.TempJob);
            octree.RangeQuery(queryBounds, results);

            Assert.AreEqual(0, results.Length);

            results.Dispose();
            octree.Dispose();
            elements.Dispose();
        }
    }
}
