using System.Collections.Generic;
#if AUTOCAD
using Autodesk.AutoCAD.DatabaseServices;
using Autodesk.AutoCAD.EditorInput;
#else
using ZwSoft.ZwCAD.DatabaseServices;
using ZwSoft.ZwCAD.EditorInput;
#endif

namespace ZwcadBatchPlot;

/// <summary>
/// CAD 对象选择提示：仅封装 <see cref="Editor.GetSelection"/> 与类型过滤。
/// 扫描范围仍走现有对话框；不注入按键、不拦截右键、不使用 WinForms 菜单。
/// </summary>
internal static class ObjectSelectionPrompt
{
    /// <summary>图框块扫描：选择阶段只允许 INSERT（块参照）。</summary>
    public static SelectionFilter TitleBlockFilter()
    {
        return new SelectionFilter(new[]
        {
            new TypedValue((int)DxfCode.Start, "INSERT")
        });
    }

    /// <summary>
    /// 矩形框扫描：块参照与各类多段线；开启四线识别时额外允许 LINE。
    /// </summary>
    /// <param name="includeLines">是否把直线纳入可选类型。</param>
    public static SelectionFilter RectangleFrameFilter(bool includeLines)
    {
        var allowedTypes = new List<string> { "INSERT", "LWPOLYLINE", "POLYLINE", "3DPOLYLINE" };
        if (includeLines)
        {
            allowedTypes.Add("LINE");
        }

        return new SelectionFilter(new[]
        {
            new TypedValue((int)DxfCode.Start, string.Join(",", allowedTypes))
        });
    }

    /// <summary>
    /// 提示用户点选或框选对象。取消、空选或失败返回 null，调用方不得改动现有作业清单。
    /// </summary>
    /// <param name="editor">当前文档编辑器。</param>
    /// <param name="message">选择提示（含前导换行）。</param>
    /// <param name="filter">选择过滤器；null 表示不过滤类型。</param>
    /// <returns>选中对象的 ObjectId；取消时为 null。</returns>
    public static ObjectId[]? Prompt(Editor editor, string message, SelectionFilter? filter)
    {
        var options = new PromptSelectionOptions
        {
            MessageForAdding = message
        };

        PromptSelectionResult selection;
        try
        {
            selection = filter != null
                ? editor.GetSelection(options, filter)
                : editor.GetSelection(options);
        }
        catch
        {
            return null;
        }

        if (selection.Status != PromptStatus.OK || selection.Value == null)
        {
            return null;
        }

        var ids = selection.Value.GetObjectIds();
        return ids != null && ids.Length > 0 ? ids : null;
    }
}
