using Microsoft.Xaml.Behaviors;
using SpsLogic;
using System;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;

namespace SpsGui.Behaviors
{
    /// <summary>
    /// Keeps the overlay window aligned with and visible only for its target window.
    /// </summary>
    public class OverlayWindowBehavior : Behavior<Window>
    {
        private const int GwlExStyle = -20;
        private const int WsExTopmost = 0x00000008;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExTransparent = 0x00000020;
        private const int WsExNoActivate = 0x08000000;
        private const uint EventSystemForeground = 0x0003;
        private const uint EventObjectLocationChange = 0x800B;
        private const uint SwpNoActivate = 0x0010;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);

        private WindowInteropHelper interopHelper;
        private WinApi.WinEventDelegate locationChangedHook;
        private WinApi.WinEventDelegate foregroundChangedHook;
        private IntPtr locationHookHandle;
        private IntPtr foregroundHookHandle;
        private IntPtr activeTargetHandle;
        private bool isClosed;

        public static readonly DependencyProperty TargetWindowInfoProperty =
            DependencyProperty.Register(
                nameof(TargetWindowInfo),
                typeof(WindowInfo),
                typeof(OverlayWindowBehavior),
                new PropertyMetadata(null, OnTargetWindowInfoChanged));

        public static readonly DependencyProperty IsOverlayEnabledProperty =
            DependencyProperty.Register(
                nameof(IsOverlayEnabled),
                typeof(bool),
                typeof(OverlayWindowBehavior),
                new PropertyMetadata(true, OnIsOverlayEnabledChanged));

        public static readonly DependencyProperty OffsetXProperty =
            DependencyProperty.Register(
                nameof(OffsetX),
                typeof(double),
                typeof(OverlayWindowBehavior),
                new PropertyMetadata(0.0, OnOffsetChanged));

        public static readonly DependencyProperty OffsetYProperty =
            DependencyProperty.Register(
                nameof(OffsetY),
                typeof(double),
                typeof(OverlayWindowBehavior),
                new PropertyMetadata(0.0, OnOffsetChanged));

        public WindowInfo TargetWindowInfo
        {
            get { return (WindowInfo)GetValue(TargetWindowInfoProperty); }
            set { SetValue(TargetWindowInfoProperty, value); }
        }

        public bool IsOverlayEnabled
        {
            get { return (bool)GetValue(IsOverlayEnabledProperty); }
            set { SetValue(IsOverlayEnabledProperty, value); }
        }

        public double OffsetX
        {
            get { return (double)GetValue(OffsetXProperty); }
            set { SetValue(OffsetXProperty, value); }
        }

        public double OffsetY
        {
            get { return (double)GetValue(OffsetYProperty); }
            set { SetValue(OffsetYProperty, value); }
        }

        protected override void OnAttached()
        {
            base.OnAttached();

            AssociatedObject.SourceInitialized += AssociatedObject_SourceInitialized;
            AssociatedObject.ContentRendered += AssociatedObject_ContentRendered;
            AssociatedObject.SizeChanged += AssociatedObject_SizeChanged;
            AssociatedObject.Closed += AssociatedObject_Closed;
        }

        protected override void OnDetaching()
        {
            UninstallHooks();

            if (AssociatedObject != null)
            {
                AssociatedObject.SourceInitialized -= AssociatedObject_SourceInitialized;
                AssociatedObject.ContentRendered -= AssociatedObject_ContentRendered;
                AssociatedObject.SizeChanged -= AssociatedObject_SizeChanged;
                AssociatedObject.Closed -= AssociatedObject_Closed;
            }

            base.OnDetaching();
        }

        private static void OnTargetWindowInfoChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var behavior = (OverlayWindowBehavior)d;
            behavior.ReinstallHooks();
            behavior.UpdateVisibility();
        }

        private static void OnIsOverlayEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((OverlayWindowBehavior)d).UpdateVisibility();
        }

        private static void OnOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            ((OverlayWindowBehavior)d).UpdatePosition();
        }

        private void AssociatedObject_SourceInitialized(object sender, EventArgs e)
        {
            interopHelper = new WindowInteropHelper(AssociatedObject);
            ApplyExtendedStyle();
            ReinstallHooks();
            UpdateVisibility();
        }

        private void AssociatedObject_ContentRendered(object sender, EventArgs e)
        {
            UpdatePosition();
        }

        private void AssociatedObject_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdatePosition();
        }

        private void AssociatedObject_Closed(object sender, EventArgs e)
        {
            isClosed = true;
            UninstallHooks();
        }

        private void ReinstallHooks()
        {
            UninstallHooks();

            if (interopHelper == null || TargetWindowInfo == null)
            {
                return;
            }

            locationChangedHook = LocationChangedHook;
            foregroundChangedHook = ForegroundChangedHook;
            locationHookHandle = WinApi.SetWinEventHook(
                EventObjectLocationChange,
                EventObjectLocationChange,
                IntPtr.Zero,
                locationChangedHook,
                TargetWindowInfo.ProcessId,
                0,
                0);
            foregroundHookHandle = WinApi.SetWinEventHook(
                EventSystemForeground,
                EventSystemForeground,
                IntPtr.Zero,
                foregroundChangedHook,
                0,
                0,
                0);
        }

        private void UninstallHooks()
        {
            if (locationHookHandle != IntPtr.Zero)
            {
                WinApi.UnhookWinEvent(locationHookHandle);
                locationHookHandle = IntPtr.Zero;
            }

            if (foregroundHookHandle != IntPtr.Zero)
            {
                WinApi.UnhookWinEvent(foregroundHookHandle);
                foregroundHookHandle = IntPtr.Zero;
            }
        }

        private void LocationChangedHook(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (eventType == EventObjectLocationChange &&
                TargetWindowInfo != null &&
                IsWindowFromTargetProcess(hwnd) &&
                idObject == 0)
            {
                BeginUpdate();
            }
        }

        private void ForegroundChangedHook(
            IntPtr hWinEventHook,
            uint eventType,
            IntPtr hwnd,
            int idObject,
            int idChild,
            uint dwEventThread,
            uint dwmsEventTime)
        {
            if (eventType == EventSystemForeground)
            {
                BeginUpdate();
            }
        }

        private void BeginUpdate()
        {
            if (AssociatedObject == null || isClosed)
            {
                return;
            }

            AssociatedObject.Dispatcher.BeginInvoke(
                DispatcherPriority.ContextIdle,
                new Action(() =>
                {
                    UpdateVisibility();
                    UpdatePosition();
                }));
        }

        private void UpdateVisibility()
        {
            if (AssociatedObject == null || isClosed || TargetWindowInfo == null)
            {
                return;
            }

            IntPtr foregroundTargetHandle = GetForegroundTargetHandle();
            bool targetForeground = foregroundTargetHandle != IntPtr.Zero;
            bool shouldShow =
                IsOverlayEnabled &&
                WinApi.IsWindow(TargetWindowInfo.Handle) &&
                WinApi.IsWindowVisible(TargetWindowInfo.Handle) &&
                !WinApi.IsIconic(TargetWindowInfo.Handle) &&
                targetForeground;

            if (shouldShow && !AssociatedObject.IsVisible)
            {
                activeTargetHandle = foregroundTargetHandle;
                AssociatedObject.Show();
            }
            else if (!shouldShow && AssociatedObject.IsVisible)
            {
                activeTargetHandle = IntPtr.Zero;
                AssociatedObject.Hide();
                return;
            }

            activeTargetHandle = shouldShow ? foregroundTargetHandle : IntPtr.Zero;

            if (!shouldShow || interopHelper == null)
            {
                return;
            }

            bool shouldTopmost = targetForeground || AssociatedObject.IsActive;
            int exStyle = WinApi.GetWindowLongPtr(interopHelper.Handle, GwlExStyle).ToInt32();
            bool isTopmost = (exStyle & WsExTopmost) != 0;

            if (shouldTopmost && !isTopmost)
            {
                WinApi.SetWindowZOrder(interopHelper.Handle, HwndTopmost, SwpNoActivate);
            }
            else if (!shouldTopmost && isTopmost)
            {
                WinApi.SetWindowZOrder(interopHelper.Handle, TargetWindowInfo.Handle, SwpNoActivate);
                WinApi.SetWindowZOrder(TargetWindowInfo.Handle, interopHelper.Handle, SwpNoActivate);
                ApplyExtendedStyle();
            }
        }

        private void UpdatePosition()
        {
            if (AssociatedObject == null || !AssociatedObject.IsVisible || TargetWindowInfo == null)
            {
                return;
            }

            Rect targetRect = GetTargetRect();
            if (targetRect == Rect.Empty)
            {
                return;
            }

            const double margin = 12;
            AssociatedObject.Left = targetRect.Right - AssociatedObject.ActualWidth - margin + OffsetX;
            AssociatedObject.Top = targetRect.Top + margin + OffsetY;
        }

        private Rect GetTargetRect()
        {
            IntPtr targetHandle = GetPositionTargetHandle();

            if (!WinApi.GetWindowRect(targetHandle, out WinApi.RECT rect))
            {
                return Rect.Empty;
            }

            double scaleX = 1.0;
            double scaleY = 1.0;
            PresentationSource source = PresentationSource.FromVisual(AssociatedObject);
            if (source != null && source.CompositionTarget != null)
            {
                scaleX = source.CompositionTarget.TransformToDevice.M11;
                scaleY = source.CompositionTarget.TransformToDevice.M22;
            }

            return new Rect(
                rect.x1 / scaleX,
                rect.y1 / scaleY,
                (rect.x2 - rect.x1) / scaleX,
                (rect.y2 - rect.y1) / scaleY);
        }

        private void ApplyExtendedStyle()
        {
            if (interopHelper == null)
            {
                return;
            }

            int exStyle = WinApi.GetWindowLongPtr(interopHelper.Handle, GwlExStyle).ToInt32();
            WinApi.SetWindowLongPtr(
                interopHelper.Handle,
                GwlExStyle,
                new IntPtr(exStyle | WsExToolWindow | WsExTransparent | WsExNoActivate));
        }

        private IntPtr GetForegroundTargetHandle()
        {
            if (TargetWindowInfo == null)
            {
                return IntPtr.Zero;
            }

            IntPtr foreground = WinApi.GetForegroundWindow();
            if (foreground == IntPtr.Zero)
            {
                return IntPtr.Zero;
            }

            if (foreground == TargetWindowInfo.Handle)
            {
                return TargetWindowInfo.Handle;
            }

            if (!IsWindowFromTargetProcess(foreground))
            {
                return IntPtr.Zero;
            }

            if (!WinApi.IsWindowVisible(foreground) || WinApi.IsIconic(foreground))
            {
                return IntPtr.Zero;
            }

            return foreground;
        }

        private IntPtr GetPositionTargetHandle()
        {
            if (activeTargetHandle != IntPtr.Zero && WinApi.IsWindow(activeTargetHandle))
            {
                return activeTargetHandle;
            }

            return TargetWindowInfo.Handle;
        }

        private bool IsWindowFromTargetProcess(IntPtr hwnd)
        {
            if (TargetWindowInfo == null || hwnd == IntPtr.Zero)
            {
                return false;
            }

            WinApi.GetWindowThreadProcessId(hwnd, out uint processId);
            return processId == TargetWindowInfo.ProcessId;
        }
    }
}
