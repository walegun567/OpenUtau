﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using OpenUtau.Core;
using OpenUtau.Core.Formats;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Classic {

    public static class Ust {
        static readonly Encoding ShiftJIS = Encoding.GetEncoding("shift_jis");

        public static UProject Load(string[] files) {
            foreach (var file in files) {
                if (Formats.DetectProjectFormat(file) != ProjectFormats.Ust) {
                    DocManager.Inst.ExecuteCmd(new UserMessageNotification("Multiple files must be all Ust files"));
                    return null;
                }
            }

            var projects = new List<UProject>();
            foreach (var file in files) {
                var encoding = DetectEncoding(file);
                using (var reader = new StreamReader(file, encoding)) {
                    projects.Add(Load(reader, file));
                }
            }

            var project = projects.First();
            project.name = "Merged Project";
            foreach (var p in projects) {
                if (p == project) {
                    continue;
                }
                var track = p.tracks[0];
                var part = p.parts[0];
                track.TrackNo = project.tracks.Count;
                part.trackNo = track.TrackNo;
                project.tracks.Add(track);
                project.parts.Add(part);
            }
            project.AfterLoad();
            project.Validate();
            return project;
        }

        public static Encoding DetectEncoding(string file) {
            using (var reader = new StreamReader(file, ShiftJIS)) {
                for (var i = 0; i < 10; i++) {
                    var line = reader.ReadLine();
                    if (line == null) {
                        break;
                    }
                    line = line.Trim();
                    if (line.StartsWith("Charset=")) {
                        return Encoding.GetEncoding(line.Replace("Charset=", ""));
                    }
                }
            }
            return ShiftJIS;
        }

        public static UProject Load(StreamReader reader, string file) {
            var project = new UProject() { resolution = 480, FilePath = file, Saved = false };
            Ustx.AddDefaultExpressions(project);

            project.tracks.Add(new UTrack {
                TrackNo = 0,
            });
            var part = new UVoicePart() {
                trackNo = 0,
                position = 0,
                name = Path.GetFileNameWithoutExtension(file),
            };
            project.parts.Add(part);

            var blocks = Ini.ReadBlocks(reader, file, @"\[#\w+\]");
            ParsePart(project, part, blocks);
            part.Duration = part.notes.Select(note => note.End).Max() + project.resolution;

            return project;
        }

        private static void ParsePart(UProject project, UVoicePart part, List<IniBlock> blocks) {
            var lastNotePos = 0;
            var lastNoteEnd = 0;
            foreach (var block in blocks) {
                var header = block.header;
                try {
                    switch (header) {
                        case "[#VERSION]":
                            break;
                        case "[#SETTING]":
                            ParseSetting(project, block.lines);
                            break;
                        case "[#TRACKEND]":
                            break;
                        default:
                            if (int.TryParse(header.Substring(2, header.Length - 3), out var noteIndex)) {
                                var note = project.CreateNote();
                                ParseNote(note, lastNotePos, lastNoteEnd, block.lines, out var noteTempo);
                                lastNotePos = note.position;
                                lastNoteEnd = note.End;
                                if (note.lyric.ToLower() != "r") {
                                    part.notes.Add(note);
                                }
                                if (noteTempo != null && (project.bpm <= 0 || project.bpm > 1000)) {
                                    // Fix tempo=500k error.
                                    project.bpm = noteTempo.Value;
                                }
                            } else {
                                throw new FileFormatException($"Unexpected header\n{block.header}");
                            }
                            break;
                    }
                } catch (Exception e) when (!(e is FileFormatException)) {
                    throw new FileFormatException($"Failed to parse block\n{block.header}", e);
                }
            }
            SnapPitchPoints(part);
        }

        private static void SnapPitchPoints(UVoicePart part) {
            UNote lastNote = null;
            foreach (var note in part.notes) {
                if (lastNote != null && note.position == lastNote.End) {
                    note.pitch.snapFirst = true;
                }
                lastNote = note;
            }
        }

        private static void ParseSetting(UProject project, List<IniLine> lines) {
            const string format = "<param>=<value>";
            foreach (var iniLine in lines) {
                var line = iniLine.line;
                var parts = line.Split('=');
                if (parts.Length != 2) {
                    throw new FileFormatException($"Line does not match format {format}.\n{iniLine}");
                }
                var param = parts[0].Trim();
                switch (param) {
                    case "Tempo":
                        if (ParseFloat(parts[1], out var temp)) {
                            project.bpm = temp;
                        }
                        break;
                    case "ProjectName":
                        project.name = parts[1].Trim();
                        break;
                    case "VoiceDir":
                        var singerpath = parts[1].Trim();
                        var singer = DocManager.Inst.GetSinger(singerpath);
                        if (singer == null) {
                            singer = new USinger(Path.GetFileName(singerpath.Replace("%DATA%", "").Replace("%VOICE%", "")));
                        }
                        project.tracks[0].Singer = singer;
                        break;
                }
            }
        }

        private static void ParseNote(UNote note, int lastNotePos, int lastNoteEnd, List<IniLine> iniLines, out float? noteTempo) {
            const string format = "<param>=<value>";
            noteTempo = null;
            string pbs = null, pbw = null, pby = null, pbm = null;
            int? delta = null;
            int? duration = null;
            int? length = null;
            foreach (var iniLine in iniLines) {
                var line = iniLine.line;
                var parts = line.Split('=');
                if (parts.Length != 2) {
                    throw new FileFormatException($"Line does not match format {format}.\n{iniLine}");
                }
                var param = parts[0].Trim();
                var error = false;
                var isFloat = ParseFloat(parts[1], out var floatValue);
                switch (param) {
                    case "Length":
                        error |= !isFloat;
                        length = (int)floatValue;
                        break;
                    case "Delta":
                        error |= !isFloat;
                        delta = (int)floatValue;
                        break;
                    case "Duration":
                        error |= !isFloat;
                        duration = (int)floatValue;
                        break;
                    case "Lyric":
                        ParseLyric(note, parts[1]);
                        break;
                    case "NoteNum":
                        error |= !isFloat;
                        note.tone = (int)floatValue;
                        break;
                    case "Velocity":
                        error |= !isFloat;
                        note.phonemeExpressions.Add(new UExpression("vel") {
                            index = 0,
                            value = floatValue,
                        });
                        break;
                    case "Intensity":
                        error |= !isFloat;
                        note.phonemeExpressions.Add(new UExpression("vol") {
                            index = 0,
                            value = floatValue,
                        });
                        break;
                    case "Moduration":
                        error |= !isFloat;
                        note.phonemeExpressions.Add(new UExpression("mod") {
                            index = 0,
                            value = floatValue,
                        });
                        break;
                    case "VoiceOverlap":
                        error |= !isFloat;
                        //note.phonemes[0].overlap = floatValue;
                        break;
                    case "PreUtterance":
                        error |= !isFloat;
                        //note.phonemes[0].preutter = floatValue;
                        break;
                    case "Envelope":
                        ParseEnvelope(note, parts[1], iniLine);
                        break;
                    case "VBR":
                        ParseVibrato(note, parts[1], iniLine);
                        break;
                    case "PBS":
                        pbs = parts[1];
                        break;
                    case "PBW":
                        pbw = parts[1];
                        break;
                    case "PBY":
                        pby = parts[1];
                        break;
                    case "PBM":
                        pbm = parts[1];
                        break;
                    case "Tempo":
                        if (isFloat) {
                            noteTempo = floatValue;
                        }
                        break;
                    default:
                        break;
                }
                if (error) {
                    throw new FileFormatException($"Invalid {param}\n${iniLine}");
                }
            }
            // UST Version < 2.0
            // | length       | length       |
            // | note1        | R            |
            // UST Version = 2.0
            // | length1      | length2      |
            // | dur1  |      | dur2         |
            // | note1 | R    | note2        |
            // | delta2       |
            if (delta != null && duration != null && length != null) {
                note.position = lastNotePos + delta.Value;
                note.duration = duration.Value;
            } else if (length != null) {
                note.position = lastNoteEnd;
                note.duration = length.Value;
            }
            ParsePitchBend(note, pbs, pbw, pby, pbm);
        }

        private static void ParseLyric(UNote note, string ust) {
            if (ust.StartsWith("?")) {
                ust = ust.Substring(1);
            }
            note.lyric = ust;
        }

        private static void ParseEnvelope(UNote note, string ust, IniLine ustLine) {
            // p1,p2,p3,v1,v2,v3,v4,%,p4,p5,v5 (0,5,35,0,100,100,0,%,0,0,100)
            try {
                var parts = ust.Split(new[] { ',' }).Select(s => float.TryParse(s, out var v) ? v : -1).ToArray();
                if (parts.Length < 7) {
                    return;
                }
                float p1 = parts[0], p2 = parts[1], p3 = parts[2], v1 = parts[3], v2 = parts[4], v3 = parts[5], v4 = parts[6];
                if (parts.Length == 11) {
                    float p4 = parts[8], p5 = parts[9], v5 = parts[10];
                }
                note.phonemeExpressions.Add(new UExpression("dec") {
                    index = 0,
                    value = 100f - v3,
                });
            } catch (Exception e) {
                throw new FileFormatException($"Invalid Envelope\n{ustLine}", e);
            }
        }

        private static void ParseVibrato(UNote note, string ust, IniLine ustLine) {
            try {
                var args = ust.Split(',').Select(s => float.TryParse(s, out var v) ? v : 0).ToArray();
                if (args.Length >= 1) {
                    note.vibrato.length = args[0];
                }
                if (args.Length >= 2) {
                    note.vibrato.period = args[1];
                }
                if (args.Length >= 3) {
                    note.vibrato.depth = args[2];
                }
                if (args.Length >= 4) {
                    note.vibrato.@in = args[3];
                }
                if (args.Length >= 5) {
                    note.vibrato.@out = args[4];
                }
                if (args.Length >= 6) {
                    note.vibrato.shift = args[5];
                }
                if (args.Length >= 7) {
                    note.vibrato.drift = args[6];
                }
            } catch {
                throw new FileFormatException($"Invalid VBR\n{ustLine}");
            }
        }

        private static void ParsePitchBend(UNote note, string pbs, string pbw, string pby, string pbm) {
            if (!string.IsNullOrWhiteSpace(pbs)) {
                var points = note.pitch.data;
                points.Clear();
                // PBS
                var parts = pbs.Contains(';') ? pbs.Split(';') : pbs.Split(',');
                float pbsX = parts.Length >= 1 && ParseFloat(parts[0], out pbsX) ? pbsX : 0;
                float pbsY = parts.Length >= 2 && ParseFloat(parts[1], out pbsY) ? pbsY : 0;
                points.Add(new PitchPoint(pbsX, pbsY));
                note.pitch.snapFirst = false;
                // PBW, PBY
                var x = points.First().X;
                if (!string.IsNullOrWhiteSpace(pbw)) {
                    var w = pbw.Split(',').Select(s => ParseFloat(s, out var v) ? v : 0).ToList();
                    var y = (pby ?? "").Split(',').Select(s => ParseFloat(s, out var v) ? v : 0).ToList();
                    while (w.Count > y.Count) {
                        y.Add(0);
                    }
                    for (var i = 0; i < w.Count(); i++) {
                        x += w[i];
                        points.Add(new PitchPoint(x, y[i]));
                    }
                }
                // PBM
                if (!string.IsNullOrWhiteSpace(pbm)) {
                    var m = pbm.Split(new[] { ',' });
                    for (var i = 0; i < m.Count() && i < points.Count; i++) {
                        switch (m[i]) {
                            case "r":
                                points[i].shape = PitchPointShape.o;
                                break;
                            case "s":
                                points[i].shape = PitchPointShape.l;
                                break;
                            case "j":
                                points[i].shape = PitchPointShape.i;
                                break;
                            default:
                                points[i].shape = PitchPointShape.io;
                                break;
                        }
                    }
                }
            }
        }

        static bool ParseFloat(string s, out float value) {
            if (string.IsNullOrEmpty(s)) {
                value = 0;
                return true;
            }
            return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        public static void SavePart(UProject project, UVoicePart part, string filePath) {
            WritePart(project, part, part.notes, filePath);
        }

        public static List<UNote> WritePart(UProject project, UVoicePart part, IEnumerable<UNote> notes, string filePath) {
            var sequence = new List<UNote>();
            var track = project.tracks[part.trackNo];
            using (var writer = new StreamWriter(filePath, false, ShiftJIS)) {
                WriteHeader(project, part, writer);
                var position = 0;
                foreach (var note in notes) {
                    if (note.position != position) {
                        writer.WriteLine($"[#{sequence.Count:D4}]");
                        var spacer = UNote.Create();
                        spacer.position = position;
                        spacer.duration = note.position - position;
                        spacer.lyric = "R";
                        spacer.tone = 60;
                        sequence.Add(spacer);
                        WriteNoteBody(project, track, spacer, writer);
                    }
                    writer.WriteLine($"[#{sequence.Count:D4}]");
                    WriteNoteBody(project, track, note, writer);
                    position = note.End;
                    sequence.Add(note);
                }
                WriteFooter(writer);
            }
            return sequence;
        }

        static void WriteHeader(UProject project, UVoicePart part, StreamWriter writer) {
            writer.WriteLine("[#SETTING]");
            writer.WriteLine($"Tempo={project.bpm}");
            writer.WriteLine("Tracks=1");
            if (project.Saved) {
                writer.WriteLine($"Project={project.FilePath.Replace(".ustx", ".ust")}");
            }
            var singer = project.tracks[part.trackNo].Singer;
            if (singer?.Id != null) {
                writer.WriteLine($"VoiceDir={singer.Location}");
            }
            writer.WriteLine($"CacheDir={PathManager.Inst.GetCachePath()}");
            writer.WriteLine("Mode2=True");
        }

        static void WriteFooter(StreamWriter writer) {
            writer.WriteLine("[#TRACKEND]");
        }

        static void WriteNoteBody(UProject project, UTrack track, UNote note, StreamWriter writer) {
            writer.WriteLine($"Length={note.duration}");
            writer.WriteLine($"Lyric={note.lyric}");
            writer.WriteLine($"NoteNum={note.tone}");
            writer.WriteLine("PreUtterance=");
            //writer.WriteLine("VoiceOverlap=");
            if (note.phonemes.Count > 0) {
                var vel = note.phonemes[0].GetExpression(project, track, "vel").Item1;
                writer.WriteLine($"Velocity={(int)vel}");
                var vol = note.phonemes[0].GetExpression(project, track, "vol").Item1;
                writer.WriteLine($"Intensity={(int)vol}");
                var mod = note.phonemes[0].GetExpression(project, track, "mod").Item1;
                writer.WriteLine($"Moduration={(int)mod}");
            }
            WriteEnvelope(note, writer);
            WritePitch(note, writer);
            WriteVibrato(note, writer);
        }

        static void WriteEnvelope(UNote note, StreamWriter writer) {

        }

        static void WritePitch(UNote note, StreamWriter writer) {
            if (note.pitch == null) {
                return;
            }
            var points = note.pitch.data;
            if (points.Count >= 2) {
                writer.WriteLine($"PBS={points[0].X};{points[0].Y}");
                var pbw = new List<string>();
                var pby = new List<string>();
                var pbm = new List<string>();
                for (var i = 0; i < points.Count; ++i) {
                    switch (points[i].shape) {
                        case PitchPointShape.o:
                            pbm.Add("r");
                            break;
                        case PitchPointShape.l:
                            pbm.Add("s");
                            break;
                        case PitchPointShape.i:
                            pbm.Add("j");
                            break;
                        case PitchPointShape.io:
                            pbm.Add("");
                            break;
                    }
                }
                for (var i = 1; i < points.Count; ++i) {
                    var prev = points[i - 1];
                    var current = points[i];
                    pbw.Add((current.X - prev.X).ToString());
                    pby.Add(current.Y.ToString());
                }
                writer.WriteLine($"PBW={string.Join(",", pbw.ToArray())}");
                writer.WriteLine($"PBY={string.Join(",", pby.ToArray())}");
                writer.WriteLine($"PBM={string.Join(",", pbm.ToArray())}");
            }
        }

        static void WriteVibrato(UNote note, StreamWriter writer) {
            var vbr = note.vibrato;
            if (vbr != null && vbr.length > 0) {
                writer.WriteLine($"VBR={vbr.length},{vbr.period},{vbr.depth},{vbr.@in},{vbr.@out},{vbr.shift},{vbr.drift}");
            }
        }

        public static void ParseDiffs(UProject project, UVoicePart part, List<UNote> sequence, string diffFile) {
            using (var reader = new StreamReader(diffFile, ShiftJIS)) {
                var blocks = Ini.ReadBlocks(reader, diffFile, @"\[#\w+\]");
                foreach (var block in blocks) {
                    var header = block.header;
                    switch (header) {
                        case "[#VERSION]":
                        case "[#SETTING]":
                        case "[#TRACKEND]":
                        case "[#PREV]":
                        case "[#NEXT]":
                        case "[#INSERT]":
                        case "[#DELETE]":
                            break;
                        default:
                            if (int.TryParse(header.Substring(2, header.Length - 3), out var noteIndex)) {
                                if (noteIndex < sequence.Count) {
                                    ParseNote(sequence[noteIndex], 0, 0, block.lines, out var _);
                                }
                            }
                            break;
                    }
                }
            }
            var position = 0;
            foreach (var note in sequence) {
                note.position = position;
                position += note.duration;
            }
            SnapPitchPoints(part);
        }
    }
}
