using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Windows.Forms;

namespace LightTranslateHold
{
    internal static class DemoMode
    {
        public static int Run(string[] args)
        {
            string popupPath = FindPath(args, "--demo-popup");
            string settingsPath = FindPath(args, "--demo-settings");
            try
            {
                if (!String.IsNullOrEmpty(settingsPath))
                {
                    Settings s = new Settings();
                    s.ApiKey = "demo-placeholder";
                    using (SettingsForm form = new SettingsForm(s))
                    {
                        form.Show();
                        Application.DoEvents();
                        form.Refresh();
                        Application.DoEvents();
                        using (Bitmap bitmap = new Bitmap(form.Width, form.Height))
                        {
                            form.DrawToBitmap(bitmap, new Rectangle(0, 0, form.Width, form.Height));
                            Save(bitmap, settingsPath);
                        }
                    }
                    return 0;
                }

                string path = String.IsNullOrEmpty(popupPath)
                    ? Path.Combine(AppPaths.DataDir, "popup-demo.png")
                    : popupPath;
                Settings settings = new Settings();
                settings.Theme = "light";
                using (PopupForm popup = new PopupForm(settings))
                {
                    popup.BeginLoading(new Rectangle(200, 200, 1, 18), "English → 中文", true);
                    popup.Append("轻译是一个轻量的 Windows 划词翻译工具。选中文字后按住快捷键，译文会出现在选中区域下方。");
                    popup.Complete("完成 · 1.2 秒");
                    Application.DoEvents();
                    popup.Refresh();
                    Application.DoEvents();
                    using (Bitmap bitmap = new Bitmap(popup.Width, popup.Height))
                    {
                        popup.DrawToBitmap(bitmap, new Rectangle(0, 0, popup.Width, popup.Height));
                        Save(bitmap, path);
                    }
                }
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex);
                return 1;
            }
        }

        private static string FindPath(string[] args, string option)
        {
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == option) return args[i + 1];
            }
            return "";
        }

        private static void Save(Bitmap bitmap, string path)
        {
            string dir = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!String.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            bitmap.Save(path, ImageFormat.Png);
            Console.WriteLine("saved " + path);
        }
    }
}
