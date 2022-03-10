﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using NAudio.Wave;
using NWaves.Filters.Base;
using NWaves.Filters.Fda;
using NWaves.Signals;
using OpenUtau.Core;
using OpenUtau.Core.Formats;
using OpenUtau.Core.Render;

namespace OpenUtau.Classic {
    interface IWavtool {
        // <output file> <input file> <STP> <note length>
        // [<p1> <p2> <p3> <v1> <v2> <v3> [<v4> <overlap> <p4> [<p5> <v5>]]]
        float[] Concatenate(List<ResamplerItem> resamplerItems, CancellationTokenSource cancellation);
    }

    class SharpWavtool : IWavtool {
        class Segment {
            public float[] samples;
            public double posMs;
            public int posSamples;
            public int skipSamples;
            public int correction = 0;
            public IList<Vector2> envelope;
            public int headWindowStart;
            public double headWindowF0;
            public double? headPhase;
            public int tailWindowStart;
            public double tailWindowF0;
            public double? tailPhase;
        }

        public float[] Concatenate(List<ResamplerItem> resamplerItems, CancellationTokenSource cancellation) {
            if (cancellation.IsCancellationRequested) {
                return null;
            }
            var phrase = resamplerItems[0].phrase;
            double posOffset = resamplerItems[0].phone.position * phrase.tickToMs - resamplerItems[0].phone.preutterMs;

            var segments = new List<Segment>();
            foreach (var item in resamplerItems) {
                if (!File.Exists(item.outputFile)) {
                    continue;
                }
                var segment = new Segment();
                segments.Add(segment);
                using (var waveStream = Wave.OpenFile(item.outputFile)) {
                    segment.samples = Wave.GetSamples(waveStream.ToSampleProvider().ToMono(1, 0));
                }
                segment.posMs = item.phone.position * item.phrase.tickToMs - item.phone.preutterMs - posOffset;
                segment.posSamples = (int)Math.Round(segment.posMs * 44100 / 1000);
                segment.skipSamples = (int)Math.Round(item.skipOver * 44100 / 1000);
                segment.envelope = EnvelopeMsToSamples(item.phone.envelope, segment.skipSamples);

                var headWindow = GetHeadWindow(segment.samples, segment.envelope, out segment.headWindowStart);
                segment.headWindowF0 = GetF0AtSample(phrase,
                    segment.posSamples - segment.skipSamples + segment.headWindowStart + headWindow.Length / 2);
                segment.headPhase = CalcPhase(headWindow,
                    segment.posSamples - segment.skipSamples + segment.headWindowStart, 44100, segment.headWindowF0);

                var tailWindow = GetTailWindow(segment.samples, segment.envelope, out segment.tailWindowStart);
                segment.tailWindowF0 = GetF0AtSample(phrase,
                    segment.posSamples - segment.skipSamples + segment.tailWindowStart + tailWindow.Length / 2);
                segment.tailPhase = CalcPhase(tailWindow,
                    segment.posSamples - segment.skipSamples + segment.tailWindowStart, 44100, segment.tailWindowF0);
            }

            for (int i = 1; i < segments.Count; ++i) {
                double? tailPhase = segments[i - 1].tailPhase;
                double? headPhase = segments[i].headPhase;
                if (!tailPhase.HasValue || !headPhase.HasValue) {
                    continue;
                }
                double lastCorrAngle = segments[i - 1].correction * 2.0 * Math.PI / 44100.0 * segments[i].headWindowF0;
                double diff = headPhase.Value - (tailPhase.Value - lastCorrAngle);
                while (diff < 0) {
                    diff += 2 * Math.PI;
                }
                while (diff >= 2 * Math.PI) {
                    diff -= 2 * Math.PI;
                }
                if (Math.Abs(diff - 2 * Math.PI) < diff) {
                    diff -= 2 * Math.PI;
                }
                segments[i].correction = (int)(diff / 2 / Math.PI * 44100 / segments[i].headWindowF0);
            }

            var phraseSamples = new float[0];
            foreach (var segment in segments) {
                Array.Resize(ref phraseSamples, segment.posSamples + segment.correction + segment.samples.Length);
                ApplyEnvelope(segment.samples, segment.envelope);
                for (int i = 0; i < segment.samples.Length - segment.skipSamples; i++) {
                    phraseSamples[segment.posSamples + segment.correction + i] += segment.samples[segment.skipSamples + i];
                }
            }
            return phraseSamples;
        }

        private static void ApplyEnvelope(float[] data, IList<Vector2> envelope) {
            int nextPoint = 0;
            for (int i = 0; i < data.Length; ++i) {
                while (nextPoint < envelope.Count && i > envelope[nextPoint].X) {
                    nextPoint++;
                }
                float gain;
                if (nextPoint == 0) {
                    gain = envelope.First().Y;
                } else if (nextPoint >= envelope.Count) {
                    gain = envelope.Last().Y;
                } else {
                    var p0 = envelope[nextPoint - 1];
                    var p1 = envelope[nextPoint];
                    if (p0.X >= p1.X) {
                        gain = p0.Y;
                    } else {
                        gain = p0.Y + (p1.Y - p0.Y) * (i - p0.X) / (p1.X - p0.X);
                    }
                }
                data[i] *= gain;
            }
        }

        private static IList<Vector2> EnvelopeMsToSamples(IList<Vector2> envelope, int skipOverSamples) {
            envelope = new List<Vector2>(envelope);
            double shift = -envelope[0].X;
            for (var i = 0; i < envelope.Count; i++) {
                var point = envelope[i];
                point.X = (float)((point.X + shift) * 44100 / 1000) + skipOverSamples;
                point.Y /= 100;
                envelope[i] = point;
            }
            return envelope;
        }

        private float[] GetHeadWindow(float[] samples, IList<Vector2> envelope, out int windowStart) {
            var windowCenter = (envelope[0] + envelope[1]) * 0.5f;
            windowStart = Math.Max((int)windowCenter.X - 440, 0);
            int windowLength = Math.Min(880, samples.Length - windowStart);
            return samples.Skip(windowStart).Take(windowLength).ToArray();
        }

        private float[] GetTailWindow(float[] samples, IList<Vector2> envelope, out int windowStart) {
            var windowCenter = (envelope[envelope.Count - 1] + envelope[envelope.Count - 2]) * 0.5f;
            windowStart = Math.Max((int)windowCenter.X - 440, 0);
            int windowLength = Math.Min(880, samples.Length - windowStart);
            return samples.Skip(windowStart).Take(windowLength).ToArray();
        }

        private double GetF0AtSample(RenderPhrase phrase, float sampleIndex) {
            int pitchIndex = (int)Math.Round(sampleIndex / 44100 * 1000 / phrase.tickToMs / 5);
            pitchIndex = Math.Clamp(pitchIndex, 0, phrase.pitches.Length);
            return MusicMath.ToneToFreq(phrase.pitches[pitchIndex] / 100);
        }

        private double? CalcPhase(float[] samples, int offset, int fs, double f) {
            if (samples.Length < 4) {
                return null;
            }
            var x = new DiscreteSignal(fs, samples);
            var peakTf = DesignFilter.IirPeak(f / fs, 5);
            var filter = new ZiFilter(peakTf);
            samples = filter.ZeroPhase(x).Samples;
            if (samples.Max() > 10) {
                return null;
            }
            double left = 0;
            double right = 0;
            for (int i = samples.Length / 2 - 1; i >= 1; --i) {
                if (samples[i] >= samples[i - 1] && samples[i] >= samples[i + 1]) {
                    left = i;
                    break;
                }
            }
            for (int i = samples.Length / 2; i <= samples.Length - 2; ++i) {
                if (samples[i] >= samples[i - 1] && samples[i] >= samples[i + 1]) {
                    right = i;
                    break;
                }
            }
            if (left >= right) {
                return null;
            }
            double actualF = fs / (right - left);
            if (Math.Abs(f - actualF) > f * 0.25) {
                return null;
            }
            double t = (offset + (left + right) * 0.5) / fs * f;
            return 2 * Math.PI * (Math.Round(t) - t);
        }
    }
}
