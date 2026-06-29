namespace ScreenForge.Editor;

/// <summary>Editör araç çubuğundaki araçlar.</summary>
public enum EditorTool
{
    Select,
    Rectangle,
    Ellipse,
    Line,
    Arrow,
    Pen,
    Highlight,
    Text,
    Step,
    Blur,
    Crop,
}

/// <summary>Tuval yerleşim modu.</summary>
public enum LayoutMode
{
    /// <summary>Pencereye ortala + ölçekle (kolaj / EditorWindow).</summary>
    Fit,
    /// <summary>Bire bir, ekran-üstü in-place editör.</summary>
    OneToOne,
}
