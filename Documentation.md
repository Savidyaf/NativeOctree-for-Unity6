# NativeOctree Technical Documentation

## Overview

NativeOctree is a Burst-compatible octree native container for Unity DOTS. It provides fast spatial
indexing of 3D point data with support for bulk insertion and AABB range queries, designed to run
inside the Unity Job System with full Burst compilation.

The octree stores points (not volumes) and uses morton codes (Z-order curves) for cache-friendly
bulk insertion. All internal memory is unmanaged, meaning the container can be passed directly into
Burst-compiled jobs without allocating on the managed heap.

---

## Project Structure

```
Assets/NativeOctree/
  Runtime/                           Core library (Burst-compiled, no UnityEngine dependency)
    NativeOctree.Runtime.asmdef
    AssemblyInfo.cs                  InternalsVisibleTo declarations
    NativeOctree.cs                  Main struct: fields, constructor, Clear, Dispose, RangeQuery
    NativeOctreeBulkInsert.cs        Partial: ClearAndBulkInsert, RecursivePrepareLeaves
    NativeOctreeRangeQuery.cs        Partial: OctreeRangeQuery nested struct
    OctElement.cs                    OctElement<T> -- point + payload pair
    OctNode.cs                       OctNode -- internal tree node
    OctreeMath.cs                    Static spatial math (GetChildBounds, Intersects, Contains)
    MortonCodeUtil.cs                Static morton code encoder
    LookupTables.cs                  SharedStatic lookup tables for Burst
    OctreeJobs.cs                    Pre-built Burst jobs (AddBulkJob, RangeQueryJob)

  Drawing/                           Editor-only debug visualization
    NativeOctree.Drawing.asmdef
    NativeOctreeDrawing.cs           Debug.DrawLine visualization + 2D texture projection
    OctreeDrawer.cs                  EditorWindow for viewing octree state

  Tests/                             Edit-mode test suite
    NativeOctree.Tests.asmdef
    OctreeCorrectnessTests.cs        Insertion and query correctness
    OctreeMathTests.cs               Spatial math unit tests
    MortonCodeTests.cs               Morton encoding unit tests
    OctreeBenchmarkTests.cs          Performance benchmarks
```

### Assembly Dependencies

```
NativeOctree.Runtime
  References: Unity.Burst, Unity.Collections, Unity.Mathematics, Unity.Mathematics.Extensions

NativeOctree.Drawing  (Editor only)
  References: NativeOctree.Runtime + above + UnityEngine

NativeOctree.Tests    (Editor only, test runner)
  References: NativeOctree.Runtime, NativeOctree.Drawing + above
```

`AssemblyInfo.cs` declares `[InternalsVisibleTo]` for both Drawing and Tests, allowing them to
access internal types like `OctNode` and internal accessors on `NativeOctree<T>`.

---

## Core Data Types

### OctElement\<T\>

```csharp
public struct OctElement<T> where T : unmanaged
{
    public float3 pos;     // 3D world-space position
    public T element;      // Arbitrary payload (entity ID, index, etc.)
}
```

The fundamental unit stored in the octree. The generic constraint `where T : unmanaged` ensures
Burst compatibility. Common payload types: `int` (entity index), `Entity`, or any blittable struct.

### OctNode

```csharp
[StructLayout(LayoutKind.Sequential)]
internal struct OctNode
{
    public int firstChildIndex;   // Index into the elements array (leaf only)
    public int count;             // Number of elements in this leaf
    public bool isLeaf;           // Whether this node contains elements
}
```

Size: 12 bytes (9 bytes of data + 3 bytes padding). The padding after `isLeaf` is intentionally
reserved for future flags (e.g., tombstone tracking when individual removal is implemented).

Nodes are stored in a flat contiguous array indexed by morton-code-derived offsets. Non-leaf nodes
have `isLeaf = false` and `count = 0`. Leaf nodes point into the elements array via
`firstChildIndex`.

### NativeOctree\<T\>

The main container. Implemented as a partial struct split across three files:

| File | Responsibility |
|------|---------------|
| `NativeOctree.cs` | Fields, constructor, `Clear()`, `Dispose()`, `RangeQuery()` |
| `NativeOctreeBulkInsert.cs` | `ClearAndBulkInsert()`, `IncrementIndex()`, `RecursivePrepareLeaves()` |
| `NativeOctreeRangeQuery.cs` | Nested `OctreeRangeQuery` struct with recursive traversal |

Internal storage consists of three unmanaged allocations:

| Field | Type | Purpose |
|-------|------|---------|
| `elements` | `UnsafeList<OctElement<T>>*` | Flat array of all elements, grouped by leaf |
| `lookup` | `UnsafeList<int>*` | Per-node element count (inclusive of descendants) |
| `nodes` | `UnsafeList<OctNode>*` | Node metadata (leaf flag, element range) |

All three pointer fields are annotated with `[NoAlias]` to tell Burst they never overlap in memory,
enabling better optimization (reordering, vectorization, constant propagation).

---

## Algorithms

### Morton Code Encoding

Morton codes (Z-order curves) map 3D coordinates to a 1D integer by interleaving the bits of the
quantized X, Y, Z values. This preserves spatial locality: nearby 3D points tend to have nearby
morton codes.

**Encoding steps** (in `MortonCodeUtil.EncodeScaled`):

1. **Translate** to local space: `localPos = worldPos - bounds.Center`
2. **Shift** to positive quadrant: `pos = (localPos + bounds.Extents) * 0.5`
3. **Scale** to depth grid: `pos *= depthLookup[maxDepth] / bounds.Extents`
4. **Clamp** to valid range: `pos = clamp(pos, 0, 255)`
5. **Interleave** via lookup table: `MortonLookup[x] | (MortonLookup[y] << 1) | (MortonLookup[z] << 2)`

The lookup table (`MortonLookup`) contains 256 pre-computed bit-spread values. Each 8-bit input
coordinate is expanded to a 24-bit value with bits spaced 3 apart, ready for OR-combining with the
other two axes.

### Bulk Insertion (ClearAndBulkInsert)

Bulk insertion is a three-phase process:

**Phase 1 -- Compute morton codes:**
For each element, compute its morton code based on position within the octree bounds. The scaling
factor is pre-computed once (`depthExtentsScaling`) and reused for all elements.

**Phase 2 -- Count elements per node:**
For each element, walk from root to maximum depth, incrementing the count in the `lookup` array
at each node along the path. After this phase, every node in the flat array knows how many elements
fall within its spatial region (including all descendants).

```
For each element:
    atIndex = 0  (root)
    For depth = 0 to maxDepth:
        lookup[atIndex]++
        atIndex = next child index based on morton code bits at this depth
```

**Phase 3 -- Prepare leaf nodes (RecursivePrepareLeaves):**
Recursively traverse the tree. At each node:
- If `elementCount > maxLeafElements` and `depth < maxDepth`: recurse deeper
- Otherwise: mark as leaf, assign `firstChildIndex = elementsCount`, advance `elementsCount`

This determines which nodes become leaves and pre-allocates their element ranges.

**Phase 4 -- Place elements:**
For each element, walk from root to its leaf (same path as Phase 2). When a leaf is found, write
the element at `elements[firstChildIndex + count]` and increment the leaf's count.

### Node Indexing Scheme

Nodes across all depths are stored in a single flat array. The index of a child node is computed
from the parent's index plus an offset derived from the morton code bits at that depth level:

```
childIndex = parentIndex
           + DepthSizeLookup[remainingDepth] * mortonBits
           + 1  (offset for self)
```

`DepthSizeLookup[d]` gives the total number of nodes in a subtree of depth `d`. This allows O(1)
child lookup without pointer chasing.

### Child Spatial Mapping

The 3 bits extracted from the morton code at each depth map directly to spatial octants:

| Bit | Axis | 0 | 1 |
|-----|------|---|---|
| bit 0 | X | negative | positive |
| bit 1 | Y | negative | positive |
| bit 2 | Z | negative | positive |

This convention is consistent between `MortonCodeUtil` (insertion) and `OctreeMath.GetChildBounds`
(queries), so no axis inversion is required.

### Range Query

The range query recursively traverses the octree, testing each child's AABB against the query AABB:

```
RecursiveRangeQuery(parentBounds, parentContained, offset, depth):
    For each of 8 children:
        elementCount = lookup[child index]
        If elementCount == 0: skip (no elements in subtree)

        childBounds = GetChildBounds(parentBounds, childIndex)

        If parent already fully contained by query:
            contained = true
        Else if query fully contains child:
            contained = true
        Else if query does not intersect child:
            skip (prune)

        If node has too many elements and depth < maxDepth:
            recurse deeper
        Else (node is a leaf with elements):
            If contained: bulk memcpy all elements to results
            Else: per-element point-in-AABB test
```

Key optimizations:
- Empty children (`elementCount == 0`) are skipped before any AABB math, avoiding `GetChildBounds`
  and intersection tests for the majority of children in sparse octrees.
- When a child is fully contained by the query AABB, all elements in that subtree are guaranteed
  to match. This allows a `UnsafeUtility.MemCpy` of the entire leaf's elements without testing
  each one individually. The `contained` flag propagates to all deeper children, turning the
  per-element test into a bulk copy for large query regions.
- Raw pointers to `lookup`, `nodes`, and `elements` are cached at query start to avoid repeated
  double indirection (struct field → UnsafeList pointer → Ptr) during recursive traversal.

---

## Lookup Tables

Three pre-computed tables are stored in `SharedStatic` memory for Burst-safe access:

| Table | Size | Purpose |
|-------|------|---------|
| `MortonLookup` | 256 x uint | Bit-interleaving for 8-bit input coordinates |
| `DepthSizeLookup` | 10 x int | Total nodes in subtree at each depth (cumulative 8^d sums) |
| `DepthLookup` | 9 x int | Grid resolution at each depth (2^d) |

`SharedStatic<T>` stores data in unmanaged memory accessible from Burst without crossing the
managed/native boundary. Tables are initialized once via `LookupTables.Initialize()`, called from
the `NativeOctree<T>` constructor.

---

## Performance Characteristics

### Time Complexity

| Operation | Complexity | Notes |
|-----------|-----------|-------|
| `ClearAndBulkInsert` | O(N * maxDepth) | Three linear passes over N elements |
| `RangeQuery` (worst case) | O(8^maxDepth + K) | K = result count; prunes non-intersecting subtrees |
| `RangeQuery` (typical) | O(K + pruned nodes) | Spatial coherence makes pruning very effective |
| `Clear` | O(totalNodes) | MemClear of lookup and nodes arrays |
| Constructor | O(totalNodes) | Allocate and clear three arrays |

### Memory Usage

The node arrays are pre-allocated for all possible nodes at the configured depth:

| maxDepth | Total Nodes | lookup (bytes) | nodes (bytes) |
|----------|-------------|----------------|---------------|
| 4 | 4,681 | 18 KB | 55 KB |
| 5 | 37,449 | 146 KB | 439 KB |
| 6 | 299,593 | 1.1 MB | 3.4 MB |
| 7 | 2,396,745 | 9.2 MB | 27.4 MB |
| 8 | 19,173,961 | 73 MB | 219 MB |

The elements array grows dynamically to fit the inserted element count.

### Burst Optimizations

- `[NoAlias]` on pointer fields enables Burst to reorder memory accesses and avoid redundant loads
- `[MethodImpl(AggressiveInlining)]` on hot-path methods (`IncrementIndex`, `GetChildBounds`,
  `Intersects`, `EncodeScaled`) ensures they are inlined into the caller
- `SharedStatic<T>` lookup tables avoid the managed-to-native bridge penalty on every table read
- `DepthSizeLookup` pointer cached once per bulk insert and passed through, avoiding repeated
  `SharedStatic` resolution in hot loops
- Direct pointer indexing (`ptr[i]`) instead of `UnsafeUtility.ReadArrayElement` for cleaner
  Burst codegen; morton codes use raw `int*` via `NativeArrayUnsafeUtility.GetUnsafeReadOnlyPtr`
- `math.select` for branchless child bounds computation (compiles to conditional moves, no branches)
- `math.all(math.abs(...) < ...)` for vectorized AABB intersection (SIMD subtract + abs + compare
  + horizontal-AND, instead of three scalar chains with short-circuit branches)
- `math.clamp` for bounds-safe morton encoding without branch overhead
- `UnsafeUtility.MemCpy` for bulk element copying when a query AABB fully contains a leaf

---

## Usage

### Basic Example

```csharp
// Create octree with bounds and allocator
var bounds = new AABB { Center = float3.zero, Extents = 1000f };
var octree = new NativeOctree<int>(bounds, Allocator.TempJob, maxDepth: 6, maxLeafElements: 16);

// Prepare elements
var elements = new NativeArray<OctElement<int>>(count, Allocator.TempJob);
for (int i = 0; i < count; i++)
    elements[i] = new OctElement<int> { pos = positions[i], element = i };

// Bulk insert
octree.ClearAndBulkInsert(elements);

// Query
var results = new NativeList<OctElement<int>>(256, Allocator.TempJob);
var queryBounds = new AABB { Center = new float3(100, 0, 0), Extents = 50f };
octree.RangeQuery(queryBounds, results);

// Use results...

// Cleanup
results.Dispose();
octree.Dispose();
elements.Dispose();
```

### Using Jobs

```csharp
// Bulk insert job
var insertJob = new OctreeJobs.AddBulkJob<int>
{
    Elements = elements,
    Octree = octree
};
insertJob.Schedule().Complete();

// Range query job
var queryJob = new OctreeJobs.RangeQueryJob<int>
{
    Octree = octree,
    Bounds = queryBounds,
    Results = results
};
queryJob.Schedule().Complete();
```

### Parameter Tuning

| Parameter | Default | Guidance |
|-----------|---------|----------|
| `maxDepth` | 6 | Higher = finer spatial resolution but exponentially more memory. 6 is a good default for most use cases. Only go to 7-8 for very large worlds with millions of elements. |
| `maxLeafElements` | 16 | Lower = more subdivision, more nodes, faster queries on small regions. Higher = fewer nodes, better for bulk operations. 16 is a balanced default. |
| `bounds` | -- | Should tightly fit your data. Oversized bounds waste resolution. Must be uniform (equal extents on all axes). |

---

## Thread Safety

`NativeOctree<T>` follows the standard Unity native container safety model:

- **Single writer** OR **multiple concurrent readers** (not both simultaneously)
- In development builds (`ENABLE_UNITY_COLLECTIONS_CHECKS`), `AtomicSafetyHandle` enforces this
  at runtime and throws clear exceptions on misuse
- In release builds, safety checks are stripped for zero overhead
- Pass the octree into jobs as you would any native container; the job system handles scheduling

---

## Extending the System

### Adding a New Query Type (e.g., Raycast)

1. Create `NativeOctreeRaycastQuery.cs` as a new partial of `NativeOctree<T>`
2. Add a `RaycastQuery(Ray, NativeList<OctElement<T>>)` public method
3. Implement a nested struct (like `OctreeRangeQuery`) that recursively traverses the tree
4. Use `OctreeMath.GetChildBounds` for child AABB computation (shared with range query)
5. Add a `RayIntersectsAABB` method to `OctreeMath` for the ray-box intersection test
6. Add a corresponding job struct to `OctreeJobs`

### Adding Individual Add/Remove

The recommended approach preserves the fast bulk insert path:

1. Add a `pendingAdds` (`UnsafeList<OctElement<T>>`) buffer to `NativeOctree<T>`
2. Add a `tombstones` (`UnsafeBitArray`) parallel to the elements array
3. `Add(element)` appends to `pendingAdds`
4. `Remove(element)` sets the tombstone bit for that element's index
5. Queries check tombstones and also scan `pendingAdds`
6. `Rebuild()` merges pending adds, removes tombstoned elements, and calls `ClearAndBulkInsert`
7. The bulk insert hot path remains completely unchanged

### Adding a New Test File

1. Place in `Assets/NativeOctree/Tests/`
2. Use namespace `NativeOctree.Tests`
3. Internal types (`OctNode`, `LookupTables`, etc.) are accessible via `InternalsVisibleTo`
4. Use `Allocator.TempJob` for test allocations and dispose in the test body
