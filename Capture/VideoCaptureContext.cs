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
            int width,
            int height,
            int maximumFrameRate,
            RecordingQualityProfile qualityProfile,
            VideoSequenceSettings videoSequence,
            bool optimizeForConcurrentEncoding,
            bool flipVertically,
            Func<byte[], long, bool> writePacket)
        {
            Width = width;
            Height = height;
            MaximumFrameRate = maximumFrameRate;
            QualityProfile = qualityProfile;
            VideoSequence = videoSequence;
            OptimizeForConcurrentEncoding = optimizeForConcurrentEncoding;
            FlipVertically = flipVertically;
            _writePacket = writePacket;
        }

        public int Width { get; }
        public int Height { get; }
        public int MaximumFrameRate { get; }
        public RecordingQualityProfile QualityProfile { get; }
        public VideoSequenceSettings VideoSequence { get; }
        public bool OptimizeForConcurrentEncoding { get; }
        public bool FlipVertically { get; }

        // Writes one indivisible encoded packet with its monotonic presentation timestamp.
        public bool WritePacket(byte[] data, long timestampMicroseconds)
        {
            return _writePacket(data, timestampMicroseconds);
        }
    }
}
