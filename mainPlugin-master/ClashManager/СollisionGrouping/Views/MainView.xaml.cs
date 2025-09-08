using Autodesk.Navisworks.Api.Clash;
using System;
using Autodesk.Navisworks.Api;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using CollisionGrouperPlugin.Models;


namespace CollisionGrouperPlugin.Views
{
    public partial class MainView : Window
    {
        public static Document doc;
        public static DocumentClash documentClash;
        public ClashTest selectedClashTest { get; set; }
        public string clashtestName { get; set; }
        public List<TestItem> TestItems { get; set; }

        public MainView()
        {
            doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            documentClash = doc.GetClash();
            InitializeComponent();
            TestItems = new List<TestItem>();
            LoadClashTests();
        }
        // Обработчик PreviewMouseDown для CheckBox
        private void CheckBox_PreviewMouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // Предотвращаем перехват фокуса

            var checkBox = sender as CheckBox;
            if (checkBox == null) return;

            // Переключаем состояние CheckBox
            checkBox.IsChecked = !checkBox.IsChecked;

            // Получаем все выделенные элементы
            var selectedItems = ItemsList.SelectedItems.Cast<TestItem>().ToList();

            // Устанавливаем галочку на всех выделенных элементах
            foreach (var item in selectedItems)
            {
                item.IsSelected = checkBox.IsChecked == true;
            }

            // Обновляем ListBox, чтобы изменения отобразились
            ItemsList.Items.Refresh();
        }

        private void OnOkButtonClick(object sender, RoutedEventArgs e)
        {
            List<TestItem> selectedItems = TestItems.Where(item => item.IsSelected).ToList();
            if (selectedItems.Count != 0)
            {
                List<ClashTest> selectedClashTests = selectedItems.Select(item => item.mainTest).ToList();
                CollisionFragmentGrouping collisiongrouper = new CollisionFragmentGrouping();
                collisiongrouper.makeGroups(selectedClashTests);
                this.Close();
            }
            else
            {
                MessageBox.Show("Не выбраны Клеш тесты");
            }
        }

        private void OnCancelButtonClick(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }

        // Вспомогательный метод для поиска родительского элемента
        private static T FindParent<T>(DependencyObject child) where T : DependencyObject
        {
            var parent = VisualTreeHelper.GetParent(child);
            while (parent != null && !(parent is T))
            {
                parent = VisualTreeHelper.GetParent(parent);
            }
            return parent as T;
        }

        private void LoadClashTests()
        {
            try
            {
                var grouper = new CollisionFragmentGrouping();
                var clashTests = grouper.GetClashTests().ToList();
                if (clashTests.Count == 0)
                {
                    MessageBox.Show("Clash-тесты не найдены.");
                    return;
                }

                TestItems = clashTests.Select(test => new TestItem
                {
                    Name = test.DisplayName ?? "Unnamed Test",
                    mainTest = test,
                    IsSelected = false
                }).ToList();

                ItemsList.ItemsSource = TestItems;
                ItemsList.Items.Refresh(); // Принудительное обновление UI
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка при загрузке clash-тестов: {ex.Message}\n{ex.StackTrace}");
            }
        }

    }
}