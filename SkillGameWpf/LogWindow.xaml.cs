using System.Windows;
using SkillGame;

namespace SkillGameWpf
{
    public partial class LogWindow : Window
    {
        public LogWindow()
        {
            InitializeComponent();
            PathText.Text = Log.FilePath;
            Refresh();
        }

        private void Refresh()
        {
            LogText.Text = Log.Tail(500);
            LogText.ScrollToEnd();
        }

        private void Refresh_Click(object sender, RoutedEventArgs e) => Refresh();
        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
