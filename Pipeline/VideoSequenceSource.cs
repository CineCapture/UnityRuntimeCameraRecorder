using System;
using UnityEngine;

namespace UnityRuntimeCameraRecorder
{
    // Describes one camera, screen, or texture used by a single-output sequence.
    public sealed class VideoSequenceSource
    {
        internal enum SourceKind
        {
            Camera,
            Screen,
            Texture
        }

        private VideoSequenceSource(SourceKind kind, Camera camera, Texture texture, bool captureCursor)
        {
            Kind = kind;
            Camera = camera;
            Texture = texture;
            CaptureCursor = captureCursor;
        }

        internal SourceKind Kind { get; }
        internal Camera Camera { get; }
        internal Texture Texture { get; }
        internal bool CaptureCursor { get; }

        // Creates a source rendered from one Unity camera.
        public static VideoSequenceSource FromCamera(Camera camera)
        {
            return new VideoSequenceSource(SourceKind.Camera, camera ?? throw new ArgumentNullException(nameof(camera)), null, false);
        }

        // Creates a source that reads the current content of a texture.
        public static VideoSequenceSource FromTexture(Texture texture)
        {
            return new VideoSequenceSource(SourceKind.Texture, null, texture ?? throw new ArgumentNullException(nameof(texture)), false);
        }

        // Creates a source from the completed player frame and optionally overlays the cursor.
        public static VideoSequenceSource FromScreen(bool captureCursor = true)
        {
            return new VideoSequenceSource(SourceKind.Screen, null, null, captureCursor);
        }
    }
}
