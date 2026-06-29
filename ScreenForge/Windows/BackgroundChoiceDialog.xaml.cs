using System.Windows;

namespace ScreenForge.Windows;

public partial class BackgroundChoiceDialog : Window
{
    public bool? Result { get; private set; }

    public BackgroundChoiceDialog()
    {
        InitializeComponent();
        BtnClose.Click += (_, _) => Close();
        BtnOpaque.Click += (_, _) => { Result = false; Close(); };
        BtnTransparent.Click += (_, _) => { Result = true; Close(); };
        MouseLeftButtonDown += (_, _) => DragMove();
    }
}
