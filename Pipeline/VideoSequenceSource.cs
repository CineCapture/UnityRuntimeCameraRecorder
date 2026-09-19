using System;
using UnityEngine;

namespace UnityMediaRecorder
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

        private VideoSequenceSource(SourceKind kind, Camera camera, Texture texture)
        {
            Kind = kind;
            Camera = camera;
            Texture = texture;
        }

        internal SourceKind Kind { get; }
        internal Camera Camera { get; }
        internal Texture Texture { get; }

        // Creates a source rendered from one Unity camera.
        public static VideoSequenceSource FromCamera(Camera camera)
        {
            return new VideoSequenceSource(SourceKind.Camera, camera ?? throw new ArgumentNullException(nameof(camera)), null);
        }

        // Creates a source that reads the current content of a texture.
        public static VideoSequenceSource FromTexture(Texture texture)
        {
            return new VideoSequenceSource(SourceKind.Texture, null, texture ?? throw new ArgumentNullException(nameof(texture)));
        }

        // Creates a source from the completed player frame, including UI and cursor.
        public static VideoSequenceSource FromScreen()
        {
            return new VideoSequenceSource(SourceKind.Screen, null, null);
        }
    }
}
