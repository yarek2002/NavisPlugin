using System;
using System.Windows;
using Autodesk.Navisworks.Api;
using ClashManager.SearchSetCreation.Views;

namespace ClashManager.Externals
{
    public class CreateSearchSetCmd : IExternalCommand
    {
        public void Execute()
        {
            try
            {
                Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
                ModelItemCollection selectedItems = doc.CurrentSelection.SelectedItems;

                if (selectedItems.Count == 0)
                {
                    MessageBox.Show("Пожалуйста, выберите элемент в модели.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (selectedItems.Count > 1)
                {
                    MessageBox.Show("Пожалуйста, выберите только один элемент.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                ModelItem selectedItem = selectedItems[0];

                // Создаем окно и передаем выбранный элемент
                CreateSearchSetView window = new CreateSearchSetView(selectedItem);
                window.WindowStyle = WindowStyle.SingleBorderWindow;
                window.ShowInTaskbar = false;
                window.Topmost = false;
                window.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при открытии окна создания поискового набора: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}
