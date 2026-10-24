using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;

namespace SdrBrightnessTray
{
    internal static class Program
    {
        private const string ShowEventName = @"Local\SdrBrightnessTray.Show";

        [STAThread]
        private static void Main(string[] args)
        {
            EnablePerMonitorDpi();
            try
            {
                Run(args);
            }
            catch (Exception ex)
            {
                try
                {
                    MessageBox.Show("程序启动失败：\r\n" + ex.Message, "SDR 内容亮度",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                catch
                {
                    // Nothing else we can do without a desktop.
                }
            }
        }

        private static void Run(string[] args)
        {
            bool createdNew;
            using (EventWaitHandle showEvent = new EventWaitHandle(false, EventResetMode.AutoReset,
                ShowEventName, out createdNew))
            {
                if (!createdNew)
                {
                    // Another copy is already in the tray; ask it to show its popup.
                    try { showEvent.Set(); }
                    catch { }
                    return;
                }

                bool install = Array.IndexOf(args, "--install") >= 0;
                bool skipAutostart = Array.IndexOf(args, "--no-autostart") >= 0;
                if (install)
                {
                    string error;
                    Autostart.SetEnabled(true, out error);
                }
                else if (!skipAutostart)
                {
                    Autostart.Initialize();
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.ThreadException += delegate(object sender, ThreadExceptionEventArgs e)
                {
                    MessageBox.Show("程序遇到错误：\r\n" + e.Exception.Message, "SDR 内容亮度",
                        MessageBoxButtons.OK, MessageBoxIcon.Error);
                };

                using (TrayContext context = new TrayContext(showEvent))
                {
                    Application.Run(context);
                }
            }
        }

        private static void EnablePerMonitorDpi()
        {
            try
            {
                SetProcessDpiAwarenessContext(new IntPtr(-4)); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
            }
            catch
            {
                // Older Windows versions fall back to the manifest setting.
            }
        }

        [DllImport("user32.dll")]
        private static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
    }
}
