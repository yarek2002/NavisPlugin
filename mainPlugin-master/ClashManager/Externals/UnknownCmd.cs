using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace ClashManager.Externals
{
    public class UnknownCmd : IExternalCommand
    {
        public void Execute()
        {
            int num = (int)MessageBox.Show("Вызвана неизвестная команда!");
        }
    }
}
