using System;
using System.IO;
using System.Security;
using System.Windows.Forms;
using Microsoft.Win32;

namespace SdrBrightnessTray
{
    internal static class Autostart
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string SettingsKeyPath = @"Software\SdrBrightnessTray";
        private const string RunValueName = "SdrBrightnessTray";
        private const string AutoStartValueName = "AutoStart";

        public static void Initialize()
        {
            try
            {
                bool enabled;
                using (RegistryKey settings = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                {
                    object value = settings == null ? null : settings.GetValue(AutoStartValueName);
                    if (value == null)
                    {
                        enabled = true;
                        if (settings != null) settings.SetValue(AutoStartValueName, 1, RegistryValueKind.DWord);
                    }
                    else
                    {
                        enabled = Convert.ToInt32(value) != 0;
                    }
                }
                if (enabled) EnsureRunEntry();
            }
            catch
            {
                // Autostart is a convenience; never prevent the tray app from starting.
            }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    return run != null && run.GetValue(RunValueName) != null;
                }
            }
            catch
            {
                return false;
            }
        }

        public static bool SetEnabled(bool enabled, out string error)
        {
            error = null;
            try
            {
                using (RegistryKey settings = Registry.CurrentUser.CreateSubKey(SettingsKeyPath))
                {
                    if (settings != null)
                    {
                        settings.SetValue(AutoStartValueName, enabled ? 1 : 0, RegistryValueKind.DWord);
                    }
                }

                using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (run == null)
                    {
                        error = "无法打开 Windows 启动项注册表";
                        return false;
                    }
                    if (enabled)
                    {
                        run.SetValue(RunValueName, BuildRunCommand(), RegistryValueKind.String);
                    }
                    else
                    {
                        run.DeleteValue(RunValueName, false);
                    }
                }
                return true;
            }
            catch (SecurityException)
            {
                error = "没有权限修改开机启动项";
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                error = "没有权限修改开机启动项";
                return false;
            }
            catch (IOException ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void EnsureRunEntry()
        {
            try
            {
                using (RegistryKey run = Registry.CurrentUser.CreateSubKey(RunKeyPath))
                {
                    if (run == null) return;
                    object current = run.GetValue(RunValueName);
                    string expected = BuildRunCommand();
                    if (!string.Equals(current as string, expected, StringComparison.OrdinalIgnoreCase))
                    {
                        run.SetValue(RunValueName, expected, RegistryValueKind.String);
                    }
                }
            }
            catch
            {
                // Ignore: the app can still be started manually.
            }
        }

        private static string BuildRunCommand()
        {
            return "\"" + Application.ExecutablePath + "\" --autostart";
        }
    }
}
