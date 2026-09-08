using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ScreenForge.Windows;

/// <summary>
/// Figma-style horizontal scrub on numeric TextBoxes. Typing is unchanged
/// while the box is keyboard-focused.
/// </summary>
public static class NumericDrag
{
    public static double Compute(
        double start, double dx, double pixelsPerUnit,
        double min, double max, bool shift, bool integer)
    {
        double units = dx / pixelsPerUnit;
        if (shift) units *= 10;
        double v = Math.Clamp(start + units, min, max);
        if (integer) v = Math.Round(v);
        return v;
    }

    public static void Attach(
        TextBox box,
        double min,
        double max,
        Action<double> onLive,
        double pixelsPerUnit,
        bool integer = true)
    {
        box.Cursor = Cursors.SizeWE;
        box.GotKeyboardFocus += (_, _) => box.Cursor = Cursors.IBeam;
        box.LostKeyboardFocus += (_, _) => box.Cursor = Cursors.SizeWE;

        bool capturing = false;
        bool scrubbed = false;
        double startX = 0;
        double startVal = min;

        box.PreviewMouseLeftButtonDown += (_, e) =>
        {
            if (box.IsKeyboardFocused) return;
            capturing = true;
            scrubbed = false;
            startX = e.GetPosition(box).X;
            string raw = box.Text.Trim().TrimEnd('%');
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out startVal))
                startVal = min;
            box.CaptureMouse();
            e.Handled = true;
        };

        box.PreviewMouseMove += (_, e) =>
        {
            if (!capturing || !box.IsMouseCaptured) return;
            double dx = e.GetPosition(box).X - startX;
            if (!scrubbed && Math.Abs(dx) < 3) return;
            scrubbed = true;
            bool shift = (Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift;
            double v = Compute(startVal, dx, pixelsPerUnit, min, max, shift, integer);
            box.Text = v.ToString(CultureInfo.InvariantCulture);
            onLive(v);
            e.Handled = true;
        };

        box.PreviewMouseLeftButtonUp += (_, e) =>
        {
            if (!capturing) return;
            if (box.IsMouseCaptured) box.ReleaseMouseCapture();
            e.Handled = true;
        };

        box.LostMouseCapture += (_, _) =>
        {
            if (!capturing) return;
            bool click = !scrubbed;
            capturing = false;
            if (click)
            {
                box.Focus();
                box.SelectAll();
            }
        };
    }
}
