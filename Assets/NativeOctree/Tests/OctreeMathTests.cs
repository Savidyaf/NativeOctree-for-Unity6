using NUnit.Framework;
using NativeOctree;
using Unity.Mathematics;

namespace NativeOctree.Tests
{
    public class OctreeMathTests
    {
        static AABB UnitBounds => new AABB { Center = float3.zero, Extents = 1f };

        [Test]
        public void GetChildBounds_AllEightChildren_CoverParent()
        {
            var parent = UnitBounds;
            var expectedHalf = parent.Extents.x * 0.5f;

            for (int i = 0; i < 8; i++)
            {
                var child = OctreeMath.GetChildBounds(parent, i);
                Assert.AreEqual(expectedHalf, child.Extents.x, 1e-6f,
                    $"Child {i} extents should be half of parent.");
            }
        }

        [Test]
        public void GetChildBounds_ChildrenAreNonOverlapping()
        {
            var parent = new AABB { Center = float3.zero, Extents = 100f };

            for (int i = 0; i < 8; i++)
            {
                for (int j = i + 1; j < 8; j++)
                {
                    var a = OctreeMath.GetChildBounds(parent, i);
                    var b = OctreeMath.GetChildBounds(parent, j);
                    if (i != j)
                    {
                        Assert.IsFalse(OctreeMath.Intersects(a, b),
                            $"Children {i} and {j} should not overlap.");
                    }
                }
            }
        }

        [Test]
        public void GetChildBounds_SpecificChild_CorrectCenter()
        {
            var parent = new AABB { Center = float3.zero, Extents = 10f };
            var half = 5f;

            // Child 0 (0b000): all bits 0 → all axes negative
            var child0 = OctreeMath.GetChildBounds(parent, 0);
            Assert.AreEqual(-half, child0.Center.x, 1e-5f);
            Assert.AreEqual(-half, child0.Center.y, 1e-5f);
            Assert.AreEqual(-half, child0.Center.z, 1e-5f);

            // Child 7 (0b111): all bits 1 → all axes positive
            var child7 = OctreeMath.GetChildBounds(parent, 7);
            Assert.AreEqual(half, child7.Center.x, 1e-5f);
            Assert.AreEqual(half, child7.Center.y, 1e-5f);
            Assert.AreEqual(half, child7.Center.z, 1e-5f);
        }

        [Test]
        public void Intersects_Overlapping_ReturnsTrue()
        {
            var a = new AABB { Center = float3.zero, Extents = 10f };
            var b = new AABB { Center = new float3(5, 5, 5), Extents = 10f };
            Assert.IsTrue(OctreeMath.Intersects(a, b));
        }

        [Test]
        public void Intersects_NonOverlapping_ReturnsFalse()
        {
            var a = new AABB { Center = float3.zero, Extents = 5f };
            var b = new AABB { Center = new float3(100, 100, 100), Extents = 5f };
            Assert.IsFalse(OctreeMath.Intersects(a, b));
        }

        [Test]
        public void Intersects_TouchingEdge_ReturnsFalse()
        {
            var a = new AABB { Center = float3.zero, Extents = 5f };
            var b = new AABB { Center = new float3(10, 0, 0), Extents = 5f };
            Assert.IsFalse(OctreeMath.Intersects(a, b), "Edge-touching AABBs should not intersect (exclusive boundary).");
        }

        [Test]
        public void Contains_InnerFullyInside_ReturnsTrue()
        {
            var outer = new AABB { Center = float3.zero, Extents = 100f };
            var inner = new AABB { Center = new float3(10, 10, 10), Extents = 5f };
            Assert.IsTrue(OctreeMath.Contains(outer, inner));
        }

        [Test]
        public void Contains_InnerPartiallyOutside_ReturnsFalse()
        {
            var outer = new AABB { Center = float3.zero, Extents = 10f };
            var inner = new AABB { Center = new float3(8, 0, 0), Extents = 5f };
            Assert.IsFalse(OctreeMath.Contains(outer, inner));
        }

        [Test]
        public void ContainsPoint_Inside_ReturnsTrue()
        {
            var bounds = new AABB { Center = float3.zero, Extents = 10f };
            Assert.IsTrue(OctreeMath.Contains(bounds, new float3(5, 5, 5)));
        }

        [Test]
        public void ContainsPoint_Outside_ReturnsFalse()
        {
            var bounds = new AABB { Center = float3.zero, Extents = 10f };
            Assert.IsFalse(OctreeMath.Contains(bounds, new float3(15, 0, 0)));
        }
    }
}
