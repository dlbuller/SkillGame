using System;
using System.Windows.Forms;

namespace SkillGame
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            ApplicationConfiguration.Initialize();
            Log.Info("SkillGame starting.");
            Application.Run(new SkillGameForm());
        }
    }
}
