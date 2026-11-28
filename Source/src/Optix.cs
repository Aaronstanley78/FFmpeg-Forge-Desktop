using System;
using System.IO;

namespace FFmpegForge
{
    /// 3.0.1 — OptiX Denoiser (VIDEO SETTINGS). NVIDIA's OptiX AI denoiser (AOV model) run by ForgeOptiX.exe, a small
    /// helper next to FFmpegForge.exe. The OptiX runtime is part of the NVIDIA driver (nvoptix.dll): nothing to install.
    ///   ffmpeg (trim, decode → RGB) → ForgeOptiX.exe (GPU) → ffmpeg (lossless UT Video intermediate, audio copied)
    /// The intermediate then goes through the normal export (AI denoiser, upscalers, codecs, audio…), like the AI denoiser.
    /// OptiX was trained on rendering noise (path tracing): on camera footage it is a strong spatial denoiser, so the
    /// strength (OptiX blend factor) lets part of the original picture through.
    public static class OptixDenoise
    {
        public static string ExePath
        {
            get
            {
                try
                {
                    foreach (var d in new[] { AppDomain.CurrentDomain.BaseDirectory, AppPaths.AppDir })
                    { string p = Path.Combine(d, "ForgeOptiX.exe"); if (File.Exists(p)) return p; }
                }
                catch { }
                return null;
            }
        }

        static bool Win { get { return AppPaths.IsWindows || Environment.GetEnvironmentVariable("FORGE_DRIVER_TEST") == "1"; } }
        public static bool Available { get { return ExePath != null && Win; } }

        public static string Status()
        {
            if (!Win) return "The OptiX Denoiser needs Windows and an NVIDIA GPU.";
            if (ExePath == null) return "ForgeOptiX.exe is missing next to FFmpegForge.exe.";
            return "NVIDIA OptiX AI denoiser (runtime in the NVIDIA driver): NVIDIA GPU with an up-to-date driver.";
        }

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }

        /// source → OptiX Denoiser → lossless intermediate (trim applied, audio copied).
        public static AiDenoise.Stage Build(Job job, ConvSettings s, out string error)
        {
            error = null;
            string exe = ExePath;
            if (exe == null || !Win) { error = Status(); return null; }
            int w = job.Width, h = job.Height;
            if (w < 16 || h < 16) { error = "Could not read the picture size (ffprobe)."; return null; }
            var st = new AiDenoise.Stage { Picture = false };
            st.TempDir = Path.Combine(Path.GetTempPath(), "FFmpegForge-OptiX-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            st.Intermediate = Path.Combine(st.TempDir, "optix.mkv");
            int fn = job.FpsNum > 0 ? job.FpsNum : 30, fd = job.FpsDen > 0 ? job.FpsDen : 1;
            if (s.Fps > 0) { fn = (int)Math.Round(s.Fps * 1000); fd = 1000; }
            string rate = fn + "/" + fd;
            double ss = s.TrimStart > 0 ? s.TrimStart : 0, to = s.TrimEnd > 0 ? s.TrimEnd : -1;
            string trim = (ss > 0 ? "-ss " + Formats.Inv(ss) + " " : "") + (to > ss && to > 0 ? "-to " + Formats.Inv(to) + " " : "");
            double dur = job.Duration;
            if (to > 0 && (dur <= 0 || to < dur)) dur = to;
            dur = Math.Max(0, dur - ss);
            st.Seconds = dur;
            string m709 = h >= 720 ? "bt709" : "smpte170m";
            double blend = 1.0 - Math.Max(0, Math.Min(100, s.OptixStrength)) / 100.0;
            var p = new Rtx.DriverPlan { W = w, H = h, OW = w, OH = h, Expected = dur, Helper = exe, HelperName = "ForgeOptiX" };
            p.DecArgs = "-hide_banner -nostdin -loglevel error " + trim + "-i " + Q(job.Path) + " -map 0:v:0 -an -sn -dn -r " + rate +
                        " -vf " + Q("scale=" + w + ":" + h + ",format=rgb24") + " -f rawvideo -";
            p.HelperArgs = "-w " + w + " -h " + h + " -blend " + blend.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            p.EncArgs = "-hide_banner -nostdin -y -loglevel warning -progress pipe:1 -nostats " +
                        "-f rawvideo -pix_fmt rgb24 -s " + w + "x" + h + " -framerate " + rate + " -i - " + trim + "-i " + Q(job.Path) +
                        " -map 0:v:0 -map 1:a? -vf " + Q("scale=out_color_matrix=" + m709 + ":out_range=tv,format=yuv444p") +
                        " -c:v utvideo -pred median -color_primaries " + m709 + " -color_trc " + m709 + " -colorspace " + m709 +
                        (job.IsDsdAudio ? " -af " + Q("aresample=176400,lowpass=f=40000,volume=2.0") + " -c:a flac" : " -c:a copy") + " -shortest " + Q(st.Intermediate);
            st.Plan = p;
            return st;
        }
    }
}
