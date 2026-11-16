using System;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace N_Remo
{
    public sealed class ScrollThumbSizeConverter : IMultiValueConverter
    {
        private const double MinimumThumbSize = 28.0;

        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values.Length < 4 ||
                !TryGetDouble(values[0], out double trackLength) ||
                !TryGetDouble(values[1], out double viewportSize) ||
                !TryGetDouble(values[2], out double minimum) ||
                !TryGetDouble(values[3], out double maximum))
            {
                return MinimumThumbSize;
            }

            if (trackLength <= 0 || double.IsNaN(trackLength) || double.IsInfinity(trackLength))
            {
                return MinimumThumbSize;
            }

            double usableTrackLength = Math.Floor(trackLength);
            double scrollableSize = Math.Max(0, maximum - minimum);

            // スクロールする範囲がなければ、つまみはトラック全体を使用する。
            if (scrollableSize <= 0)
            {
                return usableTrackLength;
            }

            // ComboBox のポップアップでは ViewportSize が NaN のままになることがある。
            // その場合にトラック全体の高さを返すと、つまみが中央で固定されて
            // スクロール不能になるため、移動可能な最小サイズへフォールバックする。
            if (double.IsNaN(viewportSize) ||
                double.IsInfinity(viewportSize) ||
                viewportSize <= 0)
            {
                return Math.Min(usableTrackLength, MinimumThumbSize);
            }

            double totalSize = scrollableSize + viewportSize;
            double calculatedSize = usableTrackLength * viewportSize / totalSize;

            // 小数ピクセルのまま最下端へ移動すると、描画境界でつまみの
            // 下側が切れて見えることがあるため、実ピクセル内へ切り下げる。
            double roundedSize = Math.Floor(calculatedSize);
            return Math.Min(usableTrackLength, Math.Max(MinimumThumbSize, roundedSize));
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)
        {
            throw new NotSupportedException();
        }

        private static bool TryGetDouble(object value, out double result)
        {
            if (value == DependencyProperty.UnsetValue || value is null)
            {
                result = 0;
                return false;
            }

            try
            {
                result = System.Convert.ToDouble(value, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                result = 0;
                return false;
            }
        }
    }
}
