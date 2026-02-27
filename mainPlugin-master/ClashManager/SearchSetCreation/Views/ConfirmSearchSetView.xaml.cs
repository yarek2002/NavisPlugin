using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using ClashManager.SearchSetCreation.Models;

namespace ClashManager.SearchSetCreation.Views
{
    public partial class ConfirmSearchSetView : Window
    {
        private static readonly IReadOnlyList<string> OperatorOptions = new[]
        {
            "=",
            "!=",
            ">",
            ">=",
            "<",
            "<=",
            "содержит",
            "не содержит"
        };

        public ObservableCollection<SearchSetConditionItem> Conditions { get; }

        public ConfirmSearchSetView(IEnumerable<SearchSetConditionItem> conditions)
        {
            InitializeComponent();

            Conditions = new ObservableCollection<SearchSetConditionItem>(conditions ?? Enumerable.Empty<SearchSetConditionItem>());
            ConditionsDataGrid.ItemsSource = Conditions;

            // Проставляем itemsource для ComboBox колонки (по индексу, т.к. XAML без x:Reference проще)
            if (ConditionsDataGrid.Columns.OfType<DataGridComboBoxColumn>().FirstOrDefault() is DataGridComboBoxColumn opCol)
            {
                opCol.ItemsSource = OperatorOptions;
            }

            // Если где-то оператор не задан — по умолчанию "="
            foreach (var c in Conditions)
            {
                if (string.IsNullOrWhiteSpace(c.Operator))
                    c.Operator = "=";
            }
        }

        private void CreateButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

