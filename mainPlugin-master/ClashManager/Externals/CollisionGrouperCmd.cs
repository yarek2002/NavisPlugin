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
    public class CollisionGrouperCmd : IExternalCommand
    {
        public void Execute()
        {
            var view = new MainView();
            view.ShowDialog();
        }
    }
}
