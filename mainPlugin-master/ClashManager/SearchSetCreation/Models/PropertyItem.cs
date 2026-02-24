using System.ComponentModel;

namespace ClashManager.SearchSetCreation.Models
{
    public class PropertyItem : INotifyPropertyChanged
    {
        private bool _isPropertySelected;

        public string Category { get; set; }
        public string PropertyName { get; set; }
        public string PropertyValue { get; set; }
        public object OriginalValue { get; set; }

        /// <summary>Выбрана ли вся строка (категория, свойство, значение).</summary>
        public bool IsPropertySelected
        {
            get => _isPropertySelected;
            set
            {
                _isPropertySelected = value;
                OnPropertyChanged(nameof(IsPropertySelected));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
