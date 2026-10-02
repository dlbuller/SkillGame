using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace SkillGameWpf
{
    /// <summary>The operator-facing update flow: check, show what changed, download with a progress bar, then install + reboot.</summary>
    public sealed class UpdateDialog
    {
        private readonly Window _win;
        private readonly Border _card;
        private static Brush B(string key) => (Brush)Application.Current.FindResource(key);
        private static FontFamily F(string key) => (FontFamily)Application.Current.FindResource(key);

        private UpdateDialog()
        {
            _card = new Border
            {
                CornerRadius = new CornerRadius(16),
                Background = new SolidColorBrush(Color.FromRgb(0x1A, 0x12, 0x0B)),
                BorderBrush = B("GoldBrush"), BorderThickness = new Thickness(1.5),
                Padding = new Thickness(30, 26, 30, 24), Margin = new Thickness(26),
                Effect = new DropShadowEffect { Color = Colors.Black, BlurRadius = 40, ShadowDepth = 0, Opacity = 0.7 },
                Width = 480,
            };
            _win = new Window
            {
                WindowStyle = WindowStyle.None, AllowsTransparency = true, Background = Brushes.Transparent,
                SizeToContent = SizeToContent.WidthAndHeight, ResizeMode = ResizeMode.NoResize, ShowInTaskbar = false,
                Owner = Application.Current?.MainWindow,
                WindowStartupLocation = Application.Current?.MainWindow != null ? WindowStartupLocation.CenterOwner : WindowStartupLocation.CenterScreen,
                Content = _card,
            };
        }

        public static void Show()
        {
            var d = new UpdateDialog();
            d._win.Loaded += async (s, e) => await d.RunAsync();
            d._win.ShowDialog();
        }

        private async Task RunAsync()
        {
            ShowMessage("CHECKING FOR UPDATES", $"Contacting the WezeBull server…\nYou're on v{Updater.Current.ToString(3)}.", busy: true);
            UpdateInfo? info;
            try
            {
                info = await Updater.CheckAsync();
            }
            catch (Exception ex)
            {
                ShowMessage("COULDN'T CHECK", "Couldn't reach the update server. Check the network and try again.\n\n" + ex.Message, okText: "OK");
                return;
            }

            if (info == null)
            {
                ShowMessage("UP TO DATE", $"SkillGame v{Updater.Current.ToString(3)} is the latest version. Nothing to install.", okText: "OK");
                return;
            }
            ShowAvailable(info);
        }

        // ---- states ---------------------------------------------------------

        private void ShowMessage(string title, string body, bool busy = false, string? okText = null)
        {
            var panel = new StackPanel();
            panel.Children.Add(Title(title));
            panel.Children.Add(Body(body));
            if (busy) panel.Children.Add(Indeterminate());
            if (okText != null)
            {
                var ok = Pill(okText, gold: true);
                ok.Click += (s, e) => _win.Close();
                panel.Children.Add(Buttons(ok));
            }
            _card.Child = panel;
        }

        private void ShowAvailable(UpdateInfo info)
        {
            var panel = new StackPanel();
            panel.Children.Add(Title("UPDATE AVAILABLE"));
            panel.Children.Add(new TextBlock
            {
                Text = $"v{Updater.Current.ToString(3)}  →  v{info.Version}" + (string.IsNullOrWhiteSpace(info.Date) ? "" : $"   ·   {info.Date}"),
                Foreground = B("GoldBrush"), FontFamily = F("DisplayFont"), FontSize = 16, Margin = new Thickness(0, 0, 0, 12),
            });
            panel.Children.Add(new TextBlock { Text = "WHAT'S NEW", Foreground = B("MutedBrush"), FontSize = 11.5, FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 6) });
            panel.Children.Add(new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(0x18, 0xFF, 0xFF, 0xFF)),
                CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 11, 14, 11), MaxHeight = 240,
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    Content = new TextBlock { Text = info.Notes.Trim(), Foreground = B("TextBrush"), FontSize = 13.5, TextWrapping = TextWrapping.Wrap, LineHeight = 20, LineStackingStrategy = LineStackingStrategy.BlockLineHeight },
                },
            });

            var later = Pill("LATER", gold: false);
            later.Click += (s, e) => _win.Close();
            var update = Pill("UPDATE NOW", gold: true);
            update.Click += async (s, e) => await DownloadAndApply(info);
            panel.Children.Add(Buttons(later, update));
            _card.Child = panel;
        }

        private async Task DownloadAndApply(UpdateInfo info)
        {
            var panel = new StackPanel();
            panel.Children.Add(Title("DOWNLOADING UPDATE"));
            var status = Body($"Getting SkillGame v{info.Version}…");
            panel.Children.Add(status);

            var track = new Border { Height = 14, CornerRadius = new CornerRadius(7), Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 6, 0, 0) };
            var fill = new Border { Height = 14, CornerRadius = new CornerRadius(7), Background = B("GoldBrush"), HorizontalAlignment = HorizontalAlignment.Left, Width = 0 };
            track.Child = fill;
            panel.Children.Add(track);
            var pct = new TextBlock { Text = "0%", Foreground = B("GoldBrush"), FontFamily = F("DisplayFont"), FontSize = 15, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 8, 0, 0) };
            panel.Children.Add(pct);
            _card.Child = panel;

            var progress = new Progress<double>(p =>
            {
                double w = Math.Max(0, Math.Min(1, p)) * (track.ActualWidth > 0 ? track.ActualWidth : _card.Width - 60);
                fill.Width = w;
                pct.Text = (int)(p * 100) + "%";
            });

            try
            {
                string zip = await Updater.DownloadAsync(info, progress);
                // Installing phase — files get swapped by the external step, then reboot. Keep it reassuring.
                var done = new StackPanel();
                done.Children.Add(Title("INSTALLING"));
                done.Children.Add(Body(Updater.RebootAfterUpdate
                    ? $"Installing v{info.Version} and restarting the machine…\nDon't power off."
                    : $"Installing v{info.Version} and relaunching…"));
                done.Children.Add(Indeterminate());
                _card.Child = done;
                await Task.Delay(900);           // let the message paint before we hand off and shut down
                Updater.ApplyAndRestart(zip, info);
            }
            catch (Exception ex)
            {
                ShowMessage("UPDATE FAILED", "The update didn't install — nothing was changed.\n\n" + ex.Message, okText: "OK");
            }
        }

        // ---- themed bits ----------------------------------------------------

        private TextBlock Title(string t) => new()
        {
            Text = t, FontFamily = F("DisplayFont"), FontWeight = FontWeights.Bold, FontSize = 21,
            Foreground = B("GoldBrush"), Margin = new Thickness(0, 0, 0, 12),
        };
        private TextBlock Body(string t) => new()
        {
            Text = t, Foreground = B("TextBrush"), FontSize = 14, TextWrapping = TextWrapping.Wrap,
            LineHeight = 21, LineStackingStrategy = LineStackingStrategy.BlockLineHeight,
        };

        private Button Pill(string text, bool gold)
        {
            var b = new Button
            {
                Content = new TextBlock { Text = text, Margin = new Thickness(14, 0, 14, 0) },
                Style = (Style)Application.Current.FindResource(gold ? "GoldButton" : "PillButton"),
                Height = 46, MinWidth = 130,
            };
            return b;
        }
        private StackPanel Buttons(params UIElement[] items)
        {
            var sp = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 24, 0, 0) };
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i] is FrameworkElement fe && i < items.Length - 1) fe.Margin = new Thickness(0, 0, 10, 0);
                sp.Children.Add(items[i]);
            }
            return sp;
        }

        // A soft brass pulse for the indeterminate (checking / installing) phases.
        private Border Indeterminate()
        {
            var track = new Border { Height = 6, CornerRadius = new CornerRadius(3), Background = new SolidColorBrush(Color.FromArgb(0x22, 0xFF, 0xFF, 0xFF)), Margin = new Thickness(0, 18, 0, 2), ClipToBounds = true };
            var dot = new Border { Height = 6, Width = 120, CornerRadius = new CornerRadius(3), Background = B("GoldBrush"), HorizontalAlignment = HorizontalAlignment.Left };
            var tt = new TranslateTransform(-120, 0);
            dot.RenderTransform = tt;
            track.Child = dot;
            track.Loaded += (s, e) =>
            {
                double w = track.ActualWidth > 0 ? track.ActualWidth : 420;
                var anim = new DoubleAnimation(-120, w, TimeSpan.FromSeconds(1.1)) { RepeatBehavior = RepeatBehavior.Forever };
                tt.BeginAnimation(TranslateTransform.XProperty, anim);
            };
            return track;
        }
    }
}
