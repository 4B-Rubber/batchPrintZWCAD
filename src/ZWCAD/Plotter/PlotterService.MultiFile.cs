using System;
using System.Collections.Generic;
using System.Threading;
using ZwSoft.ZwCAD.ApplicationServices;
using ZwSoft.ZwCAD.DatabaseServices;
using CadApp = ZwSoft.ZwCAD.ApplicationServices.Application;

/**
 * @file PlotterService.MultiFile.cs（ZWCAD）
 * @description 非当前打开图的独立出图编排：打开 DWG、切活动文档、按布局 Regen。
 *
 * 主要功能：
 * - PlotExternalFileJobs：一组外部 DWG 的打印
 * - PreviewExternalFile：外部图预览
 * - PrepareExternalSpace：同一 SpaceName 只切布局并 Regen 一次
 *
 * 注意：不与当前打开图的 EnsureSpaceRegenerated / PlotCurrentDocumentGroup 共用实现。
 * 必须等外部图真正成为活动文档并生成显示列表后再出图，否则会出现图号对、页面全白。
 */

namespace ZwcadBatchPlot;

public static partial class PlotterService
{
    private const int ExternalDocumentReadyTimeoutMs = 10000;

    /**
     * PlotExternalFileJobs：打开或定位外部 DWG，设为活动文档后按布局 Regen 再出图。
     * 本次打开的文档结束后关闭且不保存；本来就开着的文档不关。
     */
    private static void PlotExternalFileJobs(
        IReadOnlyList<PlotJob> jobs,
        string sourceFile,
        string deviceName,
        string styleSheet,
        AppSettings settings,
        Action<PlotJob>? beforeJob,
        List<PlotJobResult> results,
        CancellationToken cancellationToken)
    {
        var alreadyOpen = FindOpenDocument(sourceFile) != null;
        var doc = OpenExternalDocumentForPlot(sourceFile);
        var oldDatabase = HostApplicationServices.WorkingDatabase;
        try
        {
            ActivateExternalDocument(doc, sourceFile);
            try
            {
                doc.Database.ResolveXrefs(true, false);
            }
            catch
            {
                // 无外部参照或宿主拒绝解析时，仍按已打开内容出图。
            }

            string? lastSpaceKey = null;
            foreach (var job in jobs)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    PrepareExternalSpace(doc, job, ref lastSpaceKey);
                    beforeJob?.Invoke(job);
                    // 进度窗 Activate 会抢走 CAD 焦点，出图前必须再次确认活动文档。
                    ActivateExternalDocument(doc, sourceFile);
                    using (doc.LockDocument())
                    {
                        PlotDatabase(doc.Database, doc.Name, job, deviceName, styleSheet, settings, doc);
                    }

                    results.Add(new PlotJobResult { Job = job });
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    results.Add(new PlotJobResult { Job = job, Error = ex });
                }
            }
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = oldDatabase;
            if (!alreadyOpen)
            {
                TryCloseWithoutSave(doc);
            }
        }
    }

    /**
     * PreviewExternalFile：打开外部 DWG、切活动文档、目标空间 Regen 一次后预览。
     * 本次打开的文档结束后关闭且不保存。
     */
    private static void PreviewExternalFile(PlotJob job, string deviceName, string styleSheet)
    {
        var oldActive = CadApp.DocumentManager.MdiActiveDocument;
        var alreadyOpen = FindOpenDocument(job.SourceFile) != null;
        var doc = OpenExternalDocumentForPlot(job.SourceFile);
        var oldDatabase = HostApplicationServices.WorkingDatabase;
        try
        {
            ActivateExternalDocument(doc, job.SourceFile);
            string? lastSpaceKey = null;
            PrepareExternalSpace(doc, job, ref lastSpaceKey);
            ActivateExternalDocument(doc, job.SourceFile);
            using (doc.LockDocument())
            {
                PreviewDatabase(doc.Database, doc.Name, job, deviceName, styleSheet, doc);
            }
        }
        finally
        {
            HostApplicationServices.WorkingDatabase = oldDatabase;
            if (!alreadyOpen)
            {
                TryCloseWithoutSave(doc);
            }

            if (oldActive != null && !oldActive.IsDisposed)
            {
                CadApp.DocumentManager.MdiActiveDocument = oldActive;
            }
        }
    }

    /**
     * OpenExternalDocumentForPlot：打开外部 DWG，并等到文档管理器里能按路径找到它。
     * 禁止在打开未完成时回退到当前活动文档，否则会用错图的显示列表打出白页。
     */
    private static Document OpenExternalDocumentForPlot(string sourceFile)
    {
        var existing = FindOpenDocument(sourceFile);
        if (existing != null)
        {
            return existing;
        }

        CadApp.DocumentManager.Open(sourceFile, false);
        var waited = 0;
        while (waited < ExternalDocumentReadyTimeoutMs)
        {
            var found = FindOpenDocument(sourceFile);
            if (found != null)
            {
                return found;
            }

            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(50);
            waited += 50;
        }

        throw new InvalidOperationException("未能打开外部图纸，已停止该文件打印以避免打到错误文档: " + sourceFile);
    }

    /**
     * ActivateExternalDocument：把外部图设为活动文档并等待切换完成，再刷屏生成显示列表。
     */
    private static void ActivateExternalDocument(Document doc, string sourceFile)
    {
        if (CadApp.DocumentManager.MdiActiveDocument != doc)
        {
            CadApp.DocumentManager.MdiActiveDocument = doc;
        }

        var waited = 0;
        while (CadApp.DocumentManager.MdiActiveDocument != doc && waited < ExternalDocumentReadyTimeoutMs)
        {
            System.Windows.Forms.Application.DoEvents();
            Thread.Sleep(50);
            waited += 50;
        }

        if (CadApp.DocumentManager.MdiActiveDocument != doc)
        {
            throw new InvalidOperationException("无法将外部图纸切换为当前文档: " + sourceFile);
        }

        HostApplicationServices.WorkingDatabase = doc.Database;
        PumpExternalDocumentDisplay(doc);
    }

    /**
     * PrepareExternalSpace：把外部文档切到目标模型/布局并 Regen。
     * 同一 SpaceName 只重生成一次；模型非 DCS 窗口按每张图对齐视图。
     * 切布局后先释放文档锁再刷屏，让视口显示列表有机会生成。
     */
    private static void PrepareExternalSpace(Document doc, PlotJob job, ref string? lastSpaceKey)
    {
        var spaceKey = string.IsNullOrWhiteSpace(job.SpaceName) ? "__CURRENT__" : job.SpaceName.Trim();
        if (!string.Equals(lastSpaceKey, spaceKey, StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                using (doc.LockDocument())
                {
                    HostApplicationServices.WorkingDatabase = doc.Database;
                    using (var tr = doc.Database.TransactionManager.StartTransaction())
                    {
                        var layout = FindLayoutForJob(tr, doc.Database, job);
                        LayoutManager.Current.CurrentLayout = layout.LayoutName;
                        tr.Commit();
                    }

                    doc.Editor.Regen();
                }

                PumpExternalDocumentDisplay(doc);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    $"无法准备外部图布局“{job.SpaceName}”，已停止该张打印以避免输出错误区域。",
                    ex);
            }

            lastSpaceKey = spaceKey;
        }

        if (!job.IsPaperSpace && !job.IsDcsWindow)
        {
            using (doc.LockDocument())
            {
                PrepareEditorViewForPlot(doc, job);
            }
        }
    }

    /** PumpExternalDocumentDisplay：让 CAD 处理切文档/切布局后的重绘。 */
    private static void PumpExternalDocumentDisplay(Document doc)
    {
        try
        {
            doc.Editor.UpdateScreen();
        }
        catch
        {
        }

        try
        {
            CadApp.UpdateScreen();
        }
        catch
        {
        }

        System.Windows.Forms.Application.DoEvents();
        Thread.Sleep(50);
        System.Windows.Forms.Application.DoEvents();
    }
}
