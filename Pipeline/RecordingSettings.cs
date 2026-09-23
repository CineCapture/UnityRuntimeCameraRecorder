using FFmpegMediaWriter;

namespace UnityRuntimeCameraRecorder
{
    // Describes one recording session without depending on a game or configuration framework.
    public sealed class RecordingSettings
    {
        public string FfmpegPath { get; set; }
        public string TemporaryContainerPath { get; set; }
        public bool GenerateStatistics { get; set; }
        public string StatisticsPath { get; set; }
        public string FfprobePath { get; set; }
        public string OutputPath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int MaximumFrameRate { get; set; } = 60;
        public int SourceAntiAliasingSamples { get; set; } = 1;
        public RecordingQualityPreset QualityPreset { get; set; } = RecordingQualityPreset.Highest;
        public VideoStreamFormat VideoStreamFormat { get; set; } = VideoStreamFormat.H264;
        public bool OptimizeForConcurrentEncoding { get; set; }
        // HDR sources are explicitly unsupported by the SDR profile.
        public bool CaptureHdr { get; set; }
        public bool? FlipVertically { get; set; }
    }
}
