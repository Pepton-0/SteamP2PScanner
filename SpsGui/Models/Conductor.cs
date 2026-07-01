using SpsLogic;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace SpsGui.Models
{
    public interface IConductor
    {

    }

    public class Conductor : IConductor
    {
        public Conductor()
        {
            string culture = CultureInfo.CurrentCulture.Name;
            try
            {
                string local = CultureInfo.CreateSpecificCulture(culture).Name;
                var localDictionary = new ResourceDictionary()
                {
                    Source = new Uri(@"Resources/StringResource." + local + @".xaml", UriKind.Relative)
                };
                App.Current.Resources.MergedDictionaries.Add(localDictionary);
            }
            catch (CultureNotFoundException)
            { // for CultureInfo.CreateSpecificCulture
                Logger.Log(culture + " has no info so failed to load the localization");
            }
            catch
            {
                Logger.Log($"Failed to find local dictionary for {culture} so use default en-us.");
            }
        }
    }
}
