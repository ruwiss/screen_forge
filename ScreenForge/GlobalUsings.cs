// WinForms (Screen, çoklu monitör) + WPF birlikte kullanıldığında çakışan
// tip adlarını WPF lehine sabitler.
global using Application = System.Windows.Application;
global using MessageBox = System.Windows.MessageBox;
global using Clipboard = System.Windows.Clipboard;
global using Point = System.Windows.Point;
global using Size = System.Windows.Size;
global using Rectangle = System.Drawing.Rectangle;
global using MouseEventArgs = System.Windows.Input.MouseEventArgs;
global using KeyEventArgs = System.Windows.Input.KeyEventArgs;
global using MouseButtonEventArgs = System.Windows.Input.MouseButtonEventArgs;
global using DragEventArgs = System.Windows.DragEventArgs;
global using Cursors = System.Windows.Input.Cursors;
// WPF görsel tipleri (WinForms eşadlılarına karşı sabitlenir)
global using Brush = System.Windows.Media.Brush;
global using Brushes = System.Windows.Media.Brushes;
global using Color = System.Windows.Media.Color;
global using Orientation = System.Windows.Controls.Orientation;
global using CheckBox = System.Windows.Controls.CheckBox;
global using ComboBox = System.Windows.Controls.ComboBox;
global using TextBox = System.Windows.Controls.TextBox;
global using Button = System.Windows.Controls.Button;
global using Label = System.Windows.Controls.Label;
global using HorizontalAlignment = System.Windows.HorizontalAlignment;
global using VerticalAlignment = System.Windows.VerticalAlignment;
global using ModifierKeys = System.Windows.Input.ModifierKeys;
