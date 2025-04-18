// Copyright (c) DGP Studio. All rights reserved.
// Licensed under the MIT license.

using Snap.Hutao.Core.Graphics;
using Snap.Hutao.Win32.Foundation;
using Snap.Hutao.Win32.UI.WindowsAndMessaging;
using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using static Snap.Hutao.Win32.ConstValues;
using static Snap.Hutao.Win32.Kernel32;
using static Snap.Hutao.Win32.Macros;
using static Snap.Hutao.Win32.User32;

namespace Snap.Hutao.UI.Shell;

[SuppressMessage("", "SA1310")]
internal sealed partial class NotifyIconMessageWindow : IDisposable
{
    public const uint WM_NOTIFYICON_CALLBACK = 0x444U;
    private const string WindowClassName = "SnapHutaoNotifyIconMessageWindowClass";

    private static readonly ConcurrentDictionary<HWND, NotifyIconMessageWindow> WindowTable = [];

    [SuppressMessage("", "SA1306")]
    private readonly uint WM_TASKBARCREATED;

    private bool disposed;

    public unsafe NotifyIconMessageWindow()
    {
        ushort atom;
        fixed (char* className = WindowClassName)
        {
            WNDCLASSW wc = new()
            {
                lpfnWndProc = WNDPROC.Create(&OnWindowProcedure),
                lpszClassName = className,
            };

            // 0x80070582 ERROR_CLASS_ALREADY_EXISTS can happen
            atom = RegisterClassW(&wc);
        }

        if (atom is 0)
        {
            Marshal.ThrowExceptionForHR(HRESULT_FROM_WIN32(GetLastError()));

            // If HRESULT is not available, throw anyway
            ArgumentOutOfRangeException.ThrowIfZero(atom);
        }

        // https://learn.microsoft.com/zh,cn/windows/win32/shell/taskbar#taskbar,creation,notification
        WM_TASKBARCREATED = RegisterWindowMessageW("TaskbarCreated");

        Hwnd = CreateWindowExW(0, WindowClassName, WindowClassName, 0, 0, 0, 0, 0, default, default, default, default);

        if (Hwnd == default)
        {
            Marshal.ThrowExceptionForHR(HRESULT_FROM_WIN32(GetLastError()));
        }

        WindowTable.TryAdd(Hwnd, this);
    }

    ~NotifyIconMessageWindow()
    {
        Dispose();
    }

    public Action<NotifyIconMessageWindow>? TaskbarCreated { get; init; }

    public Action<NotifyIconMessageWindow, PointUInt16>? ContextMenuRequested { get; init; }

    public Action<NotifyIconMessageWindow, PointUInt16>? IconSelected { get; init; }

    public HWND Hwnd { get; }

    public unsafe void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;

        DestroyWindow(Hwnd);
        WindowTable.TryRemove(Hwnd, out _);

        fixed (char* className = WindowClassName)
        {
            UnregisterClassW(className);
        }

        GC.SuppressFinalize(this);
    }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static unsafe LRESULT OnWindowProcedure(HWND hwnd, uint uMsg, WPARAM wParam, LPARAM lParam)
    {
        if (!WindowTable.TryGetValue(hwnd, out NotifyIconMessageWindow? window) || XamlApplicationLifetime.Exiting)
        {
            return DefWindowProcW(hwnd, uMsg, wParam, lParam);
        }

        if (window.disposed)
        {
            return BOOL.FALSE;
        }

        if (uMsg == window.WM_TASKBARCREATED)
        {
            // TODO: Re-add the notify icon.
            window.TaskbarCreated?.Invoke(window);
        }

        // https://learn.microsoft.com/zh-cn/windows/win32/api/shellapi/ns-shellapi-notifyicondataw
        if (uMsg is WM_NOTIFYICON_CALLBACK)
        {
            LPARAM2 lParam2 = *(LPARAM2*)&lParam;
            PointUInt16 wParam2 = *(PointUInt16*)&wParam;

            switch (lParam2.Low)
            {
                case WM_CONTEXTMENU:
                    window.ContextMenuRequested?.Invoke(window, wParam2);
                    break;
                case WM_MOUSEMOVE:
                    // X: wParam2.X Y: wParam2.Y Low: WM_MOUSEMOVE
                    break;
                case WM_LBUTTONDOWN:
                case WM_LBUTTONUP:
                    break;
                case WM_RBUTTONDOWN:
                case WM_RBUTTONUP:
                    break;
                case NIN_SELECT:
                    // X: wParam2.X Y: wParam2.Y Low: NIN_SELECT
                    window.IconSelected?.Invoke(window, wParam2);
                    break;
                case NIN_POPUPOPEN:
                    // X: wParam2.X Y: 0? Low: NIN_POPUPOPEN
                    break;
                case NIN_POPUPCLOSE:
                    // X: wParam2.X Y: 0? Low: NIN_POPUPCLOSE
                    break;
            }
        }
#if DEBUG
        else
        {
            switch (uMsg)
            {
                case WM_ACTIVATEAPP:
                    break;
                case WM_DWMNCRENDERINGCHANGED:
                    break;
            }
        }
#endif

        return DefWindowProcW(hwnd, uMsg, wParam, lParam);
    }

    private readonly struct LPARAM2
    {
#pragma warning disable CS0649
        public readonly uint Low;
        public readonly uint High;
#pragma warning restore CS0649
    }
}