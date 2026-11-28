using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace FFmpegForge
{
    /// 3.0.1.1 — Monkey's Audio (.ape) export. FFmpeg can decode APE but has no APE encoder, so:
    ///   ffmpeg (trim, channels, volume, filters, sample rate; 16 / 24-bit PCM kept) -> WAV (temporary)
    ///   -> MAC.exe, the official Monkey's Audio encoder by Matthew T. Ashland (BSD-3-Clause, monkeysaudio.com)
    ///   -> APEv2 tag written by FFmpeg Forge: the source tags + the cover art ("Cover Art (Front)").
    /// FFmpeg Forge does not include MAC.exe: it uses the user's Monkey's Audio installation (or a MAC.exe placed in
    /// the MonkeysAudio folder next to FFmpegForge.exe).
    public static class ApeEngine
    {
        public const string DownloadPage = "https://monkeysaudio.com/download.html";
        public static string UserPath = "";      // chosen with "Monkey's Audio encoder…" (settings key mac_path)

        public static bool IsApe(string key) { return key == "ape"; }

        public static readonly string[] LevelNames = { "Fast (-c1000)", "Normal (-c2000)", "High (-c3000)", "Extra High (-c4000)", "Insane (-c5000)" };

        static IEnumerable<string> Candidates()
        {
            if (!string.IsNullOrEmpty(UserPath)) yield return UserPath;
            string app = AppPaths.AppDir;
            yield return Path.Combine(app, "MonkeysAudio", "MAC.exe");
            yield return Path.Combine(app, "Monkey's Audio", "MAC.exe");
            yield return Path.Combine(app, "MAC.exe");
            yield return Path.Combine(app, "bin", "MAC.exe");
            foreach (var env in new[] { "ProgramW6432", "ProgramFiles", "ProgramFiles(x86)" })
            {
                string pf = Environment.GetEnvironmentVariable(env);
                if (string.IsNullOrEmpty(pf)) continue;
                yield return Path.Combine(pf, "Monkey's Audio x64", "MAC.exe");
                yield return Path.Combine(pf, "Monkey's Audio", "MAC.exe");
            }
            string path = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (var d in path.Split(Path.PathSeparator))
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                string dd = d.Trim().Trim('"');
                yield return Path.Combine(dd, "MAC.exe");
                yield return Path.Combine(dd, "mac");
            }
        }

        public static string ExePath
        {
            get
            {
                foreach (var c in Candidates())
                    try { if (File.Exists(c)) return c; } catch { }
                return null;
            }
        }

        public static string Status()
        {
            string e = ExePath;
            if (e != null) return "Ready: Monkey's Audio encoder " + e;
            return "Monkey's Audio is not installed: FFmpeg can read .ape but has no APE encoder. Install Monkey's Audio (free, BSD licence) with \"Download Monkey's Audio\", or choose MAC.exe with \"Monkey's Audio encoder…\".";
        }

        public class Plan
        {
            public string DecArgs, Wav, MacExe, MacArgs, Output, Source, TempDir, CoverArgs, CoverFile;
            public int Level, Bits, Channels, Rate;
            public double Expected;
            public bool WantCover;
            public string Text() { return DecArgs + "  |  MAC.exe " + MacArgs + "  |  APEv2 tags" + (WantCover ? " + cover" : ""); }
        }

        static string Q(string x) { return "\"" + x.Replace("\"", "\\\"") + "\""; }

        /// Bit depth kept from the source: 24-bit for hi-res / float / 32-bit sources, 16-bit otherwise (MAC.exe: 8/16/24/32).
        static int BitsFor(Job job)
        {
            string c = (job.AudioCodec ?? "").ToLowerInvariant();
            if (c.Contains("s16") || c == "mp3" || c == "aac" || c == "opus" || c == "vorbis" || c == "ac3" || c == "eac3" || c == "mp2" || c == "wmav2" || c.StartsWith("amr")) return 16;
            if (c == "pcm_s32le" || c == "pcm_s32be") return 32;
            if (job.IsDsdAudio) return 24;
            string sf = RawSampleFormat(job);
            if (sf == "s16" || sf == "s16p" || sf == "u8" || sf == "u8p") return 16;
            return 24;
        }

        static string RawSampleFormat(Job job)
        {
            try
            {
                if (string.IsNullOrEmpty(Runner.FFprobe)) return null;
                var psi = new ProcessStartInfo(Runner.FFprobe, "-v error -select_streams a:0 -show_entries stream=sample_fmt,bits_per_raw_sample -of default=nw=1 " + Q(job.Path))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(15000);
                    string fmt = null; int bits = 0;
                    foreach (var l in o.Split('\n'))
                    {
                        var t = l.Trim();
                        if (t.StartsWith("sample_fmt=")) fmt = t.Substring(11);
                        else if (t.StartsWith("bits_per_raw_sample=")) int.TryParse(t.Substring(20), out bits);
                    }
                    if (bits > 0 && bits <= 16) return "s16";
                    return fmt;
                }
            }
            catch { return null; }
        }

        public static Plan Build(Job job, ConvSettings s, string outPath, out string error)
        {
            error = null;
            if (job.Probed && !job.HasAudio) { error = "This file has no audio track to convert to Monkey's Audio."; return null; }
            string exe = ExePath;
            if (exe == null) { error = "Monkey's Audio (.ape) needs the official encoder MAC.exe (FFmpeg has no APE encoder). Install Monkey's Audio from monkeysaudio.com (\"Download Monkey's Audio\" in AUDIO SETTINGS) or choose MAC.exe with \"Monkey's Audio encoder…\"."; return null; }

            double ss = s.TrimStart > 0 ? s.TrimStart : 0, to = s.TrimEnd > 0 ? s.TrimEnd : -1;
            string trim = (ss > 0 ? "-ss " + Formats.Inv(ss) + " " : "") + (to > ss && to > 0 ? "-to " + Formats.Inv(to) + " " : "");
            double dur = job.Duration;
            if (to > 0 && (dur <= 0 || to < dur)) dur = to;
            dur = Math.Max(0, dur - ss);

            s.SourceChannels = job.AudioChannels; s.SourceIsVideo = job.Kind == MediaKind.Video; s.SourceDuration = dur;
            s.SourceDsd = job.IsDsdAudio; s.DsdTargetRate = 176400;
            if (s.ChLayout != null && s.ChLayout != "copy")
            {
                string lerr = ChannelLayouts.Check(s.ChLayout, "pcm24");
                if (lerr != null) { error = lerr; return null; }
            }
            int bits = BitsFor(job);
            var af = new List<string>();
            string chain = CommandBuilder.AudioChain(s, s.UserAudioChain());
            if (chain != null) af.Add(chain);
            string pcm = bits == 16 ? "pcm_s16le" : bits == 32 ? "pcm_s32le" : "pcm_s24le";
            int level = Math.Max(1, Math.Min(5, s.ApeLevel)) * 1000;

            var p = new Plan { MacExe = exe, Output = outPath, Source = job.Path, Level = level, Bits = bits, Expected = dur };
            p.TempDir = Path.Combine(Path.GetTempPath(), "FFmpegForge-APE-" + Guid.NewGuid().ToString("N").Substring(0, 8));
            p.Wav = Path.Combine(p.TempDir, "audio.wav");
            p.DecArgs = "-hide_banner -nostdin -y -loglevel error -progress pipe:1 -nostats " + trim + "-i " + Q(job.Path) + " -map 0:a:0 -vn -sn -dn" +
                        (af.Count > 0 ? " -af " + Q(string.Join(",", af.ToArray())) : "") + " " + CommandBuilder.AudioFormatArgs(s, true) + " " +
                        ChannelLayouts.ExtraArgs(s.ChLayout, "pcm24") + " -c:a " + pcm + " -rf64 auto -map_metadata -1 " + Q(p.Wav);
            p.DecArgs = Regex.Replace(p.DecArgs, " {2,}", " ");
            p.MacArgs = Q(p.Wav) + " " + Q(outPath + ".part.ape") + " -c" + level;
            // the cover art: attached picture of an audio file (not the frames of a video)
            p.WantCover = job.Kind == MediaKind.Audio;
            if (p.WantCover)
            {
                string vc = CoverCodec(job.Path);
                p.WantCover = vc != null;
                if (p.WantCover)
                {
                    bool png = vc == "png";
                    bool copy = png || vc == "mjpeg";
                    p.CoverFile = Path.Combine(p.TempDir, png ? "cover.png" : "cover.jpg");
                    p.CoverArgs = "-hide_banner -nostdin -y -loglevel error -i " + Q(job.Path) + " -map 0:v:0 -an -frames:v 1 " + (copy ? "-c:v copy" : "-c:v mjpeg -q:v 2") + " -f image2 " + Q(p.CoverFile);
                }
            }
            return p;
        }

        /// Codec of the first picture stream of an audio file (its cover art), null when there is none.
        static string CoverCodec(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(Runner.FFprobe)) return null;
                var psi = new ProcessStartInfo(Runner.FFprobe, "-v error -select_streams v:0 -show_entries stream=codec_name -of default=nw=1:nk=1 " + Q(path))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd().Trim(); p.StandardError.ReadToEnd(); p.WaitForExit(15000);
                    if (o.Length == 0) return null;
                    return o.Split('\n')[0].Trim();
                }
            }
            catch { return null; }
        }

        // ---------------- tags ----------------
        static readonly Dictionary<string, string> KeyMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "title", "Title" }, { "artist", "Artist" }, { "album", "Album" }, { "album_artist", "Album Artist" }, { "albumartist", "Album Artist" },
            { "date", "Year" }, { "year", "Year" }, { "track", "Track" }, { "tracknumber", "Track" }, { "disc", "Disc" }, { "discnumber", "Disc" },
            { "genre", "Genre" }, { "comment", "Comment" }, { "description", "Comment" }, { "composer", "Composer" }, { "copyright", "Copyright" },
            { "lyrics", "Lyrics" }, { "publisher", "Publisher" }, { "label", "Publisher" }, { "isrc", "ISRC" }, { "language", "Language" },
            { "conductor", "Conductor" }, { "performer", "Performer" }, { "encoded_by", "Encoded By" }, { "barcode", "Barcode" }, { "catalog", "Catalog" }
        };
        static readonly HashSet<string> Skip = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "encoder", "major_brand", "minor_version", "compatible_brands", "creation_time", "handler_name", "vendor_id", "duration" };

        /// Source tags (format + first audio stream) as APEv2 items.
        public static List<KeyValuePair<string, string>> ReadTags(string path)
        {
            var list = new List<KeyValuePair<string, string>>();
            try
            {
                if (string.IsNullOrEmpty(Runner.FFprobe)) return list;
                var psi = new ProcessStartInfo(Runner.FFprobe, "-v error -show_entries format_tags:stream_tags -select_streams a:0 -of default=nw=1 " + Q(path))
                { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardOutputEncoding = Encoding.UTF8 };
                using (var p = Process.Start(psi))
                {
                    string o = p.StandardOutput.ReadToEnd(); p.StandardError.ReadToEnd(); p.WaitForExit(15000);
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var raw in o.Split('\n'))
                    {
                        string l = raw.TrimEnd('\r');
                        if (!l.StartsWith("TAG:")) continue;
                        int eq = l.IndexOf('=');
                        if (eq < 5) continue;
                        string k = l.Substring(4, eq - 4).Trim(), v = l.Substring(eq + 1).Trim();
                        if (v.Length == 0 || Skip.Contains(k)) continue;
                        string ak;
                        if (!KeyMap.TryGetValue(k, out ak))
                        {
                            if (k.Length < 2 || k.Length > 255 || k.Any(ch => ch < 0x20 || ch > 0x7E)) continue;
                            ak = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(k.Replace('_', ' ').ToLowerInvariant());
                        }
                        if (ak == "Year" && v.Length > 4 && Regex.IsMatch(v, @"^\d{4}")) v = v.Substring(0, 4);
                        if (!seen.Add(ak)) continue;
                        list.Add(new KeyValuePair<string, string>(ak, v));
                    }
                }
            }
            catch { }
            return list;
        }

        static void U32(Stream s, uint v) { s.WriteByte((byte)v); s.WriteByte((byte)(v >> 8)); s.WriteByte((byte)(v >> 16)); s.WriteByte((byte)(v >> 24)); }

        static void Head(Stream s, uint size, uint count, bool header)
        {
            s.Write(Encoding.ASCII.GetBytes("APETAGEX"), 0, 8);
            U32(s, 2000);
            U32(s, size);
            U32(s, count);
            U32(s, header ? 0xA0000000u : 0x80000000u);     // has a header; this block is the header / the footer
            s.Write(new byte[8], 0, 8);
        }

        /// Appends an APEv2 tag (header + items + footer) to the .ape file. Cover: "Cover Art (Front)" = file name, 0, picture.
        public static int WriteTag(string apeFile, List<KeyValuePair<string, string>> tags, string coverFile)
        {
            var items = new MemoryStream();
            int count = 0;
            foreach (var kv in tags)
            {
                var val = Encoding.UTF8.GetBytes(kv.Value);
                U32(items, (uint)val.Length); U32(items, 0);
                var key = Encoding.ASCII.GetBytes(kv.Key); items.Write(key, 0, key.Length); items.WriteByte(0);
                items.Write(val, 0, val.Length);
                count++;
            }
            if (coverFile != null && File.Exists(coverFile) && new FileInfo(coverFile).Length > 0)
            {
                var pic = File.ReadAllBytes(coverFile);
                var name = Encoding.UTF8.GetBytes(Path.GetFileName(coverFile));
                U32(items, (uint)(name.Length + 1 + pic.Length)); U32(items, 2);          // flags: binary item
                var key = Encoding.ASCII.GetBytes("Cover Art (Front)"); items.Write(key, 0, key.Length); items.WriteByte(0);
                items.Write(name, 0, name.Length); items.WriteByte(0); items.Write(pic, 0, pic.Length);
                count++;
            }
            if (count == 0) return 0;
            uint size = (uint)(items.Length + 32);
            using (var fs = new FileStream(apeFile, FileMode.Append, FileAccess.Write))
            {
                Head(fs, size, (uint)count, true);
                items.Position = 0; items.CopyTo(fs);
                Head(fs, size, (uint)count, false);
            }
            return count;
        }

        // ---------------- run ----------------
        static int RunProc(string exe, string args, Action<string> onOut, Action<string> onErr, Func<bool> cancelled, string workDir = null)
        {
            var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true, StandardErrorEncoding = Encoding.UTF8, StandardOutputEncoding = Encoding.UTF8 };
            if (workDir != null) psi.WorkingDirectory = workDir;
            using (var p = Process.Start(psi))
            {
                p.OutputDataReceived += (o, e) => { if (e.Data != null && onOut != null) onOut(e.Data); };
                p.ErrorDataReceived += (o, e) => { if (e.Data != null && onErr != null) onErr(e.Data); };
                p.BeginOutputReadLine(); p.BeginErrorReadLine();
                while (!p.WaitForExit(200))
                    if (cancelled()) { try { p.Kill(); } catch { } p.WaitForExit(5000); return -1; }
                p.WaitForExit();
                return p.ExitCode;
            }
        }

        /// MAC.exe writes its progress with carriage returns on one line: read stdout / stderr character by character.
        static int RunMac(Plan plan, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, List<string> errs)
        {
            var psi = new ProcessStartInfo(plan.MacExe, plan.MacArgs) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            try { psi.WorkingDirectory = Path.GetDirectoryName(plan.MacExe); } catch { }
            var rx = new Regex(@"(\d{1,3}(?:[.,]\d+)?)\s*%");
            using (var p = Process.Start(psi))
            {
                Action<StreamReader> pump = r =>
                {
                    var sb = new StringBuilder();
                    int c;
                    while ((c = r.Read()) >= 0)
                    {
                        if (c == '\r' || c == '\n')
                        {
                            string l = sb.ToString().Trim(); sb.Length = 0;
                            if (l.Length == 0) continue;
                            var m = rx.Match(l);
                            double v;
                            if (m.Success && double.TryParse(m.Groups[1].Value.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && v <= 100)
                                onProgress(v / 100.0);
                            else { lock (errs) { errs.Add(l); if (errs.Count > 30) errs.RemoveAt(0); } onLog("MAC: " + l); }
                        }
                        else sb.Append((char)c);
                    }
                };
                var t1 = new System.Threading.Thread(() => pump(p.StandardOutput)) { IsBackground = true };
                var t2 = new System.Threading.Thread(() => pump(p.StandardError)) { IsBackground = true };
                t1.Start(); t2.Start();
                while (!p.WaitForExit(200))
                    if (cancelled()) { try { p.Kill(); } catch { } p.WaitForExit(5000); return -1; }
                p.WaitForExit(); t1.Join(2000); t2.Join(2000);
                return p.ExitCode;
            }
        }

        /// ffmpeg -> WAV -> MAC.exe -> APEv2 tag. Returns 0 on success, -1 when cancelled.
        public static int Run(Plan plan, Action<double> onProgress, Action<string> onLog, Func<bool> cancelled, out string lastErrors)
        {
            var errs = new List<string>();
            string part = plan.Output + ".part.ape";
            try
            {
                Directory.CreateDirectory(plan.TempDir);
                try { string od = Path.GetDirectoryName(plan.Output); if (!string.IsNullOrEmpty(od)) Directory.CreateDirectory(od); } catch { }
                // 1. decode (0 - 40 %)
                double exp = plan.Expected;
                int code = RunProc(Runner.FFmpeg, plan.DecArgs,
                    l =>
                    {
                        if (exp > 0 && l.StartsWith("out_time_us="))
                        { long us; if (long.TryParse(l.Substring(12), out us) && us > 0) onProgress(0.4 * Math.Min(1, us / 1e6 / exp)); }
                    },
                    l => { if (!string.IsNullOrWhiteSpace(l)) { lock (errs) { errs.Add(l); if (errs.Count > 30) errs.RemoveAt(0); } onLog(l); } },
                    cancelled);
                if (code == -1) { lastErrors = ""; return -1; }
                if (code != 0 || !File.Exists(plan.Wav)) { lock (errs) lastErrors = string.Join(Environment.NewLine, errs.ToArray()); return code != 0 ? code : 3; }
                // 2. Monkey's Audio (40 - 95 %)
                errs.Clear();
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                code = RunMac(plan, v => onProgress(0.4 + 0.55 * v), onLog, cancelled, errs);
                if (code == -1) { try { File.Delete(part); } catch { } lastErrors = ""; return -1; }
                if (code != 0 || !File.Exists(part) || new FileInfo(part).Length < 32)
                {
                    lock (errs) lastErrors = "MAC.exe (Monkey's Audio) failed (exit code " + code + ")." + (errs.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, errs.ToArray()) : "");
                    try { File.Delete(part); } catch { }
                    return code != 0 ? code : 3;
                }
                // 3. APEv2 tag: source tags + cover art
                var tags = ReadTags(plan.Source);
                string cover = null;
                if (plan.WantCover && plan.CoverArgs != null)
                {
                    int cc = RunProc(Runner.FFmpeg, plan.CoverArgs, null, null, cancelled);
                    if (cc == 0 && File.Exists(plan.CoverFile)) cover = plan.CoverFile;
                }
                int n = WriteTag(part, tags, cover);
                onLog("APEv2 tag: " + tags.Count + " field(s)" + (cover != null ? " + cover art (" + (new FileInfo(cover).Length / 1024) + " KB)" : ""));
                if (File.Exists(plan.Output)) File.Delete(plan.Output);
                File.Move(part, plan.Output);
                onProgress(1.0);
                lastErrors = "";
                return 0;
            }
            catch (Exception ex)
            {
                try { if (File.Exists(part)) File.Delete(part); } catch { }
                lock (errs) lastErrors = ex.Message + (errs.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, errs.ToArray()) : "");
                return -2;
            }
            finally
            {
                try { if (Directory.Exists(plan.TempDir)) Directory.Delete(plan.TempDir, true); } catch { }
            }
        }
    }
}
