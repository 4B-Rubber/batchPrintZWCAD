using System;
#if AUTOCAD
using Autodesk.AutoCAD.ApplicationServices;
#else
using ZwSoft.ZwCAD.ApplicationServices;
#endif

namespace ZwcadBatchPlot;

/**
 * @file PendingPlotPreview.cs
 * @description 非模态批打窗把预览请求交给内部命令执行时的暂存区。
 *
 * AutoCAD PlotEngine 交互预览必须在命令/文档上下文中启动；按钮事件处于应用上下文，
 * 直接 Preview 会出现「假启动」（画面出来但滚轮仍归主编辑器）。
 */
internal static class PendingPlotPreview
{
    private static readonly object Gate = new();
    private static Request? _pending;

    /**
     * @description 一次待执行的打印预览请求。
     */
    internal sealed class Request
    {
        public PlotJob Job { get; set; } = null!;
        public string DeviceName { get; set; } = "";
        public string StyleSheet { get; set; } = "";
        public Document Document { get; set; } = null!;
        public Action? OnFinally { get; set; }
        public Action<Exception>? OnError { get; set; }
    }

    /** Queue：覆盖写入下一次内部预览命令要消费的请求。 */
    public static void Queue(Request request)
    {
        lock (Gate)
        {
            _pending = request;
        }
    }

    /** Take：取出并清空待执行请求；无请求时返回 null。 */
    public static Request? Take()
    {
        lock (Gate)
        {
            var request = _pending;
            _pending = null;
            return request;
        }
    }
}
