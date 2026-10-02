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
            System.Collections.Generic.List<UpdateInfo> versions;
            try { versions = await Updater.GetVersionsAsync(); }
            catch (Exception ex)
            {
                ShowMessage("COULDN'T CHECK", "Couldn't reach the update server. Check the network and try again.\n\n" + ex.Message, okText: "OK");
                return;
            }
            if (versions.Count == 0) { ShowMessage("NO VERSIONS", "The server didn't list any versions to install.", okText: "OK"); return; }
            ShowVersions(versions);
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

        // The full release list — install any version, newer (UPDATE) or older (ROLL BACK). Latest + installed are tagged.
        private void ShowVersions(System.Collections.Generic.List<UpdateInfo> versions)
        {
            string cur = Updater.Current.ToString(3);
            var panel = new StackPanel();
            panel.Children.Add(Title("SOFTWARE VERSIONS"));
            panel.Children.Add(Body($"You're on v{cur}. Install any version — newer to update, older to roll back."));
            var rows = new StackPanel { Margin = new Thickness(0, 12, 0, 0) };
            for (int i = 0; i < versions.Count; i++)
                rows.Children.Add(VersionRow(versions[i], latest: i == 0, installed: string.Equals(versions[i].Version, cur, StringComparison.OrdinalIgnoreCase)));
            panel.Children.Add(new ScrollViewer { VerticalScrollBarVisibility = ScrollBarVisibility.Auto, MaxHeight = 330, Content = rows });
            var close = Pill("CLOSE", gold: false);
            close.Click += (s, e) => _win.Close();
            panel.Children.Add(Buttons(close));
            _card.Child = panel;
        }

        private Border VersionRow(UpdateInfo v, bool latest, bool installed)
        {
            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var left = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            var head = new StackPanel { Orientation = Orientation.Horizontal };
            head.Children.Add(new TextBlock { Text = $"v{v.Version}", Foreground = B("GoldBrush"), FontFamily = F("DisplayFont"), FontSize = 16, VerticalAlignment = VerticalAlignment.Center });
            if (!string.IsNullOrWhiteSpace(v.Date))
                head.Children.Add(new TextBlock { Text = $"   {v.Date}", Foreground = B("MutedBrush"), FontSize = 11.5, VerticalAlignment = VerticalAlignment.Center });
            if (latest) head.Children.Add(Tag("LATEST", strong: true));
            if (installed) head.Children.Add(Tag("INSTALLED", strong: false));
            left.Children.Add(head);
            string note = (v.Notes ?? "").Trim();
            int nl = note.IndexOfAny(new[] { '\n', '\r' });
            if (nl > 0) note = note.Substring(0, nl);
            if (note.Length > 0)
                left.Children.Add(new TextBlock { Text = note, Foreground = B("MutedBrush"), FontSize = 12, TextTrimming = TextTrimming.CharacterEllipsis, Margin = new Thickness(0, 3, 0, 0), MaxWidth = 300 });
            Grid.SetColumn(left, 0); grid.Children.Add(left);

            if (installed)
            {
                var lbl = new TextBlock { Text = "CURRENT", Foreground = B("MutedBrush"), FontFamily = F("DisplayFont"), FontSize = 12, VerticalAlignment = VerticalAlignment.Center };
                Grid.SetColumn(lbl, 1); grid.Children.Add(lbl);
            }
            else
            {
                bool newer = Updater.Parse(v.Version) > Updater.Current;
                var btn = Pill(newer ? "UPDATE" : "ROLL BACK", gold: newer);
                btn.Height = 38; btn.MinWidth = 108;
                btn.Click += (s, e) => ConfirmInstall(v, newer);
                Grid.SetColumn(btn, 1); grid.Children.Add(btn);
            }

            return new Border
            {
                CornerRadius = new CornerRadius(8), Background = new SolidColorBrush(Color.FromArgb(0x14, 0xFF, 0xFF, 0xFF)),
                Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 0, 0, 8), Child = grid,
            };
        }

        private UIElement Tag(string text, bool strong)
        {
            return new Border
            {
                Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(6, 1, 6, 2), CornerRadius = new CornerRadius(4),
                VerticalAlignment = VerticalAlignment.Center,
                Background = strong ? B("GoldBrush") : new SolidColorBrush(Color.FromArgb(0x33, 0xFF, 0xFF, 0xFF)),
                Child = new TextBlock { Text = text, FontSize = 10, FontWeight = FontWeights.Bold, Foreground = strong ? new SolidColorBrush(Color.FromRgb(0x20, 0x18, 0x0A)) : B("TextBrush") },
            };
        }

        private void ConfirmInstall(UpdateInfo v, bool newer)
        {
            string verb = newer ? "Update to" : "Roll back to";
            if (AppDialog.Confirm(newer ? "UPDATE" : "ROLL BACK", $"{verb} v{v.Version}?\n\nSkillGame will download it and restart.", newer ? "UPDATE" : "ROLL BACK", "CANCEL"))
                _ = DownloadAndApply(v);
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
                done.Children.Add(Body($"Installing v{info.Version} and restarting SkillGame…\nThis only unzips and copies files — it won't reboot the PC."));
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
