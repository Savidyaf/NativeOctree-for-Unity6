using NUnit.Framework;
using NativeOctree;
using Unity.Mathematics;

namespace NativeOctree.Tests
{
    public class MortonCodeTests
    {
        static AABB DefaultBounds => new AABB { Center = 0, Extents = 1000 };
        const int DefaultDepth = 6;

        [SetUp]
        public void EnsureInitialized()
        {
            LookupTables.Initialize();
        }

        [Test]
        public void Encode_CenterPosition_ProducesConsistentCode()
        {
            var code = MortonCodeUtil.Encode(float3.zero, DefaultBounds, DefaultDepth);
            Assert.GreaterOrEqual(code, 0, "Morton code should be non-negative.");
        }

        [Test]
        public void Encode_SamePosition_ProducesSameCode()
        {
            var pos = new float3(100, 200, 300);
            var code1 = MortonCodeUtil.Encode(pos, DefaultBounds, DefaultDepth);
            var code2 = MortonCodeUtil.Encode(pos, DefaultBounds, DefaultDepth);
            Assert.AreEqual(code1, code2);
        }

        [Test]
        public void Encode_DifferentPositions_ProduceDifferentCodes()
        {
            var codeA = MortonCodeUtil.Encode(new float3(-800, -800, -800), DefaultBounds, DefaultDepth);
            var codeB = MortonCodeUtil.Encode(new float3(800, 800, 800), DefaultBounds, DefaultDepth);
            Assert.AreNotEqual(codeA, codeB, "Distant positions should map to different morton codes.");
        }

        [Test]
        public void Encode_OutOfBoundsPosition_ClampedSafely()
        {
            Assert.DoesNotThrow(() =>
            {
                MortonCodeUtil.Encode(new float3(5000, 5000, 5000), DefaultBounds, DefaultDepth);
            }, "Positions outside bounds should not throw (clamped to valid range).");

            Assert.DoesNotThrow(() =>
            {
                MortonCodeUtil.Encode(new float3(-5000, -5000, -5000), DefaultBounds, DefaultDepth);
            });
        }

        [Test]
        public void Encode_SymmetricPositions_ProduceValidCodes()
        {
            var posX = new float3(500, 0, 0);
            var negX = new float3(-500, 0, 0);
            var codePos = MortonCodeUtil.Encode(posX, DefaultBounds, DefaultDepth);
            var codeNeg = MortonCodeUtil.Encode(negX, DefaultBounds, DefaultDepth);

            Assert.GreaterOrEqual(codePos, 0);
            Assert.GreaterOrEqual(codeNeg, 0);
            Assert.AreNotEqual(codePos, codeNeg, "Symmetric positions on X axis should have different codes.");
        }
    }
}
