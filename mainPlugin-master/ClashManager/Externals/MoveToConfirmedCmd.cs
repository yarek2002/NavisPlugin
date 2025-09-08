using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Navigation;
using MoveToConfirmedPlugin;
using System.Windows; // Добавлено для MessageBox

namespace ClashManager.Externals
{
    public class MoveToConfirmedCmd : IExternalCommand
    {
        public void Execute()
        {
            // Окно подтверждения перед запуском
            MessageBoxResult result = MessageBox.Show("Вы хотите переместить в Confirmed?", "Подтверждение запуска", MessageBoxButton.YesNo, MessageBoxImage.Question);
            if (result == MessageBoxResult.Yes)
            {
                MoveToConfirmed moveToConfirmed = new MoveToConfirmed();
                moveToConfirmed.Execute();
            }
        }
    }
}