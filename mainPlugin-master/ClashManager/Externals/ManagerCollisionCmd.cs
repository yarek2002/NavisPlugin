using System.Windows;
using ClashManager.Externals;
using ClashManager.ManagerCollision.Views;

namespace ClashManager.Externals
{
	public class ManagerCollisionCmd : IExternalCommand
	{
		public void Execute()
		{
			var window = new ManagerCollisionView();
			window.WindowStyle = WindowStyle.SingleBorderWindow;
window.ShowInTaskbar = false;
window.Topmost = false;
			window.Show();
		}
	}
}