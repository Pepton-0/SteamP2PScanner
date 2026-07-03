using System;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace SpsGui.Views.Controls
{
    /// <summary>
    /// Draws a transparent box plot from minimum, quartiles, median, and maximum values.
    /// </summary>
    public class BoxPlotControl : FrameworkElement
    {
        /// <summary>
        /// Minimum value dependency property.
        /// </summary>
        public static readonly DependencyProperty MinimumProperty =
            DependencyProperty.Register(nameof(Minimum), typeof(double), typeof(BoxPlotControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// First quartile dependency property.
        /// </summary>
        public static readonly DependencyProperty FirstQuartileProperty =
            DependencyProperty.Register(nameof(FirstQuartile), typeof(double), typeof(BoxPlotControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Median dependency property.
        /// </summary>
        public static readonly DependencyProperty MedianProperty =
            DependencyProperty.Register(nameof(Median), typeof(double), typeof(BoxPlotControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Third quartile dependency property.
        /// </summary>
        public static readonly DependencyProperty ThirdQuartileProperty =
            DependencyProperty.Register(nameof(ThirdQuartile), typeof(double), typeof(BoxPlotControl),
                new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Maximum value dependency property.
        /// </summary>
        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(BoxPlotControl),
                new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Stroke brush dependency property.
        /// </summary>
        public static readonly DependencyProperty StrokeProperty =
            DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(BoxPlotControl),
                new FrameworkPropertyMetadata(Brushes.White, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Median brush dependency property.
        /// </summary>
        public static readonly DependencyProperty MedianBrushProperty =
            DependencyProperty.Register(nameof(MedianBrush), typeof(Brush), typeof(BoxPlotControl),
                new FrameworkPropertyMetadata(Brushes.Gold, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>
        /// Gets or sets the minimum observed value.
        /// </summary>
        public double Minimum
        {
            get { return (double)GetValue(MinimumProperty); }
            set { SetValue(MinimumProperty, value); }
        }

        /// <summary>
        /// Gets or sets the first quartile value.
        /// </summary>
        public double FirstQuartile
        {
            get { return (double)GetValue(FirstQuartileProperty); }
            set { SetValue(FirstQuartileProperty, value); }
        }

        /// <summary>
        /// Gets or sets the median value.
        /// </summary>
        public double Median
        {
            get { return (double)GetValue(MedianProperty); }
            set { SetValue(MedianProperty, value); }
        }

        /// <summary>
        /// Gets or sets the third quartile value.
        /// </summary>
        public double ThirdQuartile
        {
            get { return (double)GetValue(ThirdQuartileProperty); }
            set { SetValue(ThirdQuartileProperty, value); }
        }

        /// <summary>
        /// Gets or sets the maximum value used as the right edge scale.
        /// </summary>
        public double Maximum
        {
            get { return (double)GetValue(MaximumProperty); }
            set { SetValue(MaximumProperty, value); }
        }

        /// <summary>
        /// Gets or sets the default line and text brush.
        /// </summary>
        public Brush Stroke
        {
            get { return (Brush)GetValue(StrokeProperty); }
            set { SetValue(StrokeProperty, value); }
        }

        /// <summary>
        /// Gets or sets the brush used for median text and line.
        /// </summary>
        public Brush MedianBrush
        {
            get { return (Brush)GetValue(MedianBrushProperty); }
            set { SetValue(MedianBrushProperty, value); }
        }

        /// <summary>
        /// Draws the transparent box plot.
        /// </summary>
        /// <param name="drawingContext">Drawing context provided by WPF. Must not be null.</param>
        protected override void OnRender(DrawingContext drawingContext)
        {
            base.OnRender(drawingContext);

            double width = ActualWidth;
            double height = ActualHeight;
            if (width <= 0 || height <= 0)
            {
                return;
            }

            double scaleMax = Math.Max(1.0, Maximum);
            double textSize = Math.Max(9.0, Math.Min(16.0, height * 0.22));
            double graphHeight = Math.Min(22.0, Math.Max(10.0, height * 0.28));
            double graphTop = height - graphHeight;
            double graphCenter = graphTop + graphHeight / 2.0;

            var pen = new Pen(Stroke, 1.5);
            var medianPen = new Pen(MedianBrush, 2.0);

            DrawText(drawingContext, "0", 0, 0, Stroke, textSize);
            DrawTextRight(drawingContext, FormatInteger(Maximum), width, 0, Stroke, textSize);
            DrawCenteredMetricText(drawingContext, textSize);

            double zeroX = ToX(0.0, scaleMax, width);
            double minX = ToX(Minimum, scaleMax, width);
            double q1X = ToX(FirstQuartile, scaleMax, width);
            double medX = ToX(Median, scaleMax, width);
            double q3X = ToX(ThirdQuartile, scaleMax, width);
            double maxX = ToX(Maximum, scaleMax, width);

            drawingContext.DrawLine(pen, new Point(zeroX, graphCenter), new Point(maxX, graphCenter));
            drawingContext.DrawLine(pen, new Point(minX, graphTop), new Point(minX, graphTop + graphHeight));
            drawingContext.DrawLine(pen, new Point(maxX, graphTop), new Point(maxX, graphTop + graphHeight));
            drawingContext.DrawRectangle(null, pen, new Rect(new Point(q1X, graphTop), new Point(q3X, graphTop + graphHeight)));
            drawingContext.DrawLine(medianPen, new Point(medX, graphTop), new Point(medX, graphTop + graphHeight));
        }

        private static double ToX(double value, double maximum, double width)
        {
            double clamped = Math.Max(0.0, Math.Min(maximum, value));
            return clamped / maximum * width;
        }

        private static string FormatValue(double value)
        {
            return value.ToString("0.#", CultureInfo.InvariantCulture);
        }

        private static string FormatInteger(double value)
        {
            return Math.Round(value).ToString(CultureInfo.InvariantCulture);
        }

        private void DrawCenteredMetricText(DrawingContext context, double size)
        {
            string prefix = "Min:" + FormatValue(Minimum) +
                            "/Q1:" + FormatValue(FirstQuartile) + "/";
            string median = "Med:" + FormatValue(Median);
            string suffix = "/Q3:" + FormatValue(ThirdQuartile);

            FormattedText prefixText = CreateText(prefix, Stroke, size);
            FormattedText medianText = CreateText(median, MedianBrush, size);
            FormattedText suffixText = CreateText(suffix, Stroke, size);
            double totalWidth = prefixText.WidthIncludingTrailingWhitespace +
                                medianText.WidthIncludingTrailingWhitespace +
                                suffixText.WidthIncludingTrailingWhitespace;
            double x = Math.Max(0.0, (ActualWidth - totalWidth) / 2.0);

            context.DrawText(prefixText, new Point(x, 0));
            x += prefixText.WidthIncludingTrailingWhitespace;
            context.DrawText(medianText, new Point(x, 0));
            x += medianText.WidthIncludingTrailingWhitespace;
            context.DrawText(suffixText, new Point(x, 0));
        }

        private void DrawText(DrawingContext context, string text, double x, double y, Brush brush, double size)
        {
            context.DrawText(CreateText(text, brush, size), new Point(x, y));
        }

        private void DrawTextRight(DrawingContext context, string text, double right, double y, Brush brush, double size)
        {
            FormattedText formatted = CreateText(text, brush, size);
            context.DrawText(formatted, new Point(Math.Max(0.0, right - formatted.WidthIncludingTrailingWhitespace), y));
        }

        private FormattedText CreateText(string text, Brush brush, double size)
        {
            return new FormattedText(
                text,
                CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight,
                new Typeface("Segoe UI"),
                size,
                brush,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);
        }
    }
}
