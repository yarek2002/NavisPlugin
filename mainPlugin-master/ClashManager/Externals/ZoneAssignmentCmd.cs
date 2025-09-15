using System.Windows;
using ClashManager.Externals;
using ClashManager.ZoneAssignment.Views;

namespace ClashManager.Externals
{
	public class ZoneAssignmentCmd : IExternalCommand
	{
		public void Execute()
		{
			var window = new ZoneAssignmentView();
			window.WindowStyle = WindowStyle.SingleBorderWindow;
			window.ShowInTaskbar = false;
			window.Topmost = false;
			window.Show();
		}
	}
}
