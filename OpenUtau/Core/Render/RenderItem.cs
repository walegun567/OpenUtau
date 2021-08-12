﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using OpenUtau.Core.Ustx;
using OpenUtau.Core.Util;
using xxHashSharp;

namespace OpenUtau.Core.Render {

    internal class RenderItem {

        // For resampler
        public string SourceFile;

        public int NoteNum;
        public int Velocity;
        public int Volume;
        public string StrFlags;
        public List<int> PitchData;
        public int RequiredLength;
        public int Modulation;
        public double Tempo;
        public UOto Oto;

        // For connector
        public double SkipOver;

        public double PosMs;
        public double DurMs;
        public List<Vector2> Envelope;

        // Sound data
        public MemorySampleProvider Sound;

        // Progress
        public string phonemeName;
        public RenderEngine.Progress progress;

        public RenderItem(UPhoneme phoneme, UVoicePart part, UProject project) {
            SourceFile = phoneme.Oto.File;
            SourceFile = Path.Combine(PathManager.Inst.InstalledSingersPath, SourceFile);

            var strechRatio = Math.Pow(2, 1.0 - phoneme.Parent.expressions["vel"].value / 100);
            var length = phoneme.Oto.Preutter * strechRatio + phoneme.envelope.data[4].X;
            var requiredLength = Math.Ceiling(length / 50 + 1) * 50;
            var lengthAdjustment = phoneme.TailIntrude == 0 ? phoneme.preutter : phoneme.preutter - phoneme.TailIntrude + phoneme.TailOverlap;

            NoteNum = phoneme.Parent.noteNum;
            Velocity = (int)phoneme.Parent.expressions["vel"].value;
            Volume = (int)phoneme.Parent.expressions["vol"].value;
            StrFlags = phoneme.Parent.GetResamplerFlags();
            PitchData = BuildPitchData(phoneme, part);
            RequiredLength = (int)requiredLength;
            Oto = phoneme.Oto;
            Tempo = project.bpm;

            SkipOver = phoneme.Oto.Preutter * strechRatio - phoneme.preutter;
            PosMs = project.TickToMillisecond(part.PosTick + phoneme.Parent.position + phoneme.position) - phoneme.preutter;
            DurMs = project.TickToMillisecond(phoneme.Duration) + lengthAdjustment;
            Envelope = phoneme.envelope.data;

            phonemeName = phoneme.phoneme;
        }

        public uint HashParameters() {
            return xxHash.CalculateHash(Encoding.UTF8.GetBytes(SourceFile + " " + GetResamplerExeArgs()));
        }

        public string GetResamplerExeArgs() {
            // fresamp.exe <infile> <outfile> <tone> <velocity> <flags> <offset> <length_req>
            // <fixed_length> <endblank> <volume> <modulation> <pitch>
            return FormattableString.Invariant($"{MusicMath.GetNoteString(NoteNum)} {Velocity:D} \"{StrFlags}\" {Oto.Offset} {RequiredLength:D} {Oto.Consonant} {Oto.Cutoff} {Volume:D} {Modulation:D} {Tempo} {Base64.Base64EncodeInt12(PitchData.ToArray())}");
        }

        public ISampleProvider GetSampleProvider() {
            var envelopeSampleProvider = new EnvelopeSampleProvider(Sound, Envelope, SkipOver);
            var sampleRate = Sound.WaveFormat.SampleRate;
            return new OffsetSampleProvider(envelopeSampleProvider) {
                DelayBySamples = (int)(PosMs * sampleRate / 1000),
                TakeSamples = (int)(DurMs * sampleRate / 1000),
                SkipOverSamples = (int)(SkipOver * sampleRate / 1000),
            };
        }

        private List<int> BuildPitchData(UPhoneme phoneme, UVoicePart part) {
            var pitches = new List<int>();
            var lastNote = part.notes.OrderByDescending(x => x).Where(x => x.CompareTo(phoneme.Parent) < 0).FirstOrDefault();
            var nextNote = part.notes.Where(x => x.CompareTo(phoneme.Parent) > 0).FirstOrDefault();
            // Get relevant pitch points
            var pps = new List<PitchPoint>();

            var lastNoteInvolved = lastNote != null && phoneme.Overlapped;
            var nextNoteInvolved = nextNote != null && nextNote.phonemes[0].Overlapped;

            double lastVibratoStartMs = 0;
            double lastVibratoEndMs = 0;
            double vibratoStartMs = 0;
            double vibratoEndMs = 0;

            if (lastNoteInvolved) {
                var offsetMs = (float)DocManager.Inst.Project.TickToMillisecond(phoneme.Parent.position - lastNote.position);
                foreach (var pp in lastNote.pitch.data) {
                    var newpp = pp.Clone();
                    newpp.X -= offsetMs;
                    newpp.Y -= (phoneme.Parent.noteNum - lastNote.noteNum) * 10;
                    pps.Add(newpp);
                }
                if (lastNote.vibrato.depth != 0) {
                    lastVibratoStartMs = -DocManager.Inst.Project.TickToMillisecond(lastNote.duration) * lastNote.vibrato.length / 100;
                    lastVibratoEndMs = 0;
                }
            }

            foreach (var pp in phoneme.Parent.pitch.data) {
                pps.Add(pp);
            }

            if (phoneme.Parent.vibrato.depth != 0) {
                vibratoEndMs = DocManager.Inst.Project.TickToMillisecond(phoneme.Parent.duration);
                vibratoStartMs = vibratoEndMs * (1 - phoneme.Parent.vibrato.length / 100);
            }

            if (nextNoteInvolved) {
                var offsetMs = (float)DocManager.Inst.Project.TickToMillisecond(phoneme.Parent.position - nextNote.position);
                foreach (var pp in nextNote.pitch.data) {
                    var newpp = pp.Clone();
                    newpp.X -= offsetMs;
                    newpp.Y -= (phoneme.Parent.noteNum - nextNote.noteNum) * 10;
                    pps.Add(newpp);
                }
            }

            var startMs = (float)(DocManager.Inst.Project.TickToMillisecond(phoneme.position) - phoneme.Oto.Preutter);
            var endMs = (float)DocManager.Inst.Project.TickToMillisecond(phoneme.Duration) -
                (nextNote != null && nextNote.phonemes[0].Overlapped ? nextNote.phonemes[0].preutter - nextNote.phonemes[0].overlap : 0);
            if (pps.Count > 0) {
                if (pps.First().X > startMs) {
                    pps.Insert(0, new PitchPoint(startMs, pps.First().Y));
                }

                if (pps.Last().X < endMs) {
                    pps.Add(new PitchPoint(endMs, pps.Last().Y));
                }
            } else {
                throw new Exception("Zero pitch points.");
            }

            // Interpolation
            const int intervalTick = 5;
            var intervalMs = (float)DocManager.Inst.Project.TickToMillisecond(intervalTick);
            var currMs = startMs;
            var i = 0;

            while (currMs < endMs) {
                while (pps[i + 1].X < currMs) {
                    i++;
                }

                var pit = MusicMath.InterpolateShape(pps[i].X, pps[i + 1].X, pps[i].Y, pps[i + 1].Y, currMs, pps[i].shape);
                pit *= 10;

                // Apply vibratos
                if (currMs < lastVibratoEndMs && currMs >= lastVibratoStartMs) {
                    pit += InterpolateVibrato(lastNote.vibrato, currMs - lastVibratoStartMs, lastNote);
                }

                if (currMs < vibratoEndMs && currMs >= vibratoStartMs) {
                    pit += InterpolateVibrato(phoneme.Parent.vibrato, currMs - vibratoStartMs, phoneme.Parent);
                }

                pitches.Add((int)pit);
                currMs += intervalMs;
            }

            return pitches;
        }

        private double InterpolateVibrato(UVibrato vibrato, double posMs, UNote note) {
            var lengthMs = vibrato.length / 100 * DocManager.Inst.Project.TickToMillisecond(note.duration);
            var inMs = lengthMs * vibrato.@in / 100;
            var outMs = lengthMs * vibrato.@out / 100;

            var value = -Math.Sin(2 * Math.PI * (posMs / vibrato.period + vibrato.shift / 100)) * vibrato.depth;

            if (posMs < inMs) {
                value *= posMs / inMs;
            } else if (posMs > lengthMs - outMs) {
                value *= (lengthMs - posMs) / outMs;
            }

            return value;
        }
    }
}
