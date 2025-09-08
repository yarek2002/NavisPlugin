using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CollisionClusterPlugin;

namespace ClashManager.Externals
{
    public class MakeClusterCmd : IExternalCommand
    {
        public void Execute()
        {
            Clustering makeClusters = new Clustering();
            makeClusters.MakeClusters();
        }
    }
}
