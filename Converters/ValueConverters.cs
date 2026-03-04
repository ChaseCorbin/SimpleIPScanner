using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using SimpleIPScanner.Models;

namespace SimpleIPScanner.Converters
{
    public class InverseBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => !(bool)value;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class InverseBooleanToVisibilityConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => (bool)value ? Visibility.Collapsed : Visibility.Visible;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class LatencyToPointsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3 || values[0] is not IReadOnlyList<TraceDataPoint> history || values[1] is not double width || values[2] is not double height)
                return new PointCollection();

            if (history.Count < 2) return new PointCollection();

            double maxVal = 100;
            if (values.Length > 3 && values[3] is double m) maxVal = m;

            // Time-relative X positioning when ViewStart/ViewEnd are provided
            bool timeRelative = values.Length > 5
                && values[4] is DateTime viewStart && values[5] is DateTime viewEnd
                && (viewEnd - viewStart).TotalSeconds > 0;

            double windowSeconds = timeRelative ? ((DateTime)values[5] - (DateTime)values[4]).TotalSeconds : 0;
            DateTime vs = timeRelative ? (DateTime)values[4] : DateTime.MinValue;

            var points = new PointCollection();
            double xStep = (history.Count > 1) ? width / (history.Count - 1) : width;

            for (int i = 0; i < history.Count; i++)
            {
                double val = history[i].Latency;

                double x = timeRelative
                    ? (history[i].Timestamp - vs).TotalSeconds / windowSeconds * width
                    : i * xStep;

                // If timeout (-1), draw at top (packet loss indicator)
                double y = val < 0 ? 0 : height - (val / maxVal * height);

                // Clamp to prevent drawing outside bounds
                y = Math.Max(0, Math.Min(height, y));

                points.Add(new Point(x, y));
            }

            return points;
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class LatencyToCoordinateConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 3) return 0.0;

            double latency = 0;
            if (values[0] is double d) latency = d;
            else if (values[0] != null && double.TryParse(values[0].ToString(), out double parsed)) latency = parsed;

            if (values[1] is not double maxLatency || values[2] is not double height)
                return 0.0;

            if (maxLatency <= 0) return height;
            
            // Higher latency = Lower Y (y=0 is top)
            double y = height - (latency / maxLatency * height);
            
            // If value is off-chart (higher than max), place it above (negative or 0)
            return Math.Max(-50, Math.Min(height + 50, y));
        }
        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public class LatencyToBrushConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 2 || values[0] is not double maxVal)
                return Application.Current.FindResource("OnlineGreenBrush");

            var green = (Color)Application.Current.FindResource("OnlineGreen");
            var orange = (Color)Application.Current.FindResource("WarningOrange");
            var red = (Color)Application.Current.FindResource("ErrorRed");

            // If everything is low latency, just keep it green
            if (maxVal <= 100) return new SolidColorBrush(green);

            var brush = new LinearGradientBrush { StartPoint = new Point(0, 1), EndPoint = new Point(0, 0) };
            
            // 0 to 100ms is green
            double stop1 = 100.0 / maxVal;
            brush.GradientStops.Add(new GradientStop(green, 0));
            brush.GradientStops.Add(new GradientStop(green, stop1));
            
            // Transition to orange at 100ms
            brush.GradientStops.Add(new GradientStop(orange, stop1 + 0.01));
            
            // If it goes really high (e.g. > 200ms), transition to red
            if (maxVal > 200)
            {
                double stop2 = 200.0 / maxVal;
                brush.GradientStops.Add(new GradientStop(orange, stop2));
                brush.GradientStops.Add(new GradientStop(red, stop2 + 0.1));
            }
            else
            {
                brush.GradientStops.Add(new GradientStop(orange, 1));
            }

            return brush;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts a plain <c>List&lt;double&gt;</c> of Mbps samples to a WPF PointCollection
    /// suitable for a Polyline chart. Higher values appear higher on the canvas (y=0 is top).
    /// Inputs: history (IReadOnlyList&lt;double&gt;), width, height, maxVal.
    /// </summary>
    public class SpeedToPointsConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 4
                || values[0] is not System.Collections.Generic.IReadOnlyList<double> history
                || values[1] is not double width
                || values[2] is not double height
                || values[3] is not double maxVal
                || history.Count < 2
                || maxVal <= 0)
                return new PointCollection();

            var points = new PointCollection();
            double xStep = width / (history.Count - 1);

            for (int i = 0; i < history.Count; i++)
            {
                double x = i * xStep;
                double y = height - (history[i] / maxVal * height);
                y = Math.Max(0, Math.Min(height, y));
                points.Add(new Point(x, y));
            }

            return points;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts a list of event <see cref="TraceDataPoint"/>s (timeouts or latency spikes) to a
    /// <see cref="PathGeometry"/> consisting of one vertical line per event, positioned by timestamp.
    /// Inputs: events (IReadOnlyList&lt;TraceDataPoint&gt;), width, height, viewStart (DateTime), viewEnd (DateTime).
    /// </summary>
    public class EventMarkersConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 5
                || values[0] is not IReadOnlyList<TraceDataPoint> events
                || values[1] is not double width
                || values[2] is not double height
                || values[3] is not DateTime viewStart
                || values[4] is not DateTime viewEnd)
                return Geometry.Empty;

            double windowSeconds = (viewEnd - viewStart).TotalSeconds;
            if (windowSeconds <= 0 || events.Count == 0 || width <= 0 || height <= 0)
                return Geometry.Empty;

            var geometry = new PathGeometry();
            foreach (var evt in events)
            {
                double x = (evt.Timestamp - viewStart).TotalSeconds / windowSeconds * width;
                if (x < 0 || x > width) continue;

                var figure = new PathFigure { StartPoint = new Point(x, 0), IsClosed = false };
                figure.Segments.Add(new LineSegment(new Point(x, height), isStroked: true));
                geometry.Figures.Add(figure);
            }

            return geometry;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    /// <summary>
    /// Converts a single hop latency (long ms) to a color brush.
    /// &lt; 0      → gray  (timeout / no response)
    /// 0–99    → green
    /// 100–199 → orange
    /// ≥ 200   → red
    /// </summary>
    public class HopLatencyColorConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is long latency)
            {
                if (latency < 0)
                    return Application.Current.FindResource("OfflineGrayBrush");
                if (latency < 100)
                    return Application.Current.FindResource("OnlineGreenBrush");
                if (latency < 200)
                    return new SolidColorBrush((Color)Application.Current.FindResource("WarningOrange"));
                return new SolidColorBrush((Color)Application.Current.FindResource("ErrorRed"));
            }
            return Application.Current.FindResource("AccentCyanBrush");
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }
}
