using Microsoft.Win32;
using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Input;
using ClashManager.MakeSets.ViewModel;


namespace ClashManager.MakeSets.View
{
    public partial class MakeSetsView : Window
    {
        public MakeSetsView()
        {
            InitializeComponent();
            DataContext = new MakeSetsViewModel();
        }
    }
}