using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
#if AUTOCAD
using Autodesk.AutoCAD.DatabaseServices;
#else
using ZwSoft.ZwCAD.DatabaseServices;
#endif

namespace ZwcadBatchPlot;

/// <summary>
/// 等价于 Lisp <c>entget</c>：把图元转成 DXF 组码列表。
/// 运行时从已加载的 accore / acdb / ZwCore 解析导出，不绑死某一版 CAD DLL。
/// </summary>
internal static class CadEntGet
{
    /// <summary>
    /// 对应 native <c>ads_name</c>（两个 64 位整数）。
    /// 自带定义：AutoCAD net48 的 AcMgd 里没有托管 AdsName，ZWCAD 的托管类型命名空间也不稳定，
    /// 内联结构体可保证三个编译目标行为一致。
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    private struct AdsName
    {
        public long A;
        public long B;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate int GetAdsNameFn(out AdsName name, ObjectId id);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr EntGetFn(AdsName name);

    private static readonly object Sync = new();
    private static bool _resolved;
    private static GetAdsNameFn? _getAdsName;
    private static EntGetFn? _entGet;

    private static readonly string[] EntGetModuleNames =
    {
        "accore.dll",
        "ZwCore.dll"
    };

    private static readonly string[] GetAdsNameModuleNames =
    {
        "acdb25.dll",
        "acdb24.dll",
        "acdb23.dll",
        "acdb22.dll",
        "acdb21.dll",
        "acdb20.dll",
        "acdb19.dll",
        "ZwDatabase.dll",
        "zcaddb.dll"
    };

    private static readonly string[] EntGetExportNames =
    {
        "acdbEntGet",
        "zcdbEntGet"
    };

    private static readonly string[] GetAdsNameExportNames =
    {
        "?acdbGetAdsName@@YA?AW4ErrorStatus@Acad@@AEAY01_JVAcDbObjectId@@@Z",
        "?zcdbGetAdsName@@YA?AW4ErrorStatus@Zcad@@AEAY01_JVZcDbObjectId@@@Z",
        "?zcdbGetAdsName@@YA?AW4ErrorStatus@ZcAd@@AEAY01_JVZcDbObjectId@@@Z"
    };

    /// <summary>读取图元 DXF 组码；失败返回 false，不抛给扫描流程。</summary>
    public static bool TryGet(ObjectId id, out TypedValue[] values)
    {
        values = Array.Empty<TypedValue>();
        if (id.IsNull)
        {
            return false;
        }

        EnsureResolved();
        if (_getAdsName == null || _entGet == null)
        {
            return false;
        }

        try
        {
            var status = _getAdsName(out var name, id);
            if (status != 0)
            {
                return false;
            }

            var pointer = _entGet(name);
            if (pointer == IntPtr.Zero)
            {
                return false;
            }

            using var buffer = ResultBuffer.Create(pointer, true);
            values = buffer.AsArray() ?? Array.Empty<TypedValue>();
            return values.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    private static void EnsureResolved()
    {
        if (_resolved)
        {
            return;
        }

        lock (Sync)
        {
            if (_resolved)
            {
                return;
            }

            _entGet = ResolveDelegate<EntGetFn>(EntGetModuleNames, EntGetExportNames);
            _getAdsName = ResolveDelegate<GetAdsNameFn>(GetAdsNameModuleNames, GetAdsNameExportNames);
            if (_entGet == null || _getAdsName == null)
            {
                TryResolveFromLoadedModules();
            }

            _resolved = true;
        }
    }

    private static void TryResolveFromLoadedModules()
    {
        try
        {
            foreach (ProcessModule module in Process.GetCurrentProcess().Modules)
            {
                var name = module.ModuleName ?? "";
                if (_entGet == null
                    && (name.StartsWith("accore", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("ZwCore", StringComparison.OrdinalIgnoreCase)))
                {
                    _entGet = ResolveDelegate<EntGetFn>(new[] { name }, EntGetExportNames);
                }

                if (_getAdsName == null
                    && (name.StartsWith("acdb", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("ZwDatabase", StringComparison.OrdinalIgnoreCase)
                        || name.StartsWith("zcaddb", StringComparison.OrdinalIgnoreCase)))
                {
                    _getAdsName = ResolveDelegate<GetAdsNameFn>(new[] { name }, GetAdsNameExportNames);
                }

                if (_entGet != null && _getAdsName != null)
                {
                    return;
                }
            }
        }
        catch
        {
        }
    }

    private static TDelegate? ResolveDelegate<TDelegate>(string[] moduleNames, string[] exportNames)
        where TDelegate : class
    {
        foreach (var moduleName in moduleNames)
        {
            var module = GetModuleHandle(moduleName);
            if (module == IntPtr.Zero)
            {
                continue;
            }

            foreach (var exportName in exportNames)
            {
                var proc = GetProcAddress(module, exportName);
                if (proc == IntPtr.Zero)
                {
                    continue;
                }

                return (TDelegate)(object)Marshal.GetDelegateForFunctionPointer(proc, typeof(TDelegate));
            }
        }

        return null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr GetModuleHandle(string lpModuleName);

    [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true)]
    private static extern IntPtr GetProcAddress(IntPtr hModule, string procName);
}
