param(
    [string]$Out = "C:\Git_WorkSpace\ExcelGitDiffViewer\samples\_shot.png",
    [int]$ClickX = -1,
    [int]$ClickY = -1
)

Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Drawing;
using System.Drawing.Imaging;
public class Cap {
  [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr h, IntPtr hdc, uint flags);
  [DllImport("user32.dll")] public static extern bool ShowWindow(IntPtr h, int n);
  [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int w, int ht, uint flags);
  [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr h);
  [DllImport("user32.dll")] public static extern bool SetCursorPos(int x, int y);
  [DllImport("user32.dll")] public static extern void mouse_event(uint flags, uint dx, uint dy, uint data, IntPtr extra);
  [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
  [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr h, out RECT r);

  public static IntPtr TopMost = new IntPtr(-1);
  public static IntPtr NoTopMost = new IntPtr(-2);

  public static void Front(IntPtr h) {
    ShowWindow(h, 9);
    SetWindowPos(h, TopMost, 0, 0, 1180, 760, 0x0040);
    SetForegroundWindow(h);
    System.Threading.Thread.Sleep(500);
  }

  public static void Click(int x, int y) {
    SetCursorPos(x, y);
    System.Threading.Thread.Sleep(120);
    mouse_event(0x0002, 0, 0, 0, IntPtr.Zero); // LEFTDOWN
    mouse_event(0x0004, 0, 0, 0, IntPtr.Zero); // LEFTUP
    System.Threading.Thread.Sleep(300);
  }

  public static void Shot(IntPtr h, string path) {
    RECT r; GetWindowRect(h, out r);
    int w = r.Right-r.Left, ht = r.Bottom-r.Top;
    using (var bmp = new Bitmap(w, ht))
    using (var g = Graphics.FromImage(bmp)) {
      IntPtr hdc = g.GetHdc();
      PrintWindow(h, hdc, 0x2);
      g.ReleaseHdc(hdc);
      bmp.Save(path, ImageFormat.Png);
    }
  }
}
'@

$p = Get-Process ExcelGitDiffViewer -ErrorAction SilentlyContinue | Select-Object -First 1
if (-not $p) { Write-Output "process not found"; exit 1 }
$h = $p.MainWindowHandle
[Cap]::Front($h)
if ($ClickX -ge 0 -and $ClickY -ge 0) {
    [Cap]::Click($ClickX, $ClickY)
}
[Cap]::Shot($h, $Out)
[Cap]::SetWindowPos($h, [Cap]::NoTopMost, 0, 0, 1180, 760, 0x0040) | Out-Null
Write-Output "saved $Out"
