﻿using System.IO;
using NAudio.Wave;
using OpenUtau.Core;
using OpenUtau.Core.Render;
using OpenUtau.Core.SignalChain;
using Serilog;

namespace OpenUtau.Classic {
    internal class WorldlineResampler : IResampler {
        public const string name = "worldline";
        public string Name => name;
        public string FilePath { get; private set; }

        public WorldlineResampler() {
            string ext = OS.IsWindows() ? ".dll" : OS.IsMacOS() ? ".dylib" : ".so";
            FilePath = Path.Join(PathManager.Inst.RootPath, Name + ext);
        }

        public float[] DoResampler(ResamplerItem item, ILogger logger) {
            return Worldline.Resample(item);
        }

        public string DoResamplerReturnsFile(ResamplerItem item, ILogger logger) {
            var samples = DoResampler(item, logger);
            var source = new WaveSource(0, 0, 0, 1);
            source.SetSamples(samples);
            WaveFileWriter.CreateWaveFile16(item.outputFile, new ExportAdapter(source).ToMono(1, 0));
            return item.outputFile;
        }

        public void CheckPermissions() { }

        public override string ToString() => Name;
    }
}
