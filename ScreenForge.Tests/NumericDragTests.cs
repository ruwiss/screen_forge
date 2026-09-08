using ScreenForge.Windows;

namespace ScreenForge.Tests;

public sealed class NumericDragTests
{
    [Fact]
    public void Compute_DragRight_IncreasesByPixelsPerUnit()
    {
        Assert.Equal(60, NumericDrag.Compute(50, 30, 3, 0, 100, shift: false, integer: true));
    }

    [Fact]
    public void Compute_LargeNegative_ClampsToMin()
    {
        Assert.Equal(0, NumericDrag.Compute(50, -1000, 3, 0, 100, shift: false, integer: true));
    }

    [Fact]
    public void Compute_Shift_MultipliesThenClamps()
    {
        Assert.Equal(100, NumericDrag.Compute(50, 15, 3, 0, 100, shift: true, integer: true));
    }

    [Fact]
    public void Compute_Integer_Rounds()
    {
        // 10 + 5/2 = 12.5 → Math.Round (ToEven) = 12
        Assert.Equal(12, NumericDrag.Compute(10, 5, 2, 1, 4096, shift: false, integer: true));
    }
}
