using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace UnityRuntimeCameraRecorder
{
    // Draws a visible mouse pointer into a screen-capture render texture.
    internal sealed class ScreenCursorOverlay : IDisposable
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct NativePoint
        {
            internal int X;
            internal int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeRectangle
        {
            internal int Left;
            internal int Top;
            internal int Right;
            internal int Bottom;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct NativeCursorInfo
        {
            internal int Size;
            internal int Flags;
            internal IntPtr Handle;
            internal NativePoint Position;
        }

        private const int CursorWidth = 32;
        private const int CursorHeight = 44;
        private const int CursorShowing = 1;
        private static readonly Vector2[] CursorShape =
        {
            new Vector2(1, 1), new Vector2(1, 34), new Vector2(10, 26),
            new Vector2(17, 42), new Vector2(24, 39), new Vector2(17, 24), new Vector2(30, 24)
        };
        private readonly Texture2D _texture;

        // Creates a small high-contrast arrow cursor with a transparent background.
        internal ScreenCursorOverlay()
        {
            _texture = new Texture2D(CursorWidth, CursorHeight, TextureFormat.RGBA32, false, true)
            {
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };
            Color32[] pixels = new Color32[CursorWidth * CursorHeight];
            for (int y = 0; y < CursorHeight; y++)
            {
                for (int x = 0; x < CursorWidth; x++)
                {
                    pixels[y * CursorWidth + x] = ResolvePixel(x, y);
                }
            }
            _texture.SetPixels32(pixels);
            _texture.Apply(false, true);
        }

        // Draws the cursor at its Unity screen position while preserving the active target.
        internal void Draw(RenderTexture target)
        {
            if (!TryGetCursorPosition(target, out Vector2 position, out float scale))
            {
                return;
            }
            RenderTexture previous = RenderTexture.active;
            try
            {
                Graphics.SetRenderTarget(target);
                GL.PushMatrix();
                GL.LoadPixelMatrix(0, target.width, target.height, 0);
                float cursorHeight = CursorHeight * scale;
                Graphics.DrawTexture(new Rect(position.x, target.height - position.y - cursorHeight, CursorWidth * scale, cursorHeight), _texture);
                GL.PopMatrix();
            }
            finally
            {
                RenderTexture.active = previous;
            }
        }

        // Releases the generated cursor texture.
        public void Dispose()
        {
            UnityEngine.Object.Destroy(_texture);
        }

        // Returns a black outline, white fill, or transparent pixel for the arrow shape.
        private static Color32 ResolvePixel(int x, int y)
        {
            if (!IsInsideCursor(x, y))
            {
                return new Color32(0, 0, 0, 0);
            }
            bool border = !IsInsideCursor(x - 1, y) || !IsInsideCursor(x + 1, y) ||
                !IsInsideCursor(x, y - 1) || !IsInsideCursor(x, y + 1);
            if (!border)
            {
                return new Color32(255, 255, 255, 255);
            }
            return new Color32(0, 0, 0, 255);
        }

        // Converts the visible Windows cursor from client coordinates to capture pixels.
        private static bool TryGetCursorPosition(RenderTexture target, out Vector2 position, out float scale)
        {
            position = default;
            scale = 1f;
            NativeCursorInfo cursor = new NativeCursorInfo { Size = Marshal.SizeOf<NativeCursorInfo>() };
            IntPtr window = GetActiveWindow();
            if (window == IntPtr.Zero || !GetCursorInfo(ref cursor) || (cursor.Flags & CursorShowing) == 0 ||
                !ScreenToClient(window, ref cursor.Position) || !GetClientRect(window, out NativeRectangle client))
            {
                return false;
            }
            int width = client.Right - client.Left;
            int height = client.Bottom - client.Top;
            if (width <= 0 || height <= 0 || cursor.Position.X < 0 || cursor.Position.Y < 0 || cursor.Position.X >= width || cursor.Position.Y >= height)
            {
                return false;
            }
            position = new Vector2(cursor.Position.X * target.width / (float)width, cursor.Position.Y * target.height / (float)height);
            scale = Mathf.Max(1f, target.width / (float)width, target.height / (float)height);
            return true;
        }

        // Tests whether one texture pixel lies inside the arrow polygon.
        private static bool IsInsideCursor(int x, int y)
        {
            bool inside = false;
            for (int current = 0, previous = CursorShape.Length - 1; current < CursorShape.Length; previous = current++)
            {
                Vector2 a = CursorShape[current];
                Vector2 b = CursorShape[previous];
                if ((a.y > y) != (b.y > y) && x < (b.x - a.x) * (y - a.y) / (b.y - a.y) + a.x)
                {
                    inside = !inside;
                }
            }
            return inside;
        }

        [DllImport("user32.dll")]
        // Returns the active window attached to the Unity main thread.
        private static extern IntPtr GetActiveWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        // Reads the current system cursor state and screen position.
        private static extern bool GetCursorInfo(ref NativeCursorInfo cursorInfo);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        // Converts a screen point into window-client coordinates.
        private static extern bool ScreenToClient(IntPtr window, ref NativePoint point);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        // Returns the drawable dimensions of the Unity player window.
        private static extern bool GetClientRect(IntPtr window, out NativeRectangle rectangle);
    }
}
