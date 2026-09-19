using System.Collections.Generic;
using UnityEngine;

namespace UnityMediaRecorder
{
    // Selects how the recorder chooses the next camera in a sequence.
    public enum CameraSequenceOrder
    {
        Sequential,
        Random
    }

    // Lists transitions that may be selected between two cameras.
    public enum CameraSequenceTransition
    {
        CrossFade,
        NoTransition
    }

    // Configures several Unity cameras as one edited video stream.
    public sealed class CameraSequenceSettings
    {
        public IReadOnlyList<VideoSequenceSource> Sources { get; set; }
        public IReadOnlyList<Camera> Cameras { get; set; }
        public bool IncludeScreen { get; set; }
        public CameraSequenceOrder Order { get; set; } = CameraSequenceOrder.Sequential;
        public float MinimumShotDurationSeconds { get; set; } = 4f;
        public float MaximumShotDurationSeconds { get; set; } = 8f;
        public float CrossFadeDurationSeconds { get; set; } = 0.5f;
        public IReadOnlyList<CameraSequenceTransition> Transitions { get; set; } = new[] { CameraSequenceTransition.CrossFade };
        public int? RandomSeed { get; set; }
    }
}
