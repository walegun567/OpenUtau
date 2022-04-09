﻿using System.Threading;
using System.Threading.Tasks;
using OpenUtau.Core.Ustx;

namespace OpenUtau.Core.Render {
    /// <summary>
    /// Render result of a phrase.
    /// </summary>
    public class RenderResult {
        public float[] samples;

        /// <summary>
        /// The length of leading samples.
        /// </summary>
        public double leadingMs;

        /// <summary>
        /// Start position of non-leading samples.
        /// </summary>
        public double positionMs;

        /// <summary>
        /// Length estimated before actual render.
        /// </summary>
        public double estimatedLengthMs;
    }

    public class RenderPitchResult {
        public float[] ticks;
        public float[] tones;
    }

    /// <summary>
    /// Interface of phrase-based renderer.
    /// </summary>
    public interface IRenderer {
        bool SupportsRenderPitch { get; }
        bool SupportsExpression(UExpressionDescriptor descriptor);
        RenderResult Layout(RenderPhrase phrase);
        Task<RenderResult> Render(RenderPhrase phrase, Progress progress, CancellationTokenSource cancellation, bool isPreRender = false);
        RenderPitchResult LoadRenderedPitch(RenderPhrase phrase);
    }
}
