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
        private sealed class OverlayVisibilityState : IEquatable<OverlayVisibilityState>
        {
            public OverlayVisibilityState(
                bool isOverlayEnabled,
                IntPtr targetHandle,
                uint targetProcessId,
                IntPtr foregroundWindowHandle,
                IntPtr foregroundTargetHandle,
                bool shouldShow,
                bool hasTrackedBounds,
                int trackedLeft,
                int trackedTop,
                int trackedRight,
                int trackedBottom)
            {
                IsOverlayEnabled = isOverlayEnabled;
                TargetHandle = targetHandle;
                TargetProcessId = targetProcessId;
                ForegroundWindowHandle = foregroundWindowHandle;
                ForegroundTargetHandle = foregroundTargetHandle;
                ShouldShow = shouldShow;
                HasTrackedBounds = hasTrackedBounds;
                TrackedLeft = trackedLeft;
                TrackedTop = trackedTop;
                TrackedRight = trackedRight;
                TrackedBottom = trackedBottom;
            }

            public bool IsOverlayEnabled { get; }
            public IntPtr TargetHandle { get; }
            public uint TargetProcessId { get; }
            public IntPtr ForegroundWindowHandle { get; }
            public IntPtr ForegroundTargetHandle { get; }
            public bool ShouldShow { get; }
            public bool HasTrackedBounds { get; }
            public int TrackedLeft { get; }
            public int TrackedTop { get; }
            public int TrackedRight { get; }
            public int TrackedBottom { get; }

            public bool Equals(OverlayVisibilityState other)
            {
                if (ReferenceEquals(null, other))
                {
                    return false;
                }

                if (ReferenceEquals(this, other))
                {
                    return true;
                }

                return
                    IsOverlayEnabled == other.IsOverlayEnabled &&
                    TargetHandle == other.TargetHandle &&
                    TargetProcessId == other.TargetProcessId &&
                    ForegroundWindowHandle == other.ForegroundWindowHandle &&
                    ForegroundTargetHandle == other.ForegroundTargetHandle &&
                    ShouldShow == other.ShouldShow &&
                    HasTrackedBounds == other.HasTrackedBounds &&
                    TrackedLeft == other.TrackedLeft &&
                    TrackedTop == other.TrackedTop &&
                    TrackedRight == other.TrackedRight &&
                    TrackedBottom == other.TrackedBottom;
            }

            public override bool Equals(object obj)
            {
                return Equals(obj as OverlayVisibilityState);
            }

            public override int GetHashCode()
            {
                unchecked
                {
                    int hashCode = IsOverlayEnabled.GetHashCode();
                    hashCode = (hashCode * 397) ^ TargetHandle.GetHashCode();
                    hashCode = (hashCode * 397) ^ (int)TargetProcessId;
                    hashCode = (hashCode * 397) ^ ForegroundWindowHandle.GetHashCode();
                    hashCode = (hashCode * 397) ^ ForegroundTargetHandle.GetHashCode();
                    hashCode = (hashCode * 397) ^ ShouldShow.GetHashCode();
                    hashCode = (hashCode * 397) ^ HasTrackedBounds.GetHashCode();
                    hashCode = (hashCode * 397) ^ TrackedLeft;
                    hashCode = (hashCode * 397) ^ TrackedTop;
                    hashCode = (hashCode * 397) ^ TrackedRight;
                    hashCode = (hashCode * 397) ^ TrackedBottom;
                    return hashCode;
                }
            }
        }

        private const int GwlExStyle = -20;
        private const int WsExToolWindow = 0x00000080;
        private const int WsExTransparent = 0x00000020;
        private const int WsExNoActivate = 0x08000000;
        private const uint EventSystemForeground = 0x0003;
        private const uint EventObjectLocationChange = 0x800B;
        private const uint SwpNoActivate = 0x0010;
        private static readonly IntPtr HwndTopmost = new IntPtr(-1);
        private static readonly TimeSpan UpdateInterval = TimeSpan.FromMilliseconds(250);

        private WindowInteropHelper interopHelper;
        private WinApi.WinEventDelegate locationChangedHook;
        private WinApi.WinEventDelegate foregroundChangedHook;
        private DispatcherTimer updateTimer;
        private IntPtr locationHookHandle;
        private IntPtr foregroundHookHandle;
        private IntPtr activeTargetHandle;
        private OverlayVisibilityState lastVisibilityState;
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

            StartUpdateTimer();
        }

        protected override void OnDetaching()
        {
            UninstallHooks();
            StopUpdateTimer();

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
            behavior.lastVisibilityState = null;
            behavior.ReinstallHooks();
            behavior.UpdateVisibility();
        }

        private static void OnIsOverlayEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var behavior = (OverlayWindowBehavior)d;
            behavior.lastVisibilityState = null;
            behavior.UpdateVisibility();
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
            StopUpdateTimer();
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
                DispatcherPriority.Normal,
                new Action(() =>
                {
                    UpdateVisibility();
                    UpdatePosition();
                }));
        }

        private void StartUpdateTimer()
        {
            if (AssociatedObject == null || updateTimer != null)
            {
                return;
            }

            updateTimer = new DispatcherTimer(DispatcherPriority.Background, AssociatedObject.Dispatcher);
            updateTimer.Interval = UpdateInterval;
            updateTimer.Tick += UpdateTimer_Tick;
            updateTimer.Start();
        }

        private void StopUpdateTimer()
        {
            if (updateTimer == null)
            {
                return;
            }

            updateTimer.Stop();
            updateTimer.Tick -= UpdateTimer_Tick;
            updateTimer = null;
        }

        private void UpdateTimer_Tick(object sender, EventArgs e)
        {
            if (AssociatedObject == null || isClosed || TargetWindowInfo == null)
            {
                return;
            }

            UpdateVisibility();
            UpdatePosition();
        }

        private void UpdateVisibility()
        {
            if (AssociatedObject == null || isClosed || TargetWindowInfo == null)
            {
                return;
            }

            OverlayVisibilityState currentState = CaptureVisibilityState();
            bool shouldShow = currentState.ShouldShow;
            IntPtr foregroundTargetHandle = currentState.ForegroundTargetHandle;
            bool shouldRefreshTopmost = !currentState.Equals(lastVisibilityState);

            if (shouldShow && !AssociatedObject.IsVisible)
            {
                activeTargetHandle = foregroundTargetHandle;
                AssociatedObject.Show();
                ApplyExtendedStyle();
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
                lastVisibilityState = currentState;
                return;
            }

            if (shouldRefreshTopmost)
            {
                ApplyExtendedStyle();

                // Refresh the topmost order only when the visibility inputs changed.
                WinApi.SetWindowZOrder(interopHelper.Handle, HwndTopmost, SwpNoActivate);
            }

            lastVisibilityState = currentState;
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

        private OverlayVisibilityState CaptureVisibilityState()
        {
            IntPtr foregroundWindowHandle = WinApi.GetForegroundWindow();
            IntPtr foregroundTargetHandle = GetForegroundTargetHandle(foregroundWindowHandle);
            bool shouldShow = IsOverlayEnabled && foregroundTargetHandle != IntPtr.Zero;
            IntPtr trackedHandle = foregroundTargetHandle != IntPtr.Zero ? foregroundTargetHandle : GetPositionTargetHandle();

            bool hasTrackedBounds = false;
            int trackedLeft = 0;
            int trackedTop = 0;
            int trackedRight = 0;
            int trackedBottom = 0;

            if (trackedHandle != IntPtr.Zero && WinApi.GetWindowRect(trackedHandle, out WinApi.RECT trackedRect))
            {
                hasTrackedBounds = true;
                trackedLeft = trackedRect.x1;
                trackedTop = trackedRect.y1;
                trackedRight = trackedRect.x2;
                trackedBottom = trackedRect.y2;
            }

            return new OverlayVisibilityState(
                IsOverlayEnabled,
                TargetWindowInfo.Handle,
                TargetWindowInfo.ProcessId,
                foregroundWindowHandle,
                foregroundTargetHandle,
                shouldShow,
                hasTrackedBounds,
                trackedLeft,
                trackedTop,
                trackedRight,
                trackedBottom);
        }

        private IntPtr GetForegroundTargetHandle(IntPtr foreground)
        {
            if (TargetWindowInfo == null)
            {
                return IntPtr.Zero;
            }

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
