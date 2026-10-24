using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace SdrBrightnessTray
{
    internal static class Theme
    {
        public static readonly Color Surface = Color.FromArgb(32, 32, 32);
        public static readonly Color SurfaceTop = Color.FromArgb(38, 38, 38);
        public static readonly Color SurfaceBottom = Color.FromArgb(27, 27, 27);
        public static readonly Color Border = Color.FromArgb(58, 58, 58);
        public static readonly Color TextPrimary = Color.FromArgb(255, 255, 255);
        public static readonly Color TextSecondary = Color.FromArgb(176, 176, 176);
        public static readonly Color TextMuted = Color.FromArgb(122, 122, 122);
        public static readonly Color TextDisabled = Color.FromArgb(111, 111, 111);
        public static readonly Color Accent = Color.FromArgb(76, 194, 255);
        public static readonly Color Track = Color.FromArgb(62, 62, 62);
        public static readonly Color TrackDisabled = Color.FromArgb(45, 45, 45);
        public static readonly Color ThumbDisabled = Color.FromArgb(105, 105, 105);
        public static readonly Color Error = Color.FromArgb(255, 180, 171);

        private static readonly HashSet<string> InstalledFonts = GetInstalledFonts();

        public static Font CreateFont(string[] families, float size, FontStyle style)
        {
            for (int i = 0; i < families.Length; i++)
            {
                if (!InstalledFonts.Contains(families[i])) continue;
                try
                {
                    return new Font(families[i], size, style, GraphicsUnit.Point);
                }
                catch
                {
                    // Try the next family.
                }
            }
            return new Font(SystemFonts.MessageBoxFont.FontFamily, size, style, GraphicsUnit.Point);
        }

        public static GraphicsPath RoundedRect(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            if (radius <= 0.1f)
            {
                path.AddRectangle(bounds);
                return path;
            }
            float diameter = Math.Min(radius * 2f, Math.Min(bounds.Width, bounds.Height));
            RectangleF arc = new RectangleF(bounds.X, bounds.Y, diameter, diameter);
            path.AddArc(arc, 180, 90);
            arc.X = bounds.Right - diameter;
            path.AddArc(arc, 270, 90);
            arc.Y = bounds.Bottom - diameter;
            path.AddArc(arc, 0, 90);
            arc.X = bounds.Left;
            path.AddArc(arc, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyWindowStyle(IntPtr handle)
        {
            try
            {
                int enabled = 1;
                DwmSetWindowAttribute(handle, 20, ref enabled, sizeof(int));
                int corner = 2; // DWMWCP_ROUND
                DwmSetWindowAttribute(handle, 33, ref corner, sizeof(int));
                int border = 0x00202020; // COLORREF: BGR
                DwmSetWindowAttribute(handle, 34, ref border, sizeof(int));
            }
            catch
            {
                // DWM attributes are best-effort; the custom paint still looks fine.
            }
        }

        private static HashSet<string> GetInstalledFonts()
        {
            HashSet<string> result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                FontFamily[] families = FontFamily.Families;
                for (int i = 0; i < families.Length; i++)
                {
                    result.Add(families[i].Name);
                }
            }
            catch
            {
                // Fall through to the message-box font.
            }
            return result;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);
    }
}
