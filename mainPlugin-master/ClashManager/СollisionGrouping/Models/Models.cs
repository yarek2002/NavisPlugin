using Autodesk.Navisworks.Api.Clash;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CollisionGrouperPlugin.Models
{
    public class TestItem : INotifyPropertyChanged
    {
        private string _name;
        private ClashTest _mainTest;
        private bool _isSelected;

        public string Name
        {
            get => _name;
            set
            {
                _name = value;
                OnPropertyChanged(nameof(Name));
            }
        }

        public ClashTest mainTest
        {
            get => _mainTest;
            set
            {
                _mainTest = value;
                OnPropertyChanged(nameof(mainTest));
            }
        }

        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged(nameof(IsSelected));
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected void OnPropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    public class resultates
    {
        public List<ClashResultGroup> grups { get; set; }
        public List<ClashResult> results { get; set; }
        public resultates()
        {
            grups = new List<ClashResultGroup>();
            results = new List<ClashResult>();
        }

    }
}
