using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace FidelSec.UI.Controls
{
    public partial class ProgressRingControl : UserControl
    {
        // ── Dependency Properties ─────────────────────────────────────

        public static readonly DependencyProperty ValueProperty =
            DependencyProperty.Register(nameof(Value), typeof(double), typeof(ProgressRingControl),
                new PropertyMetadata(0.0, OnRedrawNeeded));

        public static readonly DependencyProperty MaximumProperty =
            DependencyProperty.Register(nameof(Maximum), typeof(double), typeof(ProgressRingControl),
                new PropertyMetadata(100.0, OnRedrawNeeded));

        public static readonly DependencyProperty RingSizeProperty =
            DependencyProperty.Register(nameof(RingSize), typeof(double), typeof(ProgressRingControl),
                new PropertyMetadata(160.0, OnRedrawNeeded));

        public static readonly DependencyProperty TrackColorProperty =
            DependencyProperty.Register(nameof(TrackColor), typeof(Brush), typeof(ProgressRingControl),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x31, 0x31, 0x45)), OnRedrawNeeded));

        public static readonly DependencyProperty ProgressColorProperty =
            DependencyProperty.Register(nameof(ProgressColor), typeof(Brush), typeof(ProgressRingControl),
                new PropertyMetadata(new SolidColorBrush(Color.FromRgb(0x7C, 0x3A, 0xED)), OnRedrawNeeded));

        public static readonly DependencyProperty SubLabelProperty =
            DependencyProperty.Register(nameof(SubLabel), typeof(string), typeof(ProgressRingControl),
                new PropertyMetadata(string.Empty, OnRedrawNeeded));

        // ── CLR wrappers ──────────────────────────────────────────────

        public double Value
        {
            get => (double)GetValue(ValueProperty);
            set => SetValue(ValueProperty, value);
        }

        public double Maximum
        {
            get => (double)GetValue(MaximumProperty);
            set => SetValue(MaximumProperty, value);
        }

        public double RingSize
        {
            get => (double)GetValue(RingSizeProperty);
            set => SetValue(RingSizeProperty, value);
        }

        public Brush TrackColor
        {
            get => (Brush)GetValue(TrackColorProperty);
            set => SetValue(TrackColorProperty, value);
        }

        public Brush ProgressColor
        {
            get => (Brush)GetValue(ProgressColorProperty);
            set => SetValue(ProgressColorProperty, value);
        }

        public string SubLabel
        {
            get => (string)GetValue(SubLabelProperty);
            set => SetValue(SubLabelProperty, value);
        }

        // ── Constructor ───────────────────────────────────────────────

        public ProgressRingControl()
        {
            InitializeComponent();
            Loaded += (_, _) => Redraw();
        }

        private static void OnRedrawNeeded(DependencyObject d, DependencyPropertyChangedEventArgs e)
            => ((ProgressRingControl)d).Redraw();

        // ── Drawing ───────────────────────────────────────────────────

        private void Redraw()
        {
            if (!IsLoaded) return;

            double size      = RingSize;
            double stroke    = Math.Max(8, size / 13.0);
            double center    = size / 2.0;
            double radius    = center - stroke / 2.0 - 1.0;

            // Canvas size
            RootCanvas.Width  = size;
            RootCanvas.Height = size;

            // Track ring
            TrackEllipse.Width           = size;
            TrackEllipse.Height          = size;
            TrackEllipse.Stroke          = TrackColor;
            TrackEllipse.StrokeThickness = stroke;

            // Arc
            ProgressArc.StrokeThickness = stroke;
            ProgressArc.Stroke          = ProgressColor;

            double pct   = Maximum <= 0 ? 0 : Math.Max(0, Math.Min(1, Value / Maximum));
            double angle = pct * 360.0;

            if (angle >= 359.9)
            {
                // Full circle — draw as ellipse geometry so start/end don't coincide
                ProgressArc.Data = new EllipseGeometry(new Point(center, center), radius, radius);
            }
            else if (angle < 0.5)
            {
                ProgressArc.Data = null;
            }
            else
            {
                double startRad = -Math.PI / 2.0;
                double endRad   = startRad + angle * (Math.PI / 180.0);
                double sx = center + radius * Math.Cos(startRad);
                double sy = center + radius * Math.Sin(startRad);
                double ex = center + radius * Math.Cos(endRad);
                double ey = center + radius * Math.Sin(endRad);

                var geo = new StreamGeometry();
                using (StreamGeometryContext ctx = geo.Open())
                {
                    ctx.BeginFigure(new Point(sx, sy), isFilled: false, isClosed: false);
                    ctx.ArcTo(
                        new Point(ex, ey),
                        new Size(radius, radius),
                        rotationAngle: 0,
                        isLargeArc: angle > 180,
                        sweepDirection: SweepDirection.Clockwise,
                        isStroked: true,
                        isSmoothJoin: false);
                }
                geo.Freeze();
                ProgressArc.Data = geo;
            }

            // Value text
            double fontSize    = Math.Max(14, size / 5.5);
            ValueTextBlock.FontSize   = fontSize;
            ValueTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0xF1, 0xF5, 0xF9));
            ValueTextBlock.Text       = $"{pct * 100:F0}%";
            ValueTextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));

            bool hasLabel   = !string.IsNullOrEmpty(SubLabel);
            double labelFs  = Math.Max(10, size / 13.0);
            double offsetY  = hasLabel ? fontSize * 0.35 : 0;

            Canvas.SetLeft(ValueTextBlock, center - ValueTextBlock.DesiredSize.Width / 2);
            Canvas.SetTop(ValueTextBlock,  center - ValueTextBlock.DesiredSize.Height / 2 - offsetY);

            // Sub label
            SubLabelTextBlock.FontSize   = labelFs;
            SubLabelTextBlock.Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8));
            SubLabelTextBlock.Text       = SubLabel;
            SubLabelTextBlock.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
            Canvas.SetLeft(SubLabelTextBlock, center - SubLabelTextBlock.DesiredSize.Width / 2);
            Canvas.SetTop(SubLabelTextBlock,  center - SubLabelTextBlock.DesiredSize.Height / 2 + offsetY + labelFs * 0.55);
        }
    }
}
