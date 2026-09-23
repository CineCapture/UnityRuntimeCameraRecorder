using System;
using System.Linq;
using UnityRuntimeCameraRecorder;
using FFmpegMediaWriter;

static void Expect(bool condition, string message)
{
    if (!condition)
    {
        throw new Exception(message);
    }
}

static void Invalid(Action action)
{
    try
    {
        action();
    }
    catch (ArgumentException exception)
    {
        Console.Error.WriteLine("Expected invalid-input exception: " + exception.Message);
        return;
    }

    throw new Exception("Invalid input accepted");
}
foreach (var quality in Enum.GetValues<RecordingQualityPreset>())
{
    int baseline = quality == RecordingQualityPreset.Low ? 32
        : quality == RecordingQualityPreset.Medium ? 27
        : quality == RecordingQualityPreset.High ? 23 : 16;
    foreach (var size in new[] { (1920,1080), (3840,2160), (2560,1440), (3440,1440), (1080,1920), (2048,1152) })
    {
        foreach (int fps in new[] {30,60})
        {
            var profile = RecordingQualityProfile.FromPreset(quality, size.Item1, size.Item2, fps);
            Expect(profile.QuantizationParameter == baseline, "Unexpected large-resolution QP");
            Expect(profile.NativeEncodingPreset == 5 && profile.VideoBitRate == 0 && profile.MaximumVideoBitRate == 0, "CQP profile changed");
            int audioBitRate = quality == RecordingQualityPreset.Low ? 96000
                : quality == RecordingQualityPreset.Medium ? 128000 : 192000;
            Expect(profile.AudioBitRate == audioBitRate, "AAC bitrate changed");
        }
    }
    Expect(RecordingQualityProfile.FromPreset(quality,1280,720).QuantizationParameter == baseline - 2, "720p reduction");
    Expect(RecordingQualityProfile.FromPreset(quality,640,480).QuantizationParameter == baseline - 6, "Small-resolution reduction");
}
Invalid(() => RecordingQualityProfile.FromPreset((RecordingQualityPreset)99));
Invalid(() => RecordingQualityProfile.FromPreset(RecordingQualityPreset.High,1921,1080));
Invalid(() => RecordingQualityProfile.FromPreset(RecordingQualityPreset.High,1920,0));
Invalid(() => RecordingQualityProfile.FromPreset(RecordingQualityPreset.High,1920,1080,0));

static long Time(byte[] packet, int i) => ((long)(packet[i] & 14) << 29) | ((long)packet[i+1] << 22) | ((long)(packet[i+2] & 254) << 14) | ((long)packet[i+3] << 7) | ((long)packet[i+4] >> 1);
var transport = new EncodedVideoTransport(30, VideoStreamFormat.H264);
int[] presentationOrder = { 0, 3, 1, 2 };
long previousDts = -1;
foreach (int presentation in presentationOrder)
{
    byte[] elementary = Enumerable.Range(0,701).Select(i => (byte)i).ToArray();
    byte[] ts = transport.Wrap(elementary, 5000000 + presentation * 1000000L / 30);
    Expect(ts.Length % 188 == 0, "TS alignment");
    byte[] first = ts.Skip(376).Take(188).ToArray();
    int pes = 5 + first[4];
    long pts = Time(first,pes+9);
    long dts = Time(first,pes+14);
    Expect(Math.Abs(pts-(96000+presentation*3000))<=1, "Presentation timestamp was reordered");
    Expect(dts > previousDts && pts >= dts, "Decode timestamp is invalid");
    previousDts = dts;
    var reconstructed = new System.Collections.Generic.List<byte>();
    for (int i=376;i<ts.Length;i+=188)
    {
        Expect(ts[i]==0x47,"Sync byte");
        int offset = (ts[i+3]&0x20)!=0 ? 5+ts[i+4] : 4;
        reconstructed.AddRange(ts.Skip(i+offset).Take(188-offset));
    }
    Expect(reconstructed.Skip(19).SequenceEqual(elementary),"Elementary video bytes changed");
}
Console.WriteLine("Quality profiles, arbitrary resolutions, invalid inputs and reordered PTS/DTS transport passed.");
