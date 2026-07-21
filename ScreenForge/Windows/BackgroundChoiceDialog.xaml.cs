using System.Windows;

namespace ScreenForge.Windows;

public enum BackgroundChoice
{
    Opaque,
    Transparent,
    CropOpaque,
    CropTransparent,
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
        BtnOpaqueCrop.Click += (_, _) => { Result = BackgroundChoice.CropOpaque; Close(); };
        BtnTransparentCrop.Click += (_, _) => { Result = BackgroundChoice.CropTransparent; Close(); };
        MouseLeftButtonDown += (_, _) => DragMove();
    }
}
