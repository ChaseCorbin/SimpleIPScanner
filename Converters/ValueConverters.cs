using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
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
            if (values.Length < 3 || values[0] is not ObservableCollection<TraceDataPoint> history || values[1] is not double width || values[2] is not double height)
                return new PointCollection();

            if (history.Count < 2) return new PointCollection();

            double maxVal = 100;
            if (values.Length > 3 && values[3] is double m) maxVal = m;

            var points = new PointCollection();
            double xStep = (history.Count > 1) ? width / (history.Count - 1) : width;

            for (int i = 0; i < history.Count; i++)
            {
                double val = history[i].Latency;
                double x = i * xStep;
                
                // If timeout (-1), draw at top (packet loss indicator)
                double y = val < 0 ? 0 : height - (val / maxVal * height);
                
                // Clamp Y to prevent drawing outside bounds
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
}
