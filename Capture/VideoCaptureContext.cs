using System;
using UnityEngine;

namespace UnityMediaRecorder
{
    // Provides a video backend with session inputs and format-neutral output callbacks.
    public sealed class VideoCaptureContext
    {
        private readonly Func<byte[], long, bool> _writePacket;

        // Creates one immutable backend session context.
        internal VideoCaptureContext(
            Camera camera,
            int width,
            int height,
            int maximumFrameRate,
            int antiAliasingSamples,
            VideoEncodingQuality encodingQuality,
            bool flipVertically,
            RenderTexture preparedTarget,
            Func<byte[], long, bool> writePacket)
        {
            Camera = camera;
            Width = width;
            Height = height;
            MaximumFrameRate = maximumFrameRate;
            AntiAliasingSamples = antiAliasingSamples;
            EncodingQuality = encodingQuality;
            FlipVertically = flipVertically;
            PreparedTarget = preparedTarget;
            _writePacket = writePacket;
        }

        public Camera Camera { get; }
        public int Width { get; }
        public int Height { get; }
        public int MaximumFrameRate { get; }
        public int AntiAliasingSamples { get; }
        public VideoEncodingQuality EncodingQuality { get; }
        public bool FlipVertically { get; }
        public RenderTexture PreparedTarget { get; }

        // Writes one indivisible encoded packet with its monotonic presentation timestamp.
        public bool WritePacket(byte[] data, long timestampMicroseconds)
        {
            return _writePacket(data, timestampMicroseconds);
        }
    }
}
