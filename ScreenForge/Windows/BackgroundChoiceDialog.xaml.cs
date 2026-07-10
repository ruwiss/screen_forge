using System.Windows;

namespace ScreenForge.Windows;

public enum BackgroundChoice
{
    Opaque,
    Transparent,
    Crop,
}

public partial class BackgroundChoiceDialog : Window
{
    public BackgroundChoice? Result { get; private set; }

    public BackgroundChoiceDialog()
    {
        InitializeComponent();
        BtnClose.Click += (_, _) => Close();
        BtnOpaque.Click += (_, _) => { Result = BackgroundChoice.Opaque; Close(); };
        BtnTransparent.Click += (_, _) => { Result = BackgroundChoice.Transparent; Close(); };
        BtnCrop.Click += (_, _) => { Result = BackgroundChoice.Crop; Close(); };
        MouseLeftButtonDown += (_, _) => DragMove();
    }
}
