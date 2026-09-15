using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
#if AUTOCAD
using Autodesk.AutoCAD.DatabaseServices;
#else
using ZwSoft.ZwCAD.DatabaseServices;
#endif

namespace ZwcadBatchPlot;

/// <summary>侧载枚举得到的 DWG 模型/布局空间条目。</summary>
public sealed class DwgSpaceEntry
{
    public string FilePath { get; set; } = "";
    public string LayoutName { get; set; } = "";
    public bool IsModelSpace { get; set; }

    /// <summary>模型显示为「模型」，布局用 LayoutName。</summary>
    public string DisplayName => IsModelSpace ? "模型" : LayoutName;

    public bool Selected { get; set; } = true;
}

/// <summary>不打开文档，侧载枚举 DWG 内模型与布局。</summary>
public static class DwgSpaceCatalog
{
    /// <summary>
    /// 枚举各文件中的模型/布局。失败文件写入 <paramref name="errors"/>（可空），不中断其余文件。
    /// 同一文件内：模型优先，其余按 TabOrder。
    /// </summary>
    public static List<DwgSpaceEntry> ListSpaces(IEnumerable<string> files, ICollection<string>? errors = null)
    {
        var result = new List<DwgSpaceEntry>();
        if (files == null)
        {
            return result;
        }

        foreach (var raw in files)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                continue;
            }

            string filePath;
            try
            {
                filePath = Path.GetFullPath(raw);
            }
            catch (Exception ex)
            {
                errors?.Add($"{raw}: {ex.Message}");
                continue;
            }

            try
            {
                result.AddRange(ListSpacesInFile(filePath));
            }
            catch (Exception ex)
            {
                errors?.Add($"{filePath}: {ex.Message}");
            }
        }

        return result;
    }

    private static List<DwgSpaceEntry> ListSpacesInFile(string filePath)
    {
        var entries = new List<(DwgSpaceEntry Entry, int TabOrder)>();
        using var db = new Database(false, true);
        db.ReadDwgFile(filePath, FileOpenMode.OpenForReadAndAllShare, true, "");
        db.CloseInput(true);

        using var tr = db.TransactionManager.StartTransaction();
        var blockTable = (BlockTable)tr.GetObject(db.BlockTableId, OpenMode.ForRead);
        foreach (ObjectId recordId in blockTable)
        {
            var owner = (BlockTableRecord)tr.GetObject(recordId, OpenMode.ForRead);
            if (!owner.IsLayout || owner.LayoutId.IsNull)
            {
                continue;
            }

            Layout layout;
            try
            {
                layout = (Layout)tr.GetObject(owner.LayoutId, OpenMode.ForRead);
            }
            catch
            {
                continue;
            }

            var layoutName = layout.LayoutName ?? "";
            if (string.IsNullOrWhiteSpace(layoutName))
            {
                continue;
            }

            entries.Add((
                new DwgSpaceEntry
                {
                    FilePath = filePath,
                    LayoutName = layoutName,
                    IsModelSpace = layout.ModelType,
                    Selected = true
                },
                layout.TabOrder));
        }

        tr.Commit();

        // 模型优先，其余按 TabOrder。
        entries.Sort((a, b) =>
        {
            if (a.Entry.IsModelSpace != b.Entry.IsModelSpace)
            {
                return a.Entry.IsModelSpace ? -1 : 1;
            }

            var tab = a.TabOrder.CompareTo(b.TabOrder);
            if (tab != 0)
            {
                return tab;
            }

            return string.Compare(a.Entry.LayoutName, b.Entry.LayoutName, StringComparison.OrdinalIgnoreCase);
        });

        return entries.Select(x => x.Entry).ToList();
    }
}
