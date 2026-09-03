using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;

namespace ZwcadBatchPlot;

/// <summary>
/// CAD 内嵌窗口隐藏后，Windows 可能把前台焦点交给其他进程。
/// WinForms 录入窗与 WPF 其它面板共用同一套 Hide/Restore 语义（与 main 分支一致）。
/// </summary>
internal static class CadWindowFocus
{
    private const int SwRestore = 9;

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BringWindowToTop(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsIconic(IntPtr windowHandle);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

    public static void ActivateCadWindow()
    {
        try
        {
            using var process = Process.GetCurrentProcess();
            process.Refresh();
            var windowHandle = process.MainWindowHandle;
            if (windowHandle == IntPtr.Zero)
            {
                return;
            }

            if (IsIconic(windowHandle))
            {
                ShowWindowAsync(windowHandle, SwRestore);
            }

            BringWindowToTop(windowHandle);
            SetForegroundWindow(windowHandle);
        }
        catch
        {
            // Core Console 没有主窗口；焦点恢复失败也不能阻断图框编辑和框选。
        }
    }

    /// <summary>隐藏 WinForms 插件窗并立即把输入焦点交给 CAD。</summary>
    public static void HideForCadInput(Form form)
    {
        form.Hide();
        ActivateCadWindow();
        System.Windows.Forms.Application.DoEvents();
        ActivateCadWindow();
    }

    /// <summary>隐藏 WPF 插件窗并立即把输入焦点交给 CAD。</summary>
    public static void HideForCadInput(Window window)
    {
        window.Hide();
        ActivateCadWindow();
        System.Windows.Forms.Application.DoEvents();
        ActivateCadWindow();
    }

    /// <summary>CAD 取点结束后恢复 WinForms 窗体。</summary>
    public static void RestoreDialog(Form form)
    {
        ActivateCadWindow();
        form.Visible = true;
        form.BringToFront();
        form.Activate();
    }

    /// <summary>CAD 取点结束后恢复 WPF 窗体。</summary>
    public static void RestoreDialog(Window window)
    {
        ActivateCadWindow();
        window.Visibility = Visibility.Visible;
        window.BringToFrontHwnd();
        window.Activate();
    }

    private static void BringToFrontHwnd(this Window window)
    {
        try
        {
            var handle = new WindowInteropHelper(window).Handle;
            if (handle != IntPtr.Zero)
            {
                BringWindowToTop(handle);
            }
        }
        catch
        {
            // 句柄尚未创建时忽略。
        }
    }
}
