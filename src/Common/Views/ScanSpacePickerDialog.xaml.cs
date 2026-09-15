using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ZwcadBatchPlot;

/// <summary>
/// 多文件批打前勾选模型/布局。按文件分组的扁平列表（非折叠树）。
/// 调用方用 <see cref="CadDialog.ShowModal"/> 显示；DialogResult=true 表示开始扫描。
/// </summary>
public sealed partial class ScanSpacePickerDialog : Window
{
    private sealed class FileGroup
    {
        public string FilePath { get; set; } = "";
        public CheckBox HeaderCheck { get; set; } = null!;
        public List<SpaceRow> Rows { get; } = new();
        public bool SuppressHeaderSync;
        public bool SuppressChildSync;
    }

    private sealed class SpaceRow
    {
        public DwgSpaceEntry Entry { get; set; } = null!;
        public CheckBox Check { get; set; } = null!;
        public FileGroup Group { get; set; } = null!;
    }

    private readonly List<FileGroup> _groups = new();
    private readonly List<DwgSpaceEntry> _selected = new();

    /// <summary>用户勾选并确认开始扫描的空间。</summary>
    public IReadOnlyList<DwgSpaceEntry> SelectedSpaces => _selected;

    public ScanSpacePickerDialog(IEnumerable<DwgSpaceEntry> spaces)
    {
        InitializeComponent();
        BuildContent(spaces?.ToList() ?? new List<DwgSpaceEntry>());
    }

    private void BuildContent(List<DwgSpaceEntry> spaces)
    {
        var root = new DockPanel { Margin = new Thickness(12) };

        var top = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Margin = new Thickness(0, 0, 0, 8)
        };
        DockPanel.SetDock(top, Dock.Top);
        var selectAll = new Button
        {
            Content = "全选",
            MinWidth = 76,
            Style = TryFindResource("PluginButtonStyle") as Style,
            Margin = new Thickness(0, 0, 6, 0)
        };
        selectAll.Click += (_, __) => SetAllSelected(true);
        var selectNone = new Button
        {
            Content = "全不选",
            MinWidth = 76,
            Style = TryFindResource("PluginButtonStyle") as Style
        };
        selectNone.Click += (_, __) => SetAllSelected(false);
        top.Children.Add(selectAll);
        top.Children.Add(selectNone);
        top.Children.Add(new TextBlock
        {
            Text = "勾选需要扫描的模型/布局，确认后将清空清单并重新扫描。",
            Foreground = Brushes.DimGray,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(12, 0, 0, 0),
            TextWrapping = TextWrapping.Wrap
        });
        root.Children.Add(top);

        var bottom = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 10, 0, 0)
        };
        DockPanel.SetDock(bottom, Dock.Bottom);
        var cancel = new Button
        {
            Content = "取消",
            MinWidth = 76,
            Style = TryFindResource("PluginButtonStyle") as Style,
            IsCancel = true,
            Margin = new Thickness(0, 0, 8, 0)
        };
        cancel.Click += (_, __) =>
        {
            DialogResult = false;
            Close();
        };
        var start = new Button
        {
            Content = "开始扫描",
            MinWidth = 104,
            Style = TryFindResource("PluginButtonStyle") as Style,
            IsDefault = true
        };
        start.Click += (_, __) => ConfirmStart();
        bottom.Children.Add(cancel);
        bottom.Children.Add(start);
        root.Children.Add(bottom);

        var listHost = new Border
        {
            BorderBrush = new SolidColorBrush(Color.FromRgb(0xCD, 0xD2, 0xD8)),
            BorderThickness = new Thickness(1),
            Background = Brushes.White,
            Padding = new Thickness(4)
        };
        var scroll = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled
        };
        var stack = new StackPanel();
        scroll.Content = stack;
        listHost.Child = scroll;
        root.Children.Add(listHost);

        foreach (var fileGroup in spaces.GroupBy(s => s.FilePath, StringComparer.OrdinalIgnoreCase))
        {
            var group = new FileGroup { FilePath = fileGroup.Key };
            var header = new CheckBox
            {
                Content = Path.GetFileName(fileGroup.Key),
                IsThreeState = true,
                Margin = new Thickness(2, 4, 2, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                FontWeight = FontWeights.SemiBold,
                ToolTip = fileGroup.Key
            };
            group.HeaderCheck = header;
            header.Checked += (_, __) => OnHeaderChanged(group, true);
            header.Unchecked += (_, __) => OnHeaderChanged(group, false);
            // 三态点选到 null（部分选）时，按「全选」处理，避免卡在中间态。
            header.Indeterminate += (_, __) =>
            {
                if (group.SuppressHeaderSync)
                {
                    return;
                }

                header.IsChecked = true;
            };
            stack.Children.Add(header);

            foreach (var entry in fileGroup)
            {
                var row = new SpaceRow { Entry = entry, Group = group };
                var child = new CheckBox
                {
                    Content = entry.DisplayName,
                    IsChecked = entry.Selected,
                    Margin = new Thickness(22, 1, 2, 1),
                    VerticalContentAlignment = VerticalAlignment.Center
                };
                row.Check = child;
                child.Checked += (_, __) => OnChildChanged(row, true);
                child.Unchecked += (_, __) => OnChildChanged(row, false);
                group.Rows.Add(row);
                stack.Children.Add(child);
            }

            _groups.Add(group);
            SyncHeaderFromChildren(group);
        }

        Content = root;
    }

    private void OnHeaderChanged(FileGroup group, bool selected)
    {
        if (group.SuppressHeaderSync)
        {
            return;
        }

        group.SuppressChildSync = true;
        try
        {
            foreach (var row in group.Rows)
            {
                row.Entry.Selected = selected;
                row.Check.IsChecked = selected;
            }
        }
        finally
        {
            group.SuppressChildSync = false;
        }
    }

    private void OnChildChanged(SpaceRow row, bool selected)
    {
        if (row.Group.SuppressChildSync)
        {
            return;
        }

        row.Entry.Selected = selected;
        SyncHeaderFromChildren(row.Group);
    }

    private static void SyncHeaderFromChildren(FileGroup group)
    {
        group.SuppressHeaderSync = true;
        try
        {
            var total = group.Rows.Count;
            var checkedCount = group.Rows.Count(r => r.Check.IsChecked == true);
            if (checkedCount == 0)
            {
                group.HeaderCheck.IsChecked = false;
            }
            else if (checkedCount == total)
            {
                group.HeaderCheck.IsChecked = true;
            }
            else
            {
                group.HeaderCheck.IsChecked = null;
            }
        }
        finally
        {
            group.SuppressHeaderSync = false;
        }
    }

    private void SetAllSelected(bool selected)
    {
        foreach (var group in _groups)
        {
            group.SuppressHeaderSync = true;
            group.SuppressChildSync = true;
            try
            {
                foreach (var row in group.Rows)
                {
                    row.Entry.Selected = selected;
                    row.Check.IsChecked = selected;
                }

                group.HeaderCheck.IsChecked = selected;
            }
            finally
            {
                group.SuppressHeaderSync = false;
                group.SuppressChildSync = false;
            }
        }
    }

    private void ConfirmStart()
    {
        _selected.Clear();
        foreach (var group in _groups)
        {
            foreach (var row in group.Rows)
            {
                row.Entry.Selected = row.Check.IsChecked == true;
                if (row.Entry.Selected)
                {
                    _selected.Add(row.Entry);
                }
            }
        }

        if (_selected.Count == 0)
        {
            MessageBox.Show("请至少勾选一个模型或布局。", Title, MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        DialogResult = true;
        Close();
    }
}
