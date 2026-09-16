using FFmpegMediaWriter;

namespace UnityMediaRecorder
{
    // Describes one recording session without depending on a game or configuration framework.
    public sealed class RecordingSettings
    {
        public string FfmpegPath { get; set; }
        public string TemporaryContainerPath { get; set; }
        public string ArchivePath { get; set; }
        public bool KeepIntermediateFile { get; set; }
        public bool GeneratePreviewImage { get; set; }
        public string PreviewImagePath { get; set; }
        public string OutputPath { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public int MaximumFrameRate { get; set; } = 60;
        public int AntiAliasingSamples { get; set; } = 1;
        public VideoEncodingQuality EncodingQuality { get; set; } = VideoEncodingQuality.Highest;
        public int NativeEncodingPreset { get; set; } // Zero retains the quality-based default; NVENC accepts 1–7.
        public VideoStreamFormat VideoStreamFormat { get; set; } = VideoStreamFormat.H264;
        public bool FlipVertically { get; set; }
        public bool CaptureScreen { get; set; }
    }
}
