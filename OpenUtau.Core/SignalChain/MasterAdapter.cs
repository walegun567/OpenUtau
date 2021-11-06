﻿using System;
using NAudio.Wave;

namespace OpenUtau.Core.SignalChain {
    class MasterAdapter : ISampleProvider {
        private readonly WaveFormat waveFormat;
        private readonly ISignalSource source;
        private int position;

        public WaveFormat WaveFormat => waveFormat;
        public int Paused { get; private set; }

        public MasterAdapter(ISignalSource source) {
            waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(44100, 2);
            this.source = source;
        }

        public int Read(float[] buffer, int offset, int count) {
            for (int i = offset; i < offset + count; ++i) {
                buffer[i] = 0;
            }
            if (!source.IsReady(position, count)) {
                Paused += count;
                return count;
            } else {
                int pos = source.Mix(position, buffer, offset, count);
                int n = Math.Max(0, pos - position);
                position = pos;
                return n;
            }
        }

        public void SetPosition(int position) {
            this.position = position;
            Paused = 0;
        }
    }
}
