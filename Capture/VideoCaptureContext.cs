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
            RecordingQualityProfile qualityProfile,
            bool optimizeForConcurrentEncoding,
            bool flipVertically,
            bool captureScreen,
            RenderTexture preparedTarget,
            Func<byte[], long, bool> writePacket)
        {
            Camera = camera;
            Width = width;
            Height = height;
            MaximumFrameRate = maximumFrameRate;
            AntiAliasingSamples = antiAliasingSamples;
            QualityProfile = qualityProfile;
            OptimizeForConcurrentEncoding = optimizeForConcurrentEncoding;
            FlipVertically = flipVertically;
            CaptureScreen = captureScreen;
            PreparedTarget = preparedTarget;
            _writePacket = writePacket;
        }

        public Camera Camera { get; }
        public int Width { get; }
        public int Height { get; }
        public int MaximumFrameRate { get; }
        public int AntiAliasingSamples { get; }
        public RecordingQualityProfile QualityProfile { get; }
        public bool OptimizeForConcurrentEncoding { get; }
        public bool FlipVertically { get; }
        public bool CaptureScreen { get; }
        public RenderTexture PreparedTarget { get; }

        // Writes one indivisible encoded packet with its monotonic presentation timestamp.
        public bool WritePacket(byte[] data, long timestampMicroseconds)
        {
            return _writePacket(data, timestampMicroseconds);
        }
    }
}
