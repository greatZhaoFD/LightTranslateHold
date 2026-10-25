using System;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace LightTranslateHold
{
    internal static class ConsoleTest
    {
        [STAThread]
        public static int Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppPaths.Initialize(args);
            Log.Init();
            Console.WriteLine("self-test starting, data dir=" + AppPaths.DataDir);
            int code = SelfTest.Run();
            try
            {
                string report = Path.Combine(AppPaths.DataDir, "self-test.txt");
                if (File.Exists(report)) Console.WriteLine(File.ReadAllText(report, Encoding.UTF8));
            }
            catch { }
            Console.WriteLine("exit code=" + code);
            return code;
        }
    }
}
