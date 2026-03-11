using System.Runtime.InteropServices;

namespace NativeOctree
{
    /// <summary>
    /// Internal node in the octree's flat node array.
    /// <para>
    /// Layout: 12 bytes (with padding). The 3 padding bytes after <see cref="isLeaf"/>
    /// are reserved for future flags (e.g., tombstone tracking for incremental removal).
    /// </para>
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct OctNode
    {
        /// <summary>Index into the elements array where this leaf's elements begin. Only valid when <see cref="isLeaf"/> is true.</summary>
        public int firstChildIndex;

        /// <summary>Number of elements stored in this leaf node.</summary>
        public int count;

        /// <summary>Whether this node is a leaf that contains elements.</summary>
        public bool isLeaf;
    }
}
