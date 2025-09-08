using ClashManager.MakeSets.View;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ClashManager.Externals
{
    public class MakeSetsCmd : IExternalCommand
    {
        public void Execute()
        {
            MakeSetsView makeSets = new MakeSetsView();
            makeSets.ShowDialog();
        }
    }
}
