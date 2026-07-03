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

            double labelWidth = Math.Min(70.0, Math.Max(42.0, width * 0.16));
            double chartWidth = Math.Max(1.0, width - labelWidth);
            double max = Math.Max(1.0, samples.Where(sample => sample >= 0).DefaultIfEmpty(1.0).Max());
            double rowGap = Math.Max(1.0, height / samples.Length * 0.18);
            double rowHeight = Math.Max(2.0, height / samples.Length - rowGap);

            for (int i = 0; i < samples.Length; i++)
            {
                double top = i * (rowHeight + rowGap);
                double value = samples[i];
                Brush brush = value < 0 ? LossBrush : BarBrush;
                double barWidth = value < 0
                    ? Math.Max(4.0, chartWidth * 0.04)
                    : Math.Max(1.0, Math.Min(chartWidth, value / max * chartWidth));

                drawingContext.DrawRectangle(brush, null, new Rect(0, top, barWidth, rowHeight));
            }

            var axisPen = new Pen(BarBrush, 1.0);
            var averagePen = new Pen(AverageBrush, 1.5);
            double averageX = Math.Max(0.0, Math.Min(max, Average)) / max * chartWidth;

            drawingContext.DrawLine(axisPen, new Point(0, 0), new Point(0, height));
            drawingContext.DrawLine(axisPen, new Point(chartWidth, 0), new Point(chartWidth, height));
            drawingContext.DrawLine(averagePen, new Point(averageX, 0), new Point(averageX, height));

            double textSize = Math.Max(9.0, Math.Min(14.0, height * 0.12));
            DrawText(drawingContext, "0", chartWidth + 6, height - textSize - 1, BarBrush, textSize);
            DrawText(drawingContext, FormatValue(max), chartWidth + 6, 0, BarBrush, textSize);
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
