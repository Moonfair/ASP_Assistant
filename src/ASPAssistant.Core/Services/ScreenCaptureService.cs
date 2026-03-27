namespace ASPAssistant.Core.Services;

public class ScreenCaptureService
{
    private IntPtr _gameWindowHandle;

    public void SetTargetWindow(IntPtr handle)
    {
        _gameWindowHandle = handle;
    }

    public byte[]? CaptureScreen()
    {
        if (_gameWindowHandle == IntPtr.Zero)
            return null;

        // MaaFramework integration point:
        // var controller = MaaController.CreateWin32(handle, screencapMethod);
        // controller.Screencap();
        // return controller.GetImage();
        return null;
    }
}
