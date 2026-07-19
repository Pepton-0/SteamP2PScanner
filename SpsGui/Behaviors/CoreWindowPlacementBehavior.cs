using Microsoft.Xaml.Behaviors;
using SpsLogic;
using System;
using System.Linq;
using System.Windows;
using System.Windows.Forms;
using System.Windows.Interop;
using Media = System.Windows.Media;

namespace SpsGui.Behaviors
{
    /// <summary>
    /// Restores and stores the main window placement using AppConfig.
    /// </summary>
    public sealed class CoreWindowPlacementBehavior : Behavior<Window>
    {
        private bool restored;
        private bool stored;
        private bool userMovedOrResized;

        protected override void OnAttached()
        {
            base.OnAttached();

            AssociatedObject.SourceInitialized += AssociatedObject_SourceInitialized;
            AssociatedObject.LocationChanged += AssociatedObject_LocationChanged;
            AssociatedObject.SizeChanged += AssociatedObject_SizeChanged;
            AssociatedObject.Closed += AssociatedObject_Closed;
        }

        protected override void OnDetaching()
        {
            StorePlacementOnce();

            if (AssociatedObject != null)
            {
                AssociatedObject.SourceInitialized -= AssociatedObject_SourceInitialized;
                AssociatedObject.LocationChanged -= AssociatedObject_LocationChanged;
                AssociatedObject.SizeChanged -= AssociatedObject_SizeChanged;
                AssociatedObject.Closed -= AssociatedObject_Closed;
            }

            base.OnDetaching();
        }

        private void AssociatedObject_SourceInitialized(object sender, EventArgs e)
        {
            RestorePlacement();
            restored = true;
        }

        private void AssociatedObject_LocationChanged(object sender, EventArgs e)
        {
            if (restored)
            {
                userMovedOrResized = true;
            }
        }

        private void AssociatedObject_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (restored)
            {
                userMovedOrResized = true;
            }
        }

        private void AssociatedObject_Closed(object sender, EventArgs e)
        {
            StorePlacementOnce();
        }

        private void RestorePlacement()
        {
            if (AssociatedObject == null || !AppConfig.Instance.CoreWindowPlacementSaved)
            {
                return;
            }

            if (userMovedOrResized)
            {
                return;
            }

            Rect saved = new Rect(
                AppConfig.Instance.CoreWindowLeft,
                AppConfig.Instance.CoreWindowTop,
                AppConfig.Instance.CoreWindowWidth,
                AppConfig.Instance.CoreWindowHeight);

            if (!IsValidRect(saved))
            {
                return;
            }

            // assumes that the computer has one display at least
            Rect[] workingAreas = GetWorkingAreasInDips();
            if (workingAreas.Length == 0)
            {
                return;
            }

            int displayIndex = AppConfig.Instance.CoreWindowDisplayIndex;
            Rect? targetArea = displayIndex >= 0 && displayIndex < workingAreas.Length
                ? workingAreas[displayIndex]
                : (Rect?)null;

            if (!targetArea.HasValue || !targetArea.Value.IntersectsWith(saved))
            {
                targetArea = workingAreas
                    .Select(area => (Rect?)area)
                    .FirstOrDefault(area => area.Value.IntersectsWith(saved));
            }

            if (!targetArea.HasValue)
            {
                targetArea = workingAreas
                    .Select(area => (Rect?)area)
                    .FirstOrDefault(area => area.Value.Contains(GetRectCenter(saved)));
            }

            if (!targetArea.HasValue)
            {
                targetArea = workingAreas[0];
            }

            Rect restoredRect = ClampToArea(saved, targetArea.Value);
            AssociatedObject.WindowStartupLocation = WindowStartupLocation.Manual;
            AssociatedObject.Width = Math.Max(AssociatedObject.MinWidth, restoredRect.Width);
            AssociatedObject.Height = Math.Max(AssociatedObject.MinHeight, restoredRect.Height);
            AssociatedObject.Left = restoredRect.Left;
            AssociatedObject.Top = restoredRect.Top;
        }

        private void StorePlacementOnce()
        {
            if (stored || AssociatedObject == null)
            {
                return;
            }

            stored = true;

            Rect current = new Rect(
                AssociatedObject.Left,
                AssociatedObject.Top,
                AssociatedObject.ActualWidth > 0 ? AssociatedObject.ActualWidth : AssociatedObject.Width,
                AssociatedObject.ActualHeight > 0 ? AssociatedObject.ActualHeight : AssociatedObject.Height);

            if (!IsValidRect(current))
            {
                return;
            }

            Rect[] workingAreas = GetWorkingAreasInDips();
            int displayIndex = FindDisplayIndex(current, workingAreas);

            AppConfig.Instance.CoreWindowPlacementSaved = true;
            AppConfig.Instance.CoreWindowDisplayIndex = displayIndex;
            AppConfig.Instance.CoreWindowLeft = current.Left;
            AppConfig.Instance.CoreWindowTop = current.Top;
            AppConfig.Instance.CoreWindowWidth = current.Width;
            AppConfig.Instance.CoreWindowHeight = current.Height;
        }

        private Rect[] GetWorkingAreasInDips()
        {
            Media.Matrix transform = GetTransformFromDevice();
            return Screen.AllScreens
                .Select(screen => ToDipRect(screen.WorkingArea, transform))
                .ToArray();
        }

        private Media.Matrix GetTransformFromDevice()
        {
            PresentationSource source = PresentationSource.FromVisual(AssociatedObject);
            if (source != null && source.CompositionTarget != null)
            {
                return source.CompositionTarget.TransformFromDevice;
            }

            return Media.Matrix.Identity;
        }

        private static Rect ToDipRect(System.Drawing.Rectangle bounds, Media.Matrix transform)
        {
            Point topLeft = transform.Transform(new Point(bounds.Left, bounds.Top));
            Point bottomRight = transform.Transform(new Point(bounds.Right, bounds.Bottom));
            return new Rect(topLeft, bottomRight);
        }

        private static int FindDisplayIndex(Rect rect, Rect[] workingAreas)
        {
            if (workingAreas == null || workingAreas.Length == 0)
            {
                return -1;
            }

            Point center = GetRectCenter(rect);
            for (int i = 0; i < workingAreas.Length; i++)
            {
                if (workingAreas[i].Contains(center))
                {
                    return i;
                }
            }

            double bestArea = 0.0;
            int bestIndex = 0;
            for (int i = 0; i < workingAreas.Length; i++)
            {
                Rect intersection = Rect.Intersect(rect, workingAreas[i]);
                double area = intersection == Rect.Empty ? 0.0 : intersection.Width * intersection.Height;
                if (area > bestArea)
                {
                    bestArea = area;
                    bestIndex = i;
                }
            }

            return bestIndex;
        }

        private static Rect ClampToArea(Rect rect, Rect area)
        {
            double width = Math.Min(rect.Width, area.Width);
            double height = Math.Min(rect.Height, area.Height);
            double left = Math.Max(area.Left, Math.Min(rect.Left, area.Right - width));
            double top = Math.Max(area.Top, Math.Min(rect.Top, area.Bottom - height));
            return new Rect(left, top, width, height);
        }

        private static Point GetRectCenter(Rect rect)
        {
            return new Point(rect.Left + rect.Width / 2.0, rect.Top + rect.Height / 2.0);
        }

        private static bool IsValidRect(Rect rect)
        {
            return !rect.IsEmpty &&
                   IsFinite(rect.Left) &&
                   IsFinite(rect.Top) &&
                   IsFinite(rect.Width) &&
                   IsFinite(rect.Height) &&
                   rect.Width > 0 &&
                   rect.Height > 0;
        }

        private static bool IsFinite(double value)
        {
            return !double.IsNaN(value) && !double.IsInfinity(value);
        }
    }
}
