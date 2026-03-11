
# Fork of Native Octree
This is based on https://github.com/marijnz/NativeOctree with few upgrades and improvements to work better with unity 6.

## Overview
NativeOctree is a Burst-compatible octree native container for Unity DOTS. It provides fast spatial indexing of 3D point data with support for bulk insertion and AABB range queries, designed to run inside the Unity Job System with full Burst compilation.
The octree stores points (not volumes) and uses morton codes (Z-order curves) for cache-friendly bulk insertion. All internal memory is unmanaged, meaning the container can be passed directly into Burst-compiled jobs without allocating on the managed heap.

## [Documentation](Documentation.md)


## Demo Tools 
- Includes a simple demo script that visualizes a dynamic octree with realtime querying performance.
<p align="center">
<img src="media/Demo.gif" width="500"/></br>
</p><p align="center">
<img src="media/Stats.gif" width="500"/></br>
</p>
