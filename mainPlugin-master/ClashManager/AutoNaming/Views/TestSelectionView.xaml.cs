using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Autodesk.Navisworks.Api;
using Autodesk.Navisworks.Api.Clash;

namespace ClashManager.AutoNaming.Views
{
    /// <summary>
    /// Класс для элементов списка тестов коллизий с выбором
    /// </summary>
    public class TestSelectionItem : INotifyPropertyChanged
    {
        private bool _isSelected;

        public ClashTest Test { get; set; }
        public string DisplayName { get; set; }
        public Guid Guid { get; set; }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                if (_isSelected != value)
                {
                    _isSelected = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public partial class TestSelectionView : Window
    {
        private Document _doc;
        private DocumentClash _documentClash;
        private List<TestSelectionItem> _testItems;
        private int _lastTestClickIndex = -1;
        private bool _suppressCheckboxHandlers = false;

        public List<Guid> SelectedTestGuids { get; private set; }

        public TestSelectionView()
        {
            InitializeComponent();
            _doc = Autodesk.Navisworks.Api.Application.ActiveDocument;
            _documentClash = _doc.GetClash();
            SelectedTestGuids = new List<Guid>();
            LoadTests();
        }

        private void LoadTests()
        {
            var tests = _documentClash?.TestsData?.Tests?.OfType<ClashTest>()?.ToList() ?? Enumerable.Empty<ClashTest>().ToList();

            _testItems = tests.Select(t => new TestSelectionItem
            {
                Test = t,
                DisplayName = t.DisplayName,
                Guid = t.Guid,
                IsSelected = false // По умолчанию ничего не выбрано
            }).ToList();

            // Подписываемся на изменения состояния каждого элемента
            foreach (var testItem in _testItems)
            {
                testItem.PropertyChanged += TestItem_PropertyChanged;
            }

            TestsListBox.ItemsSource = _testItems;
        }

        private void TestItem_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName == nameof(TestSelectionItem.IsSelected))
            {
                // Обновляем список выбранных GUID
                UpdateSelectedGuids();
            }
        }

        private void UpdateSelectedGuids()
        {
            SelectedTestGuids = _testItems.Where(x => x.IsSelected).Select(x => x.Guid).ToList();
        }

        private void TestsListBox_ScrollChanged(object sender, System.Windows.Controls.ScrollChangedEventArgs e)
        {
            // Принудительно синхронизируем состояние при прокрутке
            if (e.VerticalChange != 0)
            {
                ForceSyncCheckboxStates();
            }
        }

        private void ForceSyncCheckboxStates()
        {
            // Временно отписываемся от событий, чтобы избежать циклических вызовов
            foreach (var testItem in _testItems)
            {
                testItem.PropertyChanged -= TestItem_PropertyChanged;
            }

            // Синхронизируем состояние
            foreach (var testItem in _testItems)
            {
                // Состояние уже синхронизировано через binding
            }

            // Подписываемся обратно
            foreach (var testItem in _testItems)
            {
                testItem.PropertyChanged += TestItem_PropertyChanged;
            }
        }

        private void TestCheckBox_Click(object sender, RoutedEventArgs e)
        {
            var cb = sender as CheckBox;
            if (cb == null) return;
            if (_suppressCheckboxHandlers) return;

            var item = cb.DataContext as TestSelectionItem;
            if (item == null) return;

            // Предотвращаем повторную обработку события
            e.Handled = true;

            int currentIndex = TestsListBox.Items.IndexOf(item);
            bool isShift = (System.Windows.Input.Keyboard.Modifiers & System.Windows.Input.ModifierKeys.Shift) == System.Windows.Input.ModifierKeys.Shift;
            bool targetChecked = cb.IsChecked == true;

            // Если выделено несколько элементов в списке, применяем действие ко всем выделенным
            if (TestsListBox.SelectedItems.Count > 1 && TestsListBox.SelectedItems.Contains(item))
            {
                _suppressCheckboxHandlers = true;
                try
                {
                    foreach (var selectedItem in TestsListBox.SelectedItems)
                    {
                        if (selectedItem is TestSelectionItem selectedTestItem)
                        {
                            selectedTestItem.IsSelected = targetChecked;
                        }
                    }
                }
                finally { _suppressCheckboxHandlers = false; }
            }
            else if (isShift && _lastTestClickIndex >= 0)
            {
                // Shift-выбор: применяем действие ко всем элементам между последним и текущим кликом
                int from = Math.Min(_lastTestClickIndex, currentIndex);
                int to = Math.Max(_lastTestClickIndex, currentIndex);

                _suppressCheckboxHandlers = true;
                try
                {
                    for (int i = from; i <= to; i++)
                    {
                        if (TestsListBox.Items[i] is TestSelectionItem shiftItem)
                        {
                            shiftItem.IsSelected = targetChecked;
                        }
                    }
                }
                finally { _suppressCheckboxHandlers = false; }
            }
            else
            {
                // Одиночный клик - убеждаемся, что состояние синхронизировано
                item.IsSelected = targetChecked;
            }

            _lastTestClickIndex = currentIndex;
        }

        private void SelectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _testItems)
            {
                item.IsSelected = true;
            }
        }

        private void DeselectAllButton_Click(object sender, RoutedEventArgs e)
        {
            foreach (var item in _testItems)
            {
                item.IsSelected = false;
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            if (SelectedTestGuids.Count == 0)
            {
                MessageBox.Show("Выберите хотя бы один тест!", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            this.DialogResult = true;
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            SelectedTestGuids.Clear();
            this.DialogResult = false;
            this.Close();
        }
    }
}
