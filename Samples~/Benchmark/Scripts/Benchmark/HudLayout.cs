using UnityEngine;

namespace Alpershin.Vat.Samples.Benchmark
{
    /// <summary>
    /// Geometry of the on-screen panel: where the readout, the results and every button sit.
    /// Kept apart from drawing so the same rects serve both the renderer and touch hit-testing.
    /// </summary>
    public sealed class HudLayout
    {
        private const float BaseButtonHeight = 44f;
        private const float BaseMargin = 8f;
        private const float BaseWidth = 520f;
        private const float BaseFontSize = 15f;

        private Rect[] _variants = System.Array.Empty<Rect>();
        private Rect[] _counts = System.Array.Empty<Rect>();
        private int _screenWidth;
        private int _screenHeight;
        private int _variantCount;
        private int _countCount;
        private int _resultLines;

        public Rect Panel { get; private set; }
        public Rect Headline { get; private set; }
        public Rect Readout { get; private set; }
        public Rect Results { get; private set; }
        public Rect Sample { get; private set; }
        public float Scale { get; private set; } = 1f;
        public float FontSize => BaseFontSize * Scale;
        public float HeadlineFontSize => BaseFontSize * 2f * Scale;
        public float Margin => BaseMargin * Scale;

        public Rect Variant(int index) => _variants[index];
        public Rect Count(int index) => _counts[index];

        public bool NeedsRebuild(int variantCount, int countCount, int resultLines)
        {
            return _screenWidth != Screen.width
                || _screenHeight != Screen.height
                || _variantCount != variantCount
                || _countCount != countCount
                || _resultLines != resultLines;
        }

        public void Rebuild(float requestedScale, int variantCount, int countCount, int resultLines)
        {
            _screenWidth = Screen.width;
            _screenHeight = Screen.height;
            _variantCount = variantCount;
            _countCount = countCount;
            _resultLines = resultLines;

            Scale = requestedScale > 0f ? requestedScale : AutoScale();

            var margin = Margin;
            var button = BaseButtonHeight * Scale;
            var line = FontSize * 1.35f;
            var width = Mathf.Min(BaseWidth * Scale, _screenWidth - margin * 2f);

            var headlineHeight = FontSize * 2.4f;
            var readoutHeight = line * 6f;
            var resultsHeight = resultLines > 0 ? line * (resultLines + 1) : 0f;
            var rows = (variantCount > 0 ? 1 : 0) + (countCount > 0 ? 1 : 0) + 1;
            var height = margin * 2f + headlineHeight + readoutHeight + resultsHeight + rows * (button + margin);

            Panel = new Rect(margin, margin, width, height);

            var cursor = Panel.y + margin;
            var contentX = Panel.x + margin;
            var contentWidth = width - margin * 2f;

            Headline = new Rect(contentX, cursor, contentWidth, headlineHeight);
            cursor += headlineHeight;

            Readout = new Rect(contentX, cursor, contentWidth, readoutHeight);
            cursor += readoutHeight;

            Results = new Rect(contentX, cursor, contentWidth, resultsHeight);
            cursor += resultsHeight;

            _variants = Row(variantCount, contentX, cursor, contentWidth, button, margin);
            cursor += variantCount > 0 ? button + margin : 0f;

            _counts = Row(countCount, contentX, cursor, contentWidth, button, margin);
            cursor += countCount > 0 ? button + margin : 0f;

            Sample = new Rect(contentX, cursor, contentWidth, button);
        }

        private static Rect[] Row(int cells, float x, float y, float width, float height, float margin)
        {
            if (cells <= 0)
            {
                return System.Array.Empty<Rect>();
            }

            var rects = new Rect[cells];
            var cellWidth = (width - margin * (cells - 1)) / cells;

            for (var i = 0; i < cells; i++)
            {
                rects[i] = new Rect(x + i * (cellWidth + margin), y, cellWidth, height);
            }

            return rects;
        }

        // Touch targets have to stay finger-sized on a dense phone screen.
        private static float AutoScale()
        {
            var dpi = Screen.dpi > 1f ? Screen.dpi : 160f;
            return Mathf.Clamp(dpi / 160f, 1f, 4f);
        }
    }
}
