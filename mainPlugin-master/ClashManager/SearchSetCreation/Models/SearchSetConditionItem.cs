using System.ComponentModel;

namespace ClashManager.SearchSetCreation.Models
{
    public class SearchSetConditionItem : INotifyPropertyChanged
    {
        private string _operator;
        private string _value;

        public string Category { get; set; }
        public string PropertyName { get; set; }

        /// <summary>
        /// Оператор сравнения: "=", "!=", ">", ">=", "<", "<=", "содержит", "не содержит"
        /// </summary>
        public string Operator
        {
            get => _operator;
            set
            {
                _operator = value;
                OnPropertyChanged(nameof(Operator));
            }
        }

        public string Value
        {
            get => _value;
            set
            {
                _value = value;
                OnPropertyChanged(nameof(Value));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}

