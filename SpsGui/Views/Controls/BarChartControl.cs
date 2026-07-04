using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;

namespace SpsGui.Views.Controls
{
    /// <summary>
    /// Draws a transparent ping bar chart where negative samples are packet losses.
    /// </summary>
    public class BarChartControl : FrameworkElement
    {
        private const double PlaceholderSampleValue = -2.0;

        /// <summary>
        /// Samples dependency property.
        /// </summary>
        public static readonly DependencyProperty SamplesProperty =
            DependencyProperty.Register(nameof(Samples), typeof(IEnumerable), typeof(BarChartControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Average dependency property.
        /// </summary>
        public static readonly DependencyProperty AverageProperty =
            DependencyProperty.Register(nameof(Average), typeof(double), typeof(BarChartControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Bar brush dependency property.
        /// </summary>
        public static readonly DependencyProperty BarBrushProperty =
            DependencyProperty.Register(nameof(BarBrush), typeof(Brush), typeof(BarChartControl),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Average line brush dependency property.
        /// </summary>
        public static readonly DependencyProperty AverageBrushProperty =
            DependencyProperty.Register(nameof(AverageBrush), typeof(Brush), typeof(BarChartControl),
                new FrameworkPropertyMetadata(Brushes.LimeGreen, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Loss bar brush dependency property.
        /// </summary>
        public static readonly DependencyProperty LossBrushProperty =
            DependencyProperty.Register(nameof(LossBrush), typeof(Brush), typeof(BarChartControl),
                new FrameworkPropertyMetadata(Brushes.Red, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Gets or sets ping samples. Negative values are rendered as packet loss bars.
        /// </summary>
        public IEnumerable Samples
        {
            get { return (IEnumerable)GetValue(SamplesProperty); }
            set { SetValue(SamplesProperty, value); }
        }

        /// <summary>
        /// Gets or sets the average ping line position.
        /// </summary>
        public double Average
        {
            get { return (double)GetValue(AverageProperty); }
            set { SetValue(AverageProperty, value); }
        }

        /// <summary>
        /// Gets or sets the brush used for successful ping bars.
        /// </summary>
        public Brush BarBrush
        {
            get { return (Brush)GetValue(BarBrushProperty); }
            set { SetValue(BarBrushProperty, value); }
        }

        /// <summary>
        /// Gets or sets the brush used for the average line.
        /// </summary>
        public Brush AverageBrush
        {
            get { return (Brush)GetValue(AverageBrushProperty); }
            set { SetValue(AverageBrushProperty, value); }
        }

        /// <summary>
        /// Gets or sets the brush used for packet loss bars.
        /// </summary>
        public Brush LossBrush
        {
            get { return (Brush)GetValue(LossBrushProperty); }
            set { SetValue(LossBrushProperty, value); }
        }

        /// <summary>
        /// Draws the transparent chart.
        /// </summary>
        /// <param name="drawingContext">Drawing context provided by WPF. Must not be null.</param>
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            double[] samples = Samples == null
                ? new double[0]
                : Samples.Cast<object>().Select(Convert.ToDouble).ToArray();
            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0 || samples.Length == 0)
            {
                return;
            }

            double textSize = Math.Max(9.0, Math.Min(14.0, height * 0.12));
            double labelWidth = Math.Min(70.0, Math.Max(34.0, width * 0.14));
            double labelHeight = textSize + 4.0;
            double plotLeft = 2.0;
            double plotTop = 2.0;
            double plotRight = Math.Max(plotLeft + 1.0, width - labelWidth);
            double plotBottom = Math.Max(plotTop + 1.0, height - labelHeight);
            double plotWidth = plotRight - plotLeft;
            double plotHeight = plotBottom - plotTop;
            double max = Math.Max(1.0, samples.Where(sample => sample >= 0).DefaultIfEmpty(1.0).Max());
            double barGap = Math.Max(1.0, plotWidth / samples.Length * 0.18);
            double barWidth = Math.Max(1.0, plotWidth / samples.Length - barGap);

            var axisPen = new Pen(BarBrush, 1.0);
            var averagePen = new Pen(AverageBrush, 1.5);

            drawingContext.DrawLine(axisPen, new Point(plotRight, plotTop), new Point(plotRight, plotBottom));
            drawingContext.DrawLine(axisPen, new Point(plotLeft, plotBottom), new Point(plotRight, plotBottom));

            for (int i = 0; i < samples.Length; i++)
            {
                double value = samples[i];
                if (value == PlaceholderSampleValue)
                {
                    continue;
                }

                Brush brush = value < 0 ? LossBrush : BarBrush;
                double x = plotLeft + i * (barWidth + barGap);
                double barHeight = value < 0
                    ? Math.Max(3.0, plotHeight)
                    : Math.Max(1.0, Math.Min(plotHeight, value / max * plotHeight));
                double top = plotBottom - barHeight;

                drawingContext.DrawRectangle(brush, null, new Rect(x, top, barWidth, barHeight));
            }

            double averageY = plotBottom - Math.Max(0.0, Math.Min(max, Average)) / max * plotHeight;
            drawingContext.DrawLine(averagePen, new Point(plotLeft, averageY), new Point(plotRight, averageY));

            DrawText(drawingContext, FormatValue(max), plotRight + 6.0, plotTop, BarBrush, textSize);
            DrawText(drawingContext, "0", plotRight + 6.0, plotBottom - textSize, BarBrush, textSize);
        }

        private static string FormatValue(double value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private void DrawText(DrawingContext context, string text, double x, double y, Brush brush, double size)
        {
            var formatted = new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            context.DrawText(formatted, new Point(x, y));
        }
    }
}
