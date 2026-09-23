using System;
using System.Collections.Generic;

namespace UnityRuntimeCameraRecorder
{
    // Selects how the recorder chooses the next source in a sequence.
    public enum VideoSequenceOrder
    {
        Sequential,
        Random
    }

    // Lists transitions that may be selected between two video sources.
    public enum VideoSequenceTransition
    {
        CrossFade,
        NoTransition
    }

    // Configures one or more explicit sources as one edited video stream.
    public sealed class VideoSequenceSettings
    {
        public IReadOnlyList<VideoSequenceSource> Sources { get; set; }
        public VideoSequenceOrder Order { get; set; } = VideoSequenceOrder.Sequential;
        public float MinimumShotDurationSeconds { get; set; } = 4f;
        public float MaximumShotDurationSeconds { get; set; } = 8f;
        public float CrossFadeDurationSeconds { get; set; } = 0.5f;
        public IReadOnlyList<VideoSequenceTransition> Transitions { get; set; } = new[] { VideoSequenceTransition.CrossFade };
        public int? RandomSeed { get; set; }
        public Func<int, bool> CanActivateSource { get; set; }
    }
}
