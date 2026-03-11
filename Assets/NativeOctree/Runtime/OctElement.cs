using Unity.Mathematics;

namespace NativeOctree
{
    /// <summary>
    /// A point element stored in the octree, pairing a 3D position with arbitrary unmanaged data.
    /// </summary>
    /// <typeparam name="T">The payload type. Must be unmanaged for Burst compatibility.</typeparam>
    public struct OctElement<T> where T : unmanaged
    {
        public float3 pos;
        public T element;
    }
}
