using MaaFramework.Binding;
public static class T {
  public static void M() {
    var x = new DesktopWindowInfo(
      nint.Zero,
      "",
      "",
      (Win32ScreencapMethod)2,
      Win32InputMethod.Seize,
      Win32InputMethod.Seize);
    var c = new MaaWin32Controller(x, LinkOption.Start, CheckStatusOption.None);
    c.Scroll(0, -120);
  }
}
