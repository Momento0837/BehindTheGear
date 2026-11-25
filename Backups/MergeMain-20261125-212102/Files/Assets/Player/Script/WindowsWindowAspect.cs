#if UNITY_STANDALONE_WIN || UNITY_EDITOR_WIN
using System;
using System.Runtime.InteropServices;
using UnityEngine;

// Native window messages keep the client (game image), excluding the title bar, at 16:9.
public sealed class WindowsWindowAspect : MonoBehaviour
{
    [StructLayout(LayoutKind.Sequential)] struct Rect { public int left, top, right, bottom; }
    [StructLayout(LayoutKind.Sequential)] struct MonitorInfo { public int size; public Rect monitor, work; public uint flags; }
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    delegate IntPtr SubclassProc(IntPtr window, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data);
    static readonly SubclassProc Callback = WindowProc;
    static readonly UIntPtr SubclassId = new UIntPtr(0x425447);
    IntPtr window;
    bool installed;
    float nextAttempt;

    [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
    [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll")] static extern bool GetWindowRect(IntPtr h, out Rect rect);
    [DllImport("user32.dll")] static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] static extern bool ShowWindow(IntPtr h, int command);
    [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int width, int height, uint flags);
    [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
    [DllImport("user32.dll", CharSet = CharSet.Auto)] static extern bool GetMonitorInfo(IntPtr h, ref MonitorInfo info);
    [DllImport("comctl32.dll")] static extern bool SetWindowSubclass(IntPtr h, SubclassProc proc, UIntPtr id, UIntPtr data);
    [DllImport("comctl32.dll")] static extern bool RemoveWindowSubclass(IntPtr h, SubclassProc proc, UIntPtr id);
    [DllImport("comctl32.dll")] static extern IntPtr DefSubclassProc(IntPtr h, uint msg, IntPtr w, IntPtr l);

    void Update()
    {
#if !UNITY_EDITOR
        if (!installed)
        {
            if (Time.unscaledTime < nextAttempt) return;
            nextAttempt = Time.unscaledTime + 1;
            window = GetActiveWindow(); // Only a window owned by this Unity thread.
            if (window == IntPtr.Zero) return;
            installed = SetWindowSubclass(window, Callback, SubclassId, UIntPtr.Zero);
            if (!installed) return;
        }
        if (IsIconic(window)) return;
        // Maximize / Windows Snap do not send WM_SIZING. Fit a normal 16:9 window to this monitor.
        if (IsZoomed(window))
        {
            var info = new MonitorInfo { size = Marshal.SizeOf<MonitorInfo>() };
            if (GetMonitorInfo(MonitorFromWindow(window, 2), ref info))
            {
                ShowWindow(window, 9);
                GetWindowRect(window, out var outer); GetClientRect(window, out var client);
                int frameW = outer.right - outer.left - client.right;
                int frameH = outer.bottom - outer.top - client.bottom;
                int unit = Math.Max(1, Math.Min((info.work.right-info.work.left-frameW)/16, (info.work.bottom-info.work.top-frameH)/9));
                int width=unit*16+frameW, height=unit*9+frameH;
                SetWindowPos(window, IntPtr.Zero, info.work.left+(info.work.right-info.work.left-width)/2,
                    info.work.top+(info.work.bottom-info.work.top-height)/2, width, height, 0x14);
            }
            return;
        }
        if (GetClientRect(window, out var area) && area.right > 0 && area.bottom > 0
            && area.right * 9 != area.bottom * 16 && GetWindowRect(window, out var rect))
        {
            int unit = Math.Max(1, Math.Min(area.right/16, area.bottom/9));
            SetWindowPos(window, IntPtr.Zero, 0, 0, unit*16+rect.right-rect.left-area.right,
                unit*9+rect.bottom-rect.top-area.bottom, 0x16);
        }
#endif
    }

    [AOT.MonoPInvokeCallback(typeof(SubclassProc))]
    static IntPtr WindowProc(IntPtr h, uint message, IntPtr wParam, IntPtr lParam, UIntPtr id, UIntPtr data)
    {
        if (message == 0x214 && GetWindowRect(h, out var outer) && GetClientRect(h, out var client)) // WM_SIZING
        {
            var rect = Marshal.PtrToStructure<Rect>(lParam);
            int frameW=outer.right-outer.left-client.right, frameH=outer.bottom-outer.top-client.bottom;
            int edge=wParam.ToInt32();
            int unit=Math.Max(20, (int)Math.Round(edge==3 || edge==6
                ? (rect.bottom-rect.top-frameH)/9.0 : (rect.right-rect.left-frameW)/16.0));
            int width=unit*16+frameW, height=unit*9+frameH;
            if(edge==1 || edge==4 || edge==7) rect.left=rect.right-width; else rect.right=rect.left+width;
            if(edge==3 || edge==4 || edge==5) rect.top=rect.bottom-height; else rect.bottom=rect.top+height;
            Marshal.StructureToPtr(rect,lParam,false);
            return new IntPtr(1);
        }
        if(message==0x82) RemoveWindowSubclass(h,Callback,id); // WM_NCDESTROY
        return DefSubclassProc(h,message,wParam,lParam);
    }
    void OnDestroy() { if(installed && window!=IntPtr.Zero) RemoveWindowSubclass(window,Callback,SubclassId); }
}
#endif
