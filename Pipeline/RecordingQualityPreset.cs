using System;

namespace UnityRuntimeCameraRecorder
{
    public enum RecordingQualityPreset { Low, Medium, High }

    // SDR constant-QP profile; video bitrate is determined by scene complexity.
    public sealed class RecordingQualityProfile
    {
        private RecordingQualityProfile(int baseQp, int reduction, int audioBitRate, int nativeEncodingPreset)
        { BaseQuantizationParameter = baseQp; ResolutionReduction = reduction; AudioBitRate = audioBitRate; NativeEncodingPreset = nativeEncodingPreset; }
        public int BaseQuantizationParameter { get; }
        public int ResolutionReduction { get; }
        public int QuantizationParameter => Math.Max(1, Math.Min(51, BaseQuantizationParameter - ResolutionReduction));
        public int VideoBitRate => 0;
        public int MaximumVideoBitRate => 0;
        public int VbvBufferSize => 0;
        public int NativeEncodingPreset { get; }
        public int ConstantQuality => 0;
        public string RateControl => "cqp";
        public int AudioBitRate { get; }
        public int AudioSampleRate => 48000;
        public int AudioChannels => 2;
        public static RecordingQualityProfile FromPreset(RecordingQualityPreset preset, int width = 1920, int height = 1080, int frameRate = 60)
        {
            if (width <= 0 || height <= 0 || (width & 1) != 0 || (height & 1) != 0)
                throw new ArgumentException("SDR 4:2:0 requires positive, even video dimensions.");
            if (frameRate <= 0) throw new ArgumentOutOfRangeException(nameof(frameRate));
            int baseQp;
            switch (preset)
            {
                case RecordingQualityPreset.Low: baseQp = 27; break;
                case RecordingQualityPreset.Medium: baseQp = 23; break;
                case RecordingQualityPreset.High: baseQp = 16; break;
                default: throw new ArgumentOutOfRangeException(nameof(preset));
            }
            double diagonal = Math.Sqrt((double)width * width + (double)height * height);
            int reduction = (int)Math.Floor((1 - Math.Min(2000, diagonal) / 2000) * 10);
            return new RecordingQualityProfile(baseQp, reduction, preset == RecordingQualityPreset.Low ? 128000 : 192000, 5);
        }
    }
}
