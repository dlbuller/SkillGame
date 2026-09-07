using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Shapes;

namespace SkillGameWpf
{
    /// <summary>A themed modal dialog that matches the rest of the app — replaces the old grey Windows MessageBox.</summary>
    public static class AppDialog
    {
        public static void Info(string title, string message) => Show(title, message, false, "OK", null);
        public static bool Confirm(string title, string message, string ok = "YES", string cancel = "CANCEL")
            => Show(title, message, true, ok, cancel);

        private static bool Show(string title, string message, bool confirm, string okText, string? cancelText)
        {
            var app = Application.Current;
            var win = new Window
            {
                WindowStyle = WindowStyle.None,
                AllowsTransparency = true,
                Background = Brushes.Transparent,
                SizeToContent = SizeToContent.WidthAndHeight,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Owner = app?.MainWindow,
                WindowStartupLocation = app?.MainWindow != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
            };
            bool result = false;
            void Close(bool r) { result = r; win.Close(); }

            var panel = new StackPanel { MaxWidth = 440 };
            panel.Children.Add(new TextBlock
            {
                Text = title, FontFamily = (FontFamily)app!.FindResource("DisplayFont"), FontWeight = FontWeights.Bold, FontSize = 21,
                Foreground = (Brush)app.FindResource("GoldBrush"), Margin = new Thickness(0, 0, 0, 10),
            });
            panel.Children.Add(new TextBlock
            {
                Text = message, Foreground = (Brush)app.FindResource("TextBrush"), FontSize = 14.5, TextWrapping = TextWrapping.Wrap,
                LineHeight = 21, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
            });

            var btns = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            if (confirm && cancelText != null)
            {
                var cancel = new Button { Content = cancelText, Style = (Style)app.FindResource("PillButton"), Height = 44, MinWidth = 112, Margin = new Thickness(0, 0, 10, 0) };
                cancel.Click += (s, e) => Close(false);
                btns.Children.Add(cancel);
            }
            var ok = new Button { Content = okText, Style = (Style)app.FindResource("GoldButton"), Height = 44, MinWidth = 130 };
            ok.Click += (s, e) => Close(true);
            btns.Children.Add(ok);
            panel.Children.Add(btns);

            var card = new Border
            {
                CornerRadius = new CornerRadius(16), Background = new SolidColorBrush(Color.FromRgb(0x12, 0x14, 0x1C)),
                BorderBrush = (Brush)app.FindResource("GoldBrush"), BorderThickness = new Thickness(1.5),
                Padding = new Thickness(28, 24, 28, 22), Margin = new Thickness(26), Child = panel,   // margin leaves room for the shadow
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7 },
            };
            win.Content = card;

            win.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Enter) Close(true);
                else if (e.Key == Key.Escape) Close(confirm ? false : true);
            };
            win.Loaded += (s, e) => ok.Focus();
            win.ShowDialog();
            return result;
        }
    }
}
