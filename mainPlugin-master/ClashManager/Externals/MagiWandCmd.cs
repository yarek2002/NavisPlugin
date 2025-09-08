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
    public class MagicWandCmd : IExternalCommand
    {
        public void Execute()
        {
            // Окно подтверждения перед запуском
            MessageBoxResult result = MessageBox.Show("Вы хотите запустить обработку всех тестов?", "Подтверждение запуска", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                var magicWand = new MagicWand();
                magicWand.ProcessAllTests();
            }
        }
    }
}