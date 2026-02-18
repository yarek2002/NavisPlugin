using System.ComponentModel;

namespace ClashManager.SearchSetCreation.Models
{
    public class PropertyItem : INotifyPropertyChanged
    {
        private bool _isPropertySelected;
        private bool _isValueSelected;

        public string Category { get; set; }
        public string PropertyName { get; set; }
        public string PropertyValue { get; set; }
        public object OriginalValue { get; set; }

        public bool IsPropertySelected
        {
            get => _isPropertySelected;
            set
            {
                _isPropertySelected = value;
                OnPropertyChanged(nameof(IsPropertySelected));
            }
        }

        public bool IsValueSelected
        {
            get => _isValueSelected;
            set
            {
                _isValueSelected = value;
                OnPropertyChanged(nameof(IsValueSelected));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
