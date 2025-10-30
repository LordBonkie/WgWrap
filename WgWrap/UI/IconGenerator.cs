using System.Drawing.Drawing2D;
using System.Drawing.Text;

namespace WgWrap.UI;

/// <summary>
/// Utility class for generating colored WgWrap icons with "W" text overlay
/// </summary>
internal static class IconGenerator
{
    /// <summary>
    /// Determines the appropriate icon color based on VPN status and network conditions
    /// </summary>
    /// <param name="vpnStatus">Current VPN service status</param>
    /// <param name="isManuallyDisabled">Whether auto-start is manually disabled</param>
    /// <param name="isOnTrustedNetwork">Whether currently on a trusted network</param>
    /// <returns>The color to use for the icon</returns>
    public static Color GetIconColor(string vpnStatus, bool isManuallyDisabled, bool isOnTrustedNetwork)
    {
        // Priority order (highest to lowest):
        // 1. No service installed (Red)
        // 2. Manually disabled (Orange)
        // 3. Trusted network (Blue)
        // 4. Connected (Green)
        // 5. Other states (Yellow)
        
        if (vpnStatus == "Not Installed")
        {
            // Red: No service installed
            return Color.FromArgb(220, 50, 50);
        }
        else if (isManuallyDisabled)
        {
            // Yellow/Orange: Manually disabled
            return Color.FromArgb(255, 165, 0);
        }
        else if (isOnTrustedNetwork)
        {
            // Blue: Trusted network
            return Color.FromArgb(70, 130, 220);
        }
        else if (vpnStatus == "Connected")
        {
            // Green: VPN online
            return Color.FromArgb(50, 200, 50);
        }
        else
        {
            // Default yellow for other states (disconnected, starting, etc.)
            return Color.FromArgb(255, 200, 0);
        }
    }
    
    /// <summary>
    /// Creates a circular colored icon with "W" text overlay
    /// </summary>
    /// <param name="color">The background color of the icon</param>
    /// <param name="size">The size of the icon in pixels (default: 16)</param>
    /// <returns>A new Icon instance</returns>
    public static Icon CreateColoredIcon(Color color, int size = 16)
    {
        using var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        
        // Use highest quality rendering settings for sharper icons
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        
        // Draw filled circle (use full size for sharper edges)
        using (var brush = new SolidBrush(color))
        {
            graphics.FillEllipse(brush, 0.5f, 0.5f, size - 1f, size - 1f);
        }
        
        // Draw border for better visibility
        using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), 0.8f))
        {
            graphics.DrawEllipse(pen, 0.5f, 0.5f, size - 1f, size - 1f);
        }
        
        // Draw "W" text overlay - large and centered
        float fontSize = size * 0.75f; // Large letter to fill the circle (12pt for 16px)
        using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var textBrush = new SolidBrush(Color.White))
        {
            // Use StringFormat for better centering
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            
            // Calculate center point
            float centerX = size / 2f;
            float centerY = size / 2f;
            
            // Draw black border around text (1.5px) for visibility on all backgrounds
            using (var borderPen = new Pen(Color.Black, 1.5f))
            {
                var path = new GraphicsPath();
                var rect = new RectangleF(0, 0, size, size);
                path.AddString("W", font.FontFamily, (int)font.Style, fontSize, rect, format);
                graphics.DrawPath(borderPen, path);
            }
            
            // Draw White text
            graphics.DrawString("W", font, textBrush, centerX, centerY, format);
        }
        
        // Convert bitmap to icon
        System.IntPtr hIcon = bitmap.GetHicon();
        var icon = Icon.FromHandle(hIcon);
        
        return (Icon)icon.Clone();
    }
    
    /// <summary>
    /// Creates a bitmap image with the W branding (for use in forms, not system tray)
    /// </summary>
    /// <param name="color">The background color</param>
    /// <param name="size">The size in pixels</param>
    /// <returns>A new Bitmap instance</returns>
    public static Bitmap CreateColoredBitmap(Color color, int size = 32)
    {
        var bitmap = new Bitmap(size, size, System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        using var graphics = Graphics.FromImage(bitmap);
        
        // Use highest quality rendering settings for sharper icons
        graphics.SmoothingMode = SmoothingMode.HighQuality;
        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        graphics.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        graphics.CompositingQuality = CompositingQuality.HighQuality;
        
        // Draw filled circle (use full size for sharper edges)
        using (var brush = new SolidBrush(color))
        {
            graphics.FillEllipse(brush, 0.5f, 0.5f, size - 1f, size - 1f);
        }
        
        // Draw border for better visibility
        float borderWidth = size / 20f; // Thinner border (was /10.67f)
        using (var pen = new Pen(Color.FromArgb(200, 255, 255, 255), borderWidth))
        {
            graphics.DrawEllipse(pen, 0.5f, 0.5f, size - 1f, size - 1f);
        }
        
        // Draw "W" text overlay - large and centered
        float fontSize = size * 0.75f; // Large letter to fill the circle
        using (var font = new Font("Segoe UI", fontSize, FontStyle.Bold, GraphicsUnit.Pixel))
        using (var textBrush = new SolidBrush(Color.White))
        {
            // Use StringFormat for better centering
            var format = new StringFormat
            {
                Alignment = StringAlignment.Center,
                LineAlignment = StringAlignment.Center
            };
            
            // Calculate center point
            float centerX = size / 2f;
            float centerY = size / 2f;
            
            // Draw black border around text (1.5px) for visibility on all backgrounds
            using (var borderPen = new Pen(Color.Black, 1.5f))
            {
                var path = new GraphicsPath();
                var rect = new RectangleF(0, 0, size, size);
                path.AddString("W", font.FontFamily, (int)font.Style, fontSize, rect, format);
                graphics.DrawPath(borderPen, path);
            }
            
            // Draw white text
            graphics.DrawString("W", font, textBrush, centerX, centerY, format);
        }
        
        return bitmap;
    }
}

