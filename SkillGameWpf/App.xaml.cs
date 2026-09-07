using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SkillGameWpf
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // Keep a stray hardware/UI exception (e.g. an FT232H board yanked mid-read) from taking the whole app down.
            DispatcherUnhandledException += (s, ex) =>
            {
                SkillGame.Log.Error("UI-thread exception — kept alive", ex.Exception);
                ex.Handled = true;
            };
            AppDomain.CurrentDomain.UnhandledException += (s, ex) =>
                SkillGame.Log.Error("FATAL unhandled exception", ex.ExceptionObject as Exception);
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (s, ex) =>
            {
                SkillGame.Log.Error("unobserved task exception", ex.Exception);
                ex.SetObserved();
            };

            // Offscreen render mode for design verification: --render <path> [w] [h]
            if (e.Args.Length >= 1 && e.Args[0] == "--render")
            {
                RenderAndExit(e.Args);
                return;
            }

            new MainWindow().Show();
        }

        private string? _renderView;

        private void RenderAndExit(string[] args)
        {
            string path = args.Length > 1 ? args[1] : "out.png";
            int w = args.Length > 2 ? int.Parse(args[2]) : 1340;
            int h = args.Length > 3 ? int.Parse(args[3]) : 860;
            _renderView = args.Length > 4 ? args[4] : null;

            try { RenderCore(path, w, h); }
            catch (Exception ex) { File.WriteAllText(path + ".log", ex.ToString()); Shutdown(); }
        }

        private void RenderCore(string path, int w, int h)
        {
            var win = new MainWindow
            {
                Width = w,
                Height = h,
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -12000,
                Top = -12000,
                ShowInTaskbar = false,
                WindowStyle = WindowStyle.None,
                WindowState = WindowState.Normal,   // render at w×h instead of the Maximized default
            };
            win.Show();
            if (_renderView != null) win.ShowView(_renderView);

            // Let entrance/glow animations settle before capturing a steady-state frame.
            var settle = new DispatcherTimer(DispatcherPriority.Loaded) { Interval = TimeSpan.FromMilliseconds(750) };
            settle.Tick += (s, ev) =>
            {
                settle.Stop();
                try
                {
                    win.UpdateLayout();
                    int rw = (int)Math.Ceiling(win.ActualWidth);
                    int rh = (int)Math.Ceiling(win.ActualHeight);
                    var rtb = new RenderTargetBitmap(rw, rh, 96, 96, PixelFormats.Pbgra32);
                    rtb.Render(win);
                    var enc = new PngBitmapEncoder();
                    enc.Frames.Add(BitmapFrame.Create(rtb));
                    using var fs = File.Create(path);
                    enc.Save(fs);
                }
                catch (Exception ex) { File.WriteAllText(path + ".log", ex.ToString()); }
                finally { win.Close(); Shutdown(); }
            };
            settle.Start();
        }
    }
}
