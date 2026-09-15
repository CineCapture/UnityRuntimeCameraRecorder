using System;
using UnityEngine;

namespace Landoria.UnityMediaRecorder
{
    // Provides a video backend with session inputs and format-neutral output callbacks.
    public sealed class VideoCaptureContext
    {
        private readonly Func<byte[], bool> _writeFrame;
        private readonly Func<byte[], long, bool> _writePacket;

        // Creates one immutable backend session context.
        internal VideoCaptureContext(
            Camera camera,
            int width,
            int height,
            int maximumFrameRate,
            bool flipVertically,
            RenderTexture preparedTarget,
            Func<byte[], bool> writeFrame,
            Func<byte[], long, bool> writePacket)
        {
            Camera = camera;
            Width = width;
            Height = height;
            MaximumFrameRate = maximumFrameRate;
            FlipVertically = flipVertically;
            PreparedTarget = preparedTarget;
            _writeFrame = writeFrame;
            _writePacket = writePacket;
        }

        public Camera Camera { get; }
        public int Width { get; }
        public int Height { get; }
        public int MaximumFrameRate { get; }
        public bool FlipVertically { get; }
        public RenderTexture PreparedTarget { get; }

        // Writes one complete raw frame without a presentation timestamp.
        public bool WriteFrame(byte[] data)
        {
            return _writeFrame(data);
        }

        // Writes one indivisible encoded packet with its monotonic presentation timestamp.
        public bool WritePacket(byte[] data, long timestampMicroseconds)
        {
            return _writePacket(data, timestampMicroseconds);
        }
    }
}
