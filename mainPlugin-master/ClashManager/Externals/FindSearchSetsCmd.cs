using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CollisionGrouperPlugin; // Для FindSearchSets (WPF-окно)
using Autodesk.Navisworks.Api; // Для Document, ModelItem и т.д.
using System.Windows; // Для MessageBox

namespace ClashManager.Externals
{
    public class FindSearchSetsCmd : IExternalCommand
    {
        public void Execute()
        {
            Document doc = Autodesk.Navisworks.Api.Application.ActiveDocument; // Уточнили Application для избежания неоднозначности
            ModelItemCollection selectedItems = doc.CurrentSelection.SelectedItems;

            if (selectedItems.Count != 2)
            {
                MessageBox.Show("Пожалуйста, выберите ровно два элемента в модели.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            ModelItem elem1 = selectedItems[0];
            ModelItem elem2 = selectedItems[1];

            // Создаем окно и передаем данные
            FindSearchSets window = new FindSearchSets(elem1, elem2);
            window.ShowDialog();
        }
    }
}