using Unity.Collections;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;

namespace NativeOctree.Drawing
{
    public class OctreeDrawer : EditorWindow
    {
        [MenuItem("Window/OctreeDrawer")]
        static void Init()
        {
            GetWindow(typeof(OctreeDrawer)).Show();
        }

        public static void Draw<T>(NativeOctree<T> octree) where T : unmanaged
        {
            var window = (OctreeDrawer)GetWindow(typeof(OctreeDrawer));
            window.DoDraw(octree, default, default);
        }

        public static void DrawWithResults<T>(NativeOctree<T> octree, NativeList<OctElement<T>> results, AABB bounds) where T : unmanaged
        {
            var window = (OctreeDrawer)GetWindow(typeof(OctreeDrawer));
            window.DoDraw(octree, results, bounds);
        }

        [SerializeField]
        Color[][] pixels;

        void DoDraw<T>(NativeOctree<T> octree, NativeList<OctElement<T>> results, AABB bounds) where T : unmanaged
        {
            pixels = new Color[256][];
            for (var i = 0; i < pixels.Length; i++)
                pixels[i] = new Color[256];

            NativeOctreeDrawing.Draw(octree, results, bounds, pixels);
        }

        void OnGUI()
        {
            if (pixels == null) return;

            var texture = new Texture2D(256, 256);
            for (var x = 0; x < pixels.Length; x++)
            {
                for (int y = 0; y < pixels[x].Length; y++)
                {
                    texture.SetPixel(x, y, pixels[x][y]);
                }
            }
            texture.Apply();
            GUI.DrawTexture(new Rect(0, 0, position.width, position.height), texture);
        }
    }
}
