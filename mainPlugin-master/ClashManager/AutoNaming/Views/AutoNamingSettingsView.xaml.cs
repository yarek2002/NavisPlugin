using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using ClashManager.AutoNaming;

namespace ClashManager.AutoNaming.Views
{
    public partial class AutoNamingSettingsView : Window, INotifyPropertyChanged
    {
        public TestAutoNamingSettings AppliedSettings { get; private set; }
        public List<System.Guid> SelectedTestGuids { get; set; } = new List<System.Guid>();

        // Observable collection for dynamic parameters
        public ObservableCollection<ParameterItem> Parameters { get; set; } = new ObservableCollection<ParameterItem>();

        // Separator value for binding
        private string _separatorText = " | ";
        public string SeparatorText
        {
            get => _separatorText;
            set
            {
                if (_separatorText != value)
                {
                    _separatorText = value;
                    OnPropertyChanged();
                }
            }
        }

        // Complete custom naming mode
        private bool _useCompleteCustomNaming;
        public bool UseCompleteCustomNaming
        {
            get => _useCompleteCustomNaming;
            set
            {
                if (_useCompleteCustomNaming != value)
                {
                    _useCompleteCustomNaming = value;
                    OnPropertyChanged();
                }
            }
        }

        /// <summary>
        /// Если true, перед авто-наименованием каждая коллизия в группе будет вынесена в отдельную группу.
        /// </summary>
        private bool _separateByTwoClash;
        public bool SeparateByTwoClash
        {
            get => _separateByTwoClash;
            set
            {
                if (_separateByTwoClash != value)
                {
                    _separateByTwoClash = value;
                    OnPropertyChanged();
                }
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public AutoNamingSettingsView()
        {
            InitializeComponent();

            // Set DataContext for binding
            DataContext = this;

            // Bind the ItemsControl to our parameters collection
            ParametersItemsControl.ItemsSource = Parameters;
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // К моменту Loaded свойства (включая SelectedTestGuids) уже заданы из вызывающего кода,
            // поэтому можно корректно загрузить настройки из JSON.
            LoadExistingSettings();
        }

        private void LoadExistingSettings()
        {
            // Try to load settings for selected tests
            if (SelectedTestGuids.Count > 0)
            {
                var allSettings = AutoNamingSettings.LoadFromFile();

                // Try to find settings from any of the selected tests
                TestAutoNamingSettings testSettings = null;
                foreach (var testGuid in SelectedTestGuids)
                {
                    testSettings = allSettings.GetTestSettings(testGuid);
                    if (testSettings != null)
                        break; // Use the first test that has settings
                }

                if (testSettings != null)
                {
                    // Copy existing parameters
                    Parameters.Clear();
                    foreach (var param in testSettings.Parameters)
                    {
                        Parameters.Add(new ParameterItem(param.IsEnabled, param.ParameterName)
                        {
                            ParameterSeparator = param.ParameterSeparator ?? ","
                        });
                    }

                    // Set separator and complete custom naming mode
                    SeparatorText = testSettings.Separator ?? " | ";
                    UseCompleteCustomNaming = testSettings.UseCompleteCustomNaming;
                    SeparateByTwoClash = testSettings.SeparateByTwoClash;
                }
                else
                {
                    // Нет сохранённых настроек для выбранных тестов (или файл ещё не создан) —
                    // показываем параметры по умолчанию
                    Parameters.Clear();
                    AddDefaultCompleteNamingParameters();
                    SeparatorText = " | ";
                    UseCompleteCustomNaming = false;
                    SeparateByTwoClash = false;
                }
            }
            else
            {
                // Нет выбранных тестов — также показываем параметры по умолчанию
                Parameters.Clear();
                AddDefaultCompleteNamingParameters();
                SeparatorText = " | ";
                UseCompleteCustomNaming = false;
                SeparateByTwoClash = false;
            }
        }

        private void AddParameterButton_Click(object sender, RoutedEventArgs e)
        {
            // Add a new parameter directly to the list
            Parameters.Add(new ParameterItem(false, string.Empty));
        }

        private void SetParameterSettingsButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null && button.Tag is ParameterItem parameterItem)
            {
                // Open parameter settings dialog
                var dialog = new ParameterSettingsDialog(parameterItem.ParameterSeparator);
                dialog.Owner = this;

                if (dialog.ShowDialog() == true)
                {
                    // Update parameter separator
                    parameterItem.ParameterSeparator = dialog.ParameterSeparator;
                }
            }
        }

        private void RemoveParameterButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null && button.Tag is ParameterItem parameterItem)
            {
                // Don't allow removing the last parameter
                if (Parameters.Count > 1)
                {
                    Parameters.Remove(parameterItem);
                }
                else
                {
                    MessageBox.Show("Нельзя удалить последний параметр!", "Предупреждение",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void MoveParameterUpButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null && button.Tag is ParameterItem parameterItem)
            {
                int currentIndex = Parameters.IndexOf(parameterItem);
                if (currentIndex > 0)
                {
                    // Перемещаем параметр вверх
                    Parameters.Move(currentIndex, currentIndex - 1);
                }
            }
        }

        private void MoveParameterDownButton_Click(object sender, RoutedEventArgs e)
        {
            var button = sender as Button;
            if (button != null && button.Tag is ParameterItem parameterItem)
            {
                int currentIndex = Parameters.IndexOf(parameterItem);
                if (currentIndex < Parameters.Count - 1)
                {
                    // Перемещаем параметр вниз
                    Parameters.Move(currentIndex, currentIndex + 1);
                }
            }
        }

        private void ApplyButton_Click(object sender, RoutedEventArgs e)
        {
            // Create settings object from current UI state
            var settings = new TestAutoNamingSettings
            {
                Parameters = new List<ParameterItem>(Parameters),
                Separator = SeparatorText?.Trim() ?? " | ",
                UseCompleteCustomNaming = UseCompleteCustomNaming,
                SeparateByTwoClash = SeparateByTwoClash
            };

            // Сохраняем примененные настройки
            AppliedSettings = settings;

            // Загружаем существующие настройки и добавляем/обновляем настройки для выбранных тестов
            var allSettings = AutoNamingSettings.LoadFromFile();
            allSettings.SetTestSettings(SelectedTestGuids, settings);
            allSettings.SaveToFile();

            MessageBox.Show($"Настройки сохранены для {SelectedTestGuids.Count} тестов!", "Информация", MessageBoxButton.OK, MessageBoxImage.Information);
            this.DialogResult = true;
            this.Close();
        }

        private void CompleteCustomNamingToggle_Checked(object sender, RoutedEventArgs e)
        {
            // При включении режима полного наименования добавляем стандартные параметры по умолчанию,
            // только если список параметров пустой или содержит только пустые параметры
            if (Parameters.Count == 0 || Parameters.All(p => string.IsNullOrWhiteSpace(p.ParameterName)))
            {
                AddDefaultCompleteNamingParameters();
            }
            else
            {
                // Если уже есть параметры, показываем диалог с вопросом
                var result = MessageBox.Show(
                    "Обнаружены существующие параметры. Заменить их стандартными для режима полного наименования?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    AddDefaultCompleteNamingParameters();
                }
            }
        }

        private void CompleteCustomNamingToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            // При отключении режима полного наименования оставляем параметры как есть
        }

        private void AddDefaultCompleteNamingParameters()
        {
            // Очищаем существующие параметры
            Parameters.Clear();

            // Добавляем стандартные параметры для полного наименования коллизий,
            // соответствующие тому, что используется в базовом режиме
            // 1. Название модели (будет взято из ModelItem)
            Parameters.Add(new ParameterItem(true, "Название nwc") { ParameterSeparator = " " });

            // 2. ID элементов
            Parameters.Add(new ParameterItem(true, "Id") { ParameterSeparator = "," });

            // 3. GUID группы коллизий
            Parameters.Add(new ParameterItem(true, "GUID группы") { ParameterSeparator = "" });
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }


}
