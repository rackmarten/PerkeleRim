using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using AssetStudio;
using Newtonsoft.Json;

// VoiceExport <data folder> <output folder> [--max-seconds N] [--ffmpeg <ffmpeg.exe>]
//
// Writes every AudioClip of My Summer Car's data folder to <output>/audio/ (Ogg Opus through ffmpeg
// when given, else WAV) and <output>/index.json + index.js: one row per line the way MSC groups them.
// MSC plays its voices through MasterAudio, a scene object whose children are sound groups
// (Hangover, Swearing, Drunk, Teimo...) and whose grandchildren are a group's variations, each an
// AudioSource with one clip; a group's number for a line is the variation's place among its
// siblings. Clips no MasterAudio group plays are listed under the scene object that plays them, or
// under "(no AudioSource)" (MSC's PlayMaker FSMs and soundbanks play those). Clips longer than
// --max-seconds (60; 0 for all) are listed without audio: music, radio, the drunk monologues.
// Everything written is the user's own game audio and stays in the gitignored output folder.
static class Program
{
    class Row
    {
        public string group;
        public int index;           // 1-based within the group
        public string variation;    // the MasterAudio variation's or the scene object's name
        public string clip;         // the AudioClip's name
        public string id;           // audio file stem, also the transcripts' key
        public string file;         // relative to the output folder; null when skipped
        public float seconds;
        public string[] uses;       // scene paths of the AudioSources playing the clip
    }

    static int Main(string[] args)
    {
        if (args.Length < 2)
        {
            Console.Error.WriteLine("usage: VoiceExport <data folder> <output folder> [--max-seconds N] [--ffmpeg <ffmpeg.exe>]");
            return 2;
        }
        string data = args[0], output = args[1], ffmpeg = null;
        float maxSeconds = 60;
        for (int i = 2; i + 1 < args.Length; i += 2)
        {
            if (args[i] == "--max-seconds") maxSeconds = float.Parse(args[i + 1], System.Globalization.CultureInfo.InvariantCulture);
            else if (args[i] == "--ffmpeg") ffmpeg = args[i + 1];
        }

        var manager = new AssetsManager();
        manager.LoadFilesAndFolders(out _, data);
        var objects = manager.AssetsFileList.SelectMany(f => f.Objects).ToList();
        var clips = objects.OfType<AudioClip>().ToList();

        // AudioSource isn't one of AssetStudio's classes: Component (m_GameObject), Behaviour
        // (m_Enabled, aligned), then Unity 5's OutputAudioMixerGroup and m_audioClip.
        var sources = new List<(GameObject go, AudioClip clip)>();
        foreach (var o in objects.Where(o => o.type == ClassIDType.AudioSource))
        {
            var r = o.reader;
            r.Reset();
            var go = new PPtr<GameObject>(r);
            r.ReadByte();
            r.Position = (r.Position + 3) & ~3L;
            new PPtr<AssetStudio.Object>(r);
            var clip = new PPtr<AudioClip>(r);
            if (go.TryGet(out var g) && clip.TryGet(out var c)) sources.Add((g, c));
        }

        var ids = Ids(clips);
        var uses = sources.GroupBy(s => s.clip).ToDictionary(x => x.Key, x => x.Select(s => PathOf(s.go)).Distinct().OrderBy(p => p).ToArray());
        var rows = new List<Row>();
        var inGroup = new HashSet<AudioClip>();
        foreach (var s in sources)
        {
            var path = Chain(s.go);
            if (path.Count != 3 || path[0].m_Name != "MasterAudio") continue;
            rows.Add(NewRow(path[1].m_Name, Place(path[2]), path[2].m_Name, s.clip, ids, uses));
            inGroup.Add(s.clip);
        }
        var roots = sources.GroupBy(s => s.clip).ToDictionary(x => x.Key, x => x.Select(s => Chain(s.go)[0].m_Name).OrderBy(n => n).First());
        foreach (var byGroup in clips.Where(c => !inGroup.Contains(c))
                     .GroupBy(c => roots.TryGetValue(c, out var root) ? "scene: " + root : "(no AudioSource)"))
        {
            int n = 0;
            foreach (var c in byGroup.OrderBy(c => c.m_Name, StringComparer.OrdinalIgnoreCase))
                rows.Add(NewRow(byGroup.Key, ++n, c.m_Name, c, ids, uses));
        }
        rows = rows.OrderBy(r => r.group.StartsWith("scene: ") ? 1 : r.group.StartsWith("(") ? 2 : 0)
            .ThenBy(r => r.group, StringComparer.OrdinalIgnoreCase).ThenBy(r => r.index).ToList();

        // Audio: decoded one at a time (FMOD), encoded by a few ffmpeg processes alongside.
        var audio = Path.Combine(output, "audio");
        Directory.CreateDirectory(audio);
        string ext = ffmpeg != null ? ".ogg" : ".wav";
        var todo = rows.Where(r => maxSeconds <= 0 || r.seconds <= maxSeconds).GroupBy(r => r.id).ToList();
        var queue = new BlockingCollection<(string path, byte[] wav)>(16);
        var encoders = Enumerable.Range(0, ffmpeg != null ? Math.Max(2, Environment.ProcessorCount / 2) : 1)
            .Select(_ => Task.Run(() => { foreach (var job in queue.GetConsumingEnumerable()) Write(job.path, job.wav, ffmpeg); })).ToArray();
        int written = 0, failed = 0;
        foreach (var byId in todo)
        {
            var clip = clips.First(c => ids[c] == byId.Key);
            string file = "audio/" + byId.Key + ext, path = Path.Combine(output, file);
            if (!File.Exists(path))
            {
                var converter = new AudioClipConverter(clip);
                string log = null;
                var wav = converter.IsSupport ? converter.ConvertToWav(clip.m_AudioData.GetData(), ref log) : null;
                if (wav == null) { failed++; continue; }
                queue.Add((path, wav));
                if (++written % 100 == 0) Console.WriteLine($"{written} clips decoded");
            }
            foreach (var r in byId) r.file = file;
        }
        queue.CompleteAdding();
        Task.WaitAll(encoders);

        var index = new
        {
            source = Path.GetFullPath(data),
            unity = manager.AssetsFileList[0].version.ToString(),
            maxSeconds,
            rows,
        };
        var json = JsonConvert.SerializeObject(index, Formatting.Indented);
        File.WriteAllText(Path.Combine(output, "index.json"), json, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(output, "index.js"), "window.voiceIndex = " + json + ";\n", new UTF8Encoding(false));
        Console.WriteLine($"{rows.Count} lines in {rows.Select(r => r.group).Distinct().Count()} groups; {written} clips written, " +
            $"{todo.Count - written - failed} already there, {failed} failed, {rows.Count(r => r.file == null) - failed} longer than {maxSeconds} s left out");
        return 0;
    }

    static Row NewRow(string group, int index, string variation, AudioClip clip, Dictionary<AudioClip, string> ids, Dictionary<AudioClip, string[]> uses) => new Row
    {
        group = group,
        index = index,
        variation = variation,
        clip = clip.m_Name,
        id = ids[clip],
        seconds = (float)Math.Round(clip.m_Length, 1),
        uses = uses.TryGetValue(clip, out var u) ? u : new string[0],
    };

    // File stems: the clip name in ASCII, with the path id when two clips share it.
    static Dictionary<AudioClip, string> Ids(List<AudioClip> clips)
    {
        string Safe(string s) => new string(s.Select(ch => ch < 128 && (char.IsLetterOrDigit(ch) || ch == '-' || ch == '_') ? ch : '_').ToArray());
        var ids = new Dictionary<AudioClip, string>();
        foreach (var same in clips.GroupBy(c => Safe(c.m_Name), StringComparer.OrdinalIgnoreCase))
            foreach (var c in same)
                ids[c] = same.Count() == 1 ? same.Key : $"{same.Key}~{c.m_PathID}";
        return ids;
    }

    static void Write(string path, byte[] wav, string ffmpeg)
    {
        if (ffmpeg == null) { File.WriteAllBytes(path, wav); return; }
        // Through a file: .NET Framework's redirected stdin starts with a UTF-8 BOM.
        string tmp = path + ".tmp.ogg", input = path + ".tmp.wav";
        File.WriteAllBytes(input, wav);
        var p = Process.Start(new ProcessStartInfo(ffmpeg, $"-hide_banner -loglevel error -y -i \"{input}\" -c:a libopus -b:a 64k \"{tmp}\"")
        {
            UseShellExecute = false,
            RedirectStandardError = true,
            CreateNoWindow = true,
        });
        var err = p.StandardError.ReadToEnd();
        p.WaitForExit();
        File.Delete(input);
        if (p.ExitCode != 0) { Console.Error.WriteLine($"ffmpeg failed on {path}: {err}"); return; }
        File.Delete(path);
        File.Move(tmp, path);
    }

    // The GameObject and its ancestors, root first.
    static List<GameObject> Chain(GameObject g)
    {
        var chain = new List<GameObject>();
        for (var t = g.m_Transform; t != null && t.m_GameObject.TryGet(out var o);)
        {
            chain.Add(o);
            if (t.m_Father.IsNull || !t.m_Father.TryGet(out t)) break;
        }
        chain.Reverse();
        return chain;
    }

    static string PathOf(GameObject g) => string.Join("/", Chain(g).Select(o => o.m_Name));

    // 1-based place among its parent's children.
    static int Place(GameObject g)
    {
        var t = g.m_Transform;
        if (t == null || t.m_Father.IsNull || !t.m_Father.TryGet(out var parent)) return 1;
        int i = parent.m_Children.FindIndex(c => c.m_PathID == t.m_PathID);
        return i + 1;
    }
}
