using CommunityToolkit.Mvvm.DependencyInjection;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SpsGui.Behaviors
{
    /// <summary>
    /// Insert a singleton ViewModel as DataContext to View as a behaviour automatically like <para><code>
    /// xmlns:behaviour="clr-namespace:AutoSushida.Behaviours"
    /// behaviour:IocHelper.AutoViewModel="{x:Type vm:MainWindowViewModel}"
    /// </code></para>
    /// </summary>
    public class IocHelper
    {
        public static Type GetAutoViewModel(DependencyObject obj) => (Type)obj.GetValue(AutoViewModelProperty);
        public static void SetAutoViewModel(DependencyObject obj, Type value) => obj.SetValue(AutoViewModelProperty, value);

        public static readonly DependencyProperty AutoViewModelProperty =
            DependencyProperty.RegisterAttached(
                "AutoViewModel",
                typeof(Type),
                typeof(IocHelper),
                new FrameworkPropertyMetadata(null,
                    FrameworkPropertyMetadataOptions.NotDataBindable,
                    AutoViewModelChanged));


        private static void AutoViewModelChanged(DependencyObject obj, DependencyPropertyChangedEventArgs args)
        {
            if (DesignerProperties.GetIsInDesignMode(obj))
                return;
            if (obj is FrameworkElement element && args.NewValue is Type type)
                element.DataContext = Ioc.Default.GetService(type);
        }
    }
}
