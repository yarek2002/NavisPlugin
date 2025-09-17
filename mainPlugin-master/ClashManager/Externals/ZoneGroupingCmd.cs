using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using CollisionGrouperPlugin;
using CollisionGrouperPlugin.Views;

namespace ClashManager.Externals
{
    public class ZoneGroupingCmd : IExternalCommand
    {
        public void Execute()
        {
            // Окно подтверждения перед запуском
            MessageBoxResult result = MessageBox.Show("Вы хотите запустить авто-группировку по зонам для всех тестов?", "Подтверждение запуска", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var zoneGrouping = new ZoneGrouping();
                zoneGrouping.ProcessAllTests();
            }
        }
    }
}
