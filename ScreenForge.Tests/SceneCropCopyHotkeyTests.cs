using System.Windows.Input;
using ScreenForge.Windows;

namespace ScreenForge.Tests;

public sealed class SceneCropCopyHotkeyTests
{
    [Fact]
    public void PendingCopy_CtrlC_WhileCropping_Commits()
    {
        Assert.True(CaptureOverlayWindow.ShouldCommitSceneCropOnCopyHotkey(
            isSceneCropping: true,
            pendingCopy: true,
            key: Key.C,
            modifiers: ModifierKeys.Control));
    }

    [Fact]
    public void PendingSaveOrUpload_CtrlC_DoesNotCommit()
    {
        Assert.False(CaptureOverlayWindow.ShouldCommitSceneCropOnCopyHotkey(
            isSceneCropping: true,
            pendingCopy: false,
            key: Key.C,
            modifiers: ModifierKeys.Control));
    }

    [Fact]
    public void PendingCopy_CWithoutCtrl_DoesNotCommit()
    {
        Assert.False(CaptureOverlayWindow.ShouldCommitSceneCropOnCopyHotkey(
            isSceneCropping: true,
            pendingCopy: true,
            key: Key.C,
            modifiers: ModifierKeys.None));
    }

    [Fact]
    public void NotCropping_CtrlC_DoesNotCommit()
    {
        Assert.False(CaptureOverlayWindow.ShouldCommitSceneCropOnCopyHotkey(
            isSceneCropping: false,
            pendingCopy: true,
            key: Key.C,
            modifiers: ModifierKeys.Control));
    }
}
