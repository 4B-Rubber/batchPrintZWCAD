using System;
using System.ComponentModel;
using System.Windows;

namespace ZwcadBatchPlot;

/// <summary>
/// 批量打印进度窗：显示进度条、当前张数与当前任务说明，并提供停止按钮。
/// 由批打主窗以非模态方式打开；打印结束时调用 <see cref="ForceClose"/>。
/// </summary>
public sealed partial class BatchPrintProgressWindow : Window
{
    private readonly Action _onCancel;
    private bool _forceClose;
    private bool _cancelRequested;

    public BatchPrintProgressWindow(int total, Action onCancel)
    {
        InitializeComponent();
        _onCancel = onCancel ?? throw new ArgumentNullException(nameof(onCancel));
        _progressBar.Maximum = Math.Max(1, total);
        _progressBar.Value = 0;
        _countText.Text = $"0 / {total}";
        Closing += OnClosing;
    }

    /// <summary>更新已开始张数与当前任务说明（completed 从 1 计到 total）。</summary>
    public void Report(int completed, int total, string detail)
    {
        if (total <= 0)
        {
            total = 1;
        }

        _progressBar.IsIndeterminate = false;
        _progressBar.Maximum = total;
        _progressBar.Value = Math.Max(0, Math.Min(completed, total));
        _countText.Text = $"{Math.Max(0, completed)} / {total}";
        _titleText.Text = completed >= total ? "即将完成…" : "正在批量打印…";
        _detailText.Text = string.IsNullOrWhiteSpace(detail) ? "准备中…" : detail;
    }

    /// <summary>进入合并等阶段：进度条拉满并改说明文字。</summary>
    public void SetPhase(string title, string detail)
    {
        _progressBar.IsIndeterminate = false;
        _progressBar.Value = _progressBar.Maximum;
        _titleText.Text = title;
        _detailText.Text = detail;
        _stopButton.IsEnabled = false;
    }

    /// <summary>打印流程结束时强制关闭，忽略关闭确认。</summary>
    public void ForceClose()
    {
        _forceClose = true;
        try
        {
            Close();
        }
        catch
        {
            // 窗口可能已关闭。
        }
    }

    private void Stop_Click(object sender, RoutedEventArgs e)
    {
        RequestCancel();
    }

    private void OnClosing(object? sender, CancelEventArgs e)
    {
        if (_forceClose)
        {
            return;
        }

        // 点标题栏关闭等同于停止：取消打印，但先留住窗口直到外层 ForceClose。
        e.Cancel = true;
        RequestCancel();
    }

    private void RequestCancel()
    {
        if (_cancelRequested)
        {
            return;
        }

        _cancelRequested = true;
        _stopButton.IsEnabled = false;
        _stopButton.Content = "正在停止…";
        _titleText.Text = "正在停止…";
        try
        {
            _onCancel();
        }
        catch
        {
            // 取消失败仍保持窗口，由外层 finally 关闭。
        }
    }
}