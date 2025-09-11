using System.Windows;
using ClashManager.Externals;
using ClashManager.AutoNaming.Views;

namespace ClashManager.Externals
{
	public class AutoNamingCmd : IExternalCommand
	{
		public void Execute()
		{
			var window = new AutoNamingView();
			window.WindowStyle = WindowStyle.SingleBorderWindow;
			window.ShowInTaskbar = false;
			window.Topmost = false;
			window.Show();
		}
	}
}
