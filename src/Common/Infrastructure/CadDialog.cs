using System;
using System.ComponentModel;
using System.Windows;
#if AUTOCAD
#if ACAD_CORE
using CadApp = Autodesk.AutoCAD.ApplicationServices.Core.Application;
#else
using CadApp = Autodesk.AutoCAD.ApplicationServices.Application;
#endif
#else
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;
#endif

namespace ZwcadBatchPlot;

/// <summary>
/// CAD 窗口显示入口，对齐 main 分支 WinForms 的
/// <c>CadApp.ShowModalDialog</c> / <c>CadApp.ShowModelessDialog</c> / <c>form.ShowDialog(owner)</c>。
/// 顶层窗必须走 CAD API（WPF 为 ShowModalWindow / ShowModelessWindow），
/// 这样 Hide 去图面取点时模态循环不会结束；嵌套窗用 Owner + ShowDialog，对应原来的 ShowDialog(this)。
/// 顶层窗关闭时把焦点交还 CAD，避免落到其它进程；不改 CAD Enable，以免打断 Hide/Restore 取点链。
/// </summary>
internal static class CadDialog
{
    /// <summary>顶层模态窗，对应 <c>CadApp.ShowModalDialog(form)</c>。</summary>
    public static bool? ShowModal(Window window)
    {
        return ShowModal(window, owner: null);
    }

    /// <summary>
    /// 有属主时对应 <c>form.ShowDialog(owner)</c>（插件窗里再弹子窗）；
    /// 无属主时对应 <c>CadApp.ShowModalDialog(form)</c>。
    /// </summary>
    public static bool? ShowModal(Window window, Window? owner)
    {
        if (owner != null)
        {
            // 子窗关闭后焦点应回到属主插件窗，不要抢 CAD 前台。
            window.Owner = owner;
            return window.ShowDialog();
        }

        AttachTopLevelFocusRestore(window);
#if ACAD_CORE
        return window.ShowDialog();
#else
        return CadApp.ShowModalWindow(window);
#endif
    }

    /// <summary>非模态面板，对应 <c>CadApp.ShowModelessDialog(form)</c>。</summary>
    public static void ShowModeless(Window window)
    {
        AttachTopLevelFocusRestore(window);
#if ACAD_CORE
        window.Show();
#else
        CadApp.ShowModelessWindow(window);
#endif
    }

    /// <summary>
    /// 顶层窗关闭前/后把 CAD 拉回前台。
    /// 关闭前先 Activate，避免模态解除瞬间焦点落到其它已启用进程。
    /// </summary>
    private static void AttachTopLevelFocusRestore(Window window)
    {
        void OnClosing(object? sender, CancelEventArgs e)
        {
            if (e.Cancel)
            {
                return;
            }

            CadWindowFocus.ActivateCadWindow();
        }

        void OnClosed(object? sender, EventArgs e)
        {
            window.Closing -= OnClosing;
            window.Closed -= OnClosed;
            CadWindowFocus.ActivateCadWindow();
        }

        window.Closing += OnClosing;
        window.Closed += OnClosed;
    }
}
