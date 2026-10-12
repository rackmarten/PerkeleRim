using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using PerkeleRim.Protocol;

namespace PerkeleRim
{
    // MSC's state kept with a Skyrim save (MscSave / MscLoad): the message payload, and the state
    // itself as a small text file in the save's snapshot folder. Not MSC's own save: only what MSC
    // can capture from memory and put back in place (Game/SaveSync). The file holds the key Skyrim's
    // cosave has; a load only uses it when they match. Unity-free, so the tests cover it.
    //
    // Positions are Skyrim world space (units), so they survive a new island anchor; rotations and
    // velocities are Unity's (the island only ever moves, never turns).
    public class Snapshot
    {
        const int HeaderSize = 16;  // sizeof(MscSnapshot)
        public const string FileName = "PerkeleRim_snapshot.txt";
        const string Header = "PerkeleRim snapshot 1";

        public class Global
        {
            public char Kind;  // 'f' float, 'i' int, 'b' bool
            public string Name;
            public double Value;
        }

        // A value in one object's FSM (path from the scene root): its active state (Kind 's', Name =
        // the state) or a variable ('f' float, 'i' int).
        public class FsmValue
        {
            public string Path;
            public string Fsm;
            public char Kind;
            public string Name;
            public double Value;
        }

        // A vehicle or an item: by name (vehicles) or Unity instance id (items, same MSC session only).
        public class Body
        {
            public string Name = "";
            public int Id;
            public Vec3 Position;  // Skyrim units
            public Quat Rotation;  // Unity
            public Vec3 Velocity;  // Unity, m/s
            public Vec3 Angular;   // Unity, rad/s
            public float Fuel = float.NaN;  // vehicles: tank level, NaN = none
            public bool Driven;             // vehicles: the player sat in it
            public int Bottles = -1;        // beer cases: bottles left, -1 = not a case
        }

        public ulong Key;
        public bool World;  // MSC had the player: vehicles and items were captured too
        public readonly List<Global> Globals = new List<Global>();
        public readonly List<FsmValue> Fsms = new List<FsmValue>();
        public readonly List<Body> Vehicles = new List<Body>();
        public readonly List<Body> Items = new List<Body>();

        // ---- Message payload ----

        public static byte[] Encode(ulong key, string path)
        {
            var bytes = Encoding.UTF8.GetBytes(path ?? "");
            if (key == 0 || bytes.Length > Wire.MscSnapshotPathMax) return null;
            var payload = new byte[HeaderSize + bytes.Length];
            BitConverter.GetBytes(key).CopyTo(payload, 0);
            BitConverter.GetBytes((uint)bytes.Length).CopyTo(payload, 8);
            Array.Copy(bytes, 0, payload, HeaderSize, bytes.Length);
            return payload;
        }

        // The key and folder in a payload, false when it's malformed.
        public static bool Decode(byte[] payload, int size, out ulong key, out string path)
        {
            key = 0;
            path = null;
            if (payload == null || size < HeaderSize || size > payload.Length) return false;
            ulong k = BitConverter.ToUInt64(payload, 0);
            uint length = BitConverter.ToUInt32(payload, 8);
            if (k == 0 || length > Wire.MscSnapshotPathMax || HeaderSize + length > size) return false;
            key = k;
            path = Encoding.UTF8.GetString(payload, HeaderSize, (int)length);
            return true;
        }

        // ---- Text ----
        // One record per line, tab separated (names have spaces): a header, "key", "world", then
        // "global", "fsm", "vehicle" and "item" lines. Unknown lines are skipped.

        public void Write(TextWriter w)
        {
            w.WriteLine(Header);
            w.WriteLine("key\t" + Key.ToString("X16"));
            w.WriteLine("world\t" + (World ? "1" : "0"));
            foreach (var g in Globals)
                w.WriteLine("global\t" + g.Kind + "\t" + g.Name + "\t" + Num(g.Value));
            foreach (var v in Fsms)
                w.WriteLine("fsm\t" + v.Path + "\t" + v.Fsm + "\t" + v.Kind + "\t" + v.Name + "\t" + Num(v.Value));
            foreach (var v in Vehicles)
                w.WriteLine("vehicle\t" + v.Name + "\t" + Pose(v) + "\t" + Num(v.Fuel) + "\t" + (v.Driven ? "1" : "0"));
            foreach (var i in Items)
                w.WriteLine("item\t" + i.Id.ToString(CultureInfo.InvariantCulture) + "\t" + i.Name + "\t" + Pose(i) + "\t" + i.Bottles.ToString(CultureInfo.InvariantCulture));
        }

        // The snapshot in r, null when it isn't one or has no key.
        public static Snapshot Read(TextReader r)
        {
            if (r.ReadLine() != Header) return null;
            var s = new Snapshot();
            string line;
            while ((line = r.ReadLine()) != null)
            {
                var f = line.Split('\t');
                try
                {
                    switch (f[0])
                    {
                        case "key":
                            ulong.TryParse(f[1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out s.Key);
                            break;
                        case "world":
                            s.World = f[1] == "1";
                            break;
                        case "global":
                            if (f.Length >= 4 && f[1].Length == 1)
                                s.Globals.Add(new Global { Kind = f[1][0], Name = f[2], Value = Double(f[3]) });
                            break;
                        case "fsm":
                            if (f.Length >= 6 && f[3].Length == 1)
                                s.Fsms.Add(new FsmValue { Path = f[1], Fsm = f[2], Kind = f[3][0], Name = f[4], Value = Double(f[5]) });
                            break;
                        case "vehicle":
                            if (f.Length >= 16)
                            {
                                var v = new Body { Name = f[1] };
                                ReadPose(f, 2, v);
                                v.Fuel = (float)Double(f[15]);
                                v.Driven = f.Length > 16 && f[16] == "1";
                                s.Vehicles.Add(v);
                            }
                            break;
                        case "item":
                            if (f.Length >= 17)
                            {
                                var i = new Body { Id = int.Parse(f[1], CultureInfo.InvariantCulture), Name = f[2] };
                                ReadPose(f, 3, i);
                                i.Bottles = int.Parse(f[16], CultureInfo.InvariantCulture);
                                s.Items.Add(i);
                            }
                            break;
                    }
                }
                catch (FormatException) { }      // a damaged line; the rest still counts
                catch (OverflowException) { }
            }
            return s.Key != 0 ? s : null;
        }

        static string Num(double v) => v.ToString("R", CultureInfo.InvariantCulture);
        static string Num(float v) => v.ToString("R", CultureInfo.InvariantCulture);
        static double Double(string s) => double.Parse(s, NumberStyles.Float, CultureInfo.InvariantCulture);
        static float Float(string s) => (float)Double(s);

        // 13 fields: position, rotation, velocity, angular velocity.
        static string Pose(Body b) => string.Join("\t", new[]
        {
            Num(b.Position.x), Num(b.Position.y), Num(b.Position.z),
            Num(b.Rotation.x), Num(b.Rotation.y), Num(b.Rotation.z), Num(b.Rotation.w),
            Num(b.Velocity.x), Num(b.Velocity.y), Num(b.Velocity.z),
            Num(b.Angular.x), Num(b.Angular.y), Num(b.Angular.z),
        });

        static void ReadPose(string[] f, int at, Body b)
        {
            b.Position = new Vec3(Float(f[at]), Float(f[at + 1]), Float(f[at + 2]));
            b.Rotation = new Quat(Float(f[at + 3]), Float(f[at + 4]), Float(f[at + 5]), Float(f[at + 6]));
            b.Velocity = new Vec3(Float(f[at + 7]), Float(f[at + 8]), Float(f[at + 9]));
            b.Angular = new Vec3(Float(f[at + 10]), Float(f[at + 11]), Float(f[at + 12]));
        }

        // ---- Files ----

        // Writes the snapshot into folder (created if needed). Written next to it first and swapped
        // in, so a file is never half a snapshot.
        public void Store(string folder)
        {
            Directory.CreateDirectory(folder);
            string file = Path.Combine(folder, FileName);
            string temp = file + ".tmp";
            using (var w = new StreamWriter(temp, false, new UTF8Encoding(false))) Write(w);
            if (File.Exists(file)) File.Delete(file);
            File.Move(temp, file);
        }

        // The snapshot in folder, null when there is none or it can't be read.
        public static Snapshot Load(string folder)
        {
            try
            {
                string file = Path.Combine(folder, FileName);
                if (!File.Exists(file)) return null;
                using (var r = new StreamReader(file, Encoding.UTF8)) return Read(r);
            }
            catch (IOException) { return null; }
            catch (UnauthorizedAccessException) { return null; }
        }
    }
}
