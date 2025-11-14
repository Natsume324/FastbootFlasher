using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Text;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;


namespace FastbootFlasher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<Partition> Partitions { get; set; }
        public MainWindow()
        {
            InitializeComponent();
            SwitchLanguage("zh-cn");
            Partitions = new ObservableCollection<Partition>();
            DataContext = this;
        }
       
       
        private void SwitchLanguage(string culture)
        {
            Application.Current.Resources.MergedDictionaries.Clear();
            Application.Current.Resources.MergedDictionaries.Add(
                new ResourceDictionary { Source = new Uri($"Resources/Lang-{culture}.xaml", UriKind.Relative) });
        }

        private void LangComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // 获取选中的 ComboBoxItem
            var selectedItem = LangComboBox.SelectedItem as ComboBoxItem;

            if (selectedItem != null)
            {
                // 获取 Tag 值
                var tagValue = selectedItem.Tag;
                SwitchLanguage($"{tagValue}");
            }
            
        }

        private void Load_Btn_Click(object sender, RoutedEventArgs e)
        {
            Partitions.Clear();
            FilePath_Box.Clear();
            Log_Box.Clear();
            Microsoft.Win32.OpenFileDialog openFileDialog = new()
            {
                Filter = (string)FindResource("Firmware") + "|*.*",
                Multiselect = true,
                Title = (string)FindResource("SelectFile")
            };
            if(openFileDialog.ShowDialog() == true)
            {
                
                foreach (var FileName in openFileDialog.FileNames)
                {
                    FilePath_Box.Text+= FileName + "\n";
                    if (FileName.EndsWith(".bat"))
                    {
                        var parsed = BatFile.ParseBat(FileName); 
                        if (parsed != null)
                        {
                            foreach (var p in parsed)
                                Partitions.Add(p);
                        }
                    }
                    else if(FileName.EndsWith(".bin"))
                    {
                        if(UpdateBin.IsUpdateBin(FileName))
                        {
                            var parsed = UpdateBin.ParseUpdateBin(FileName);
                            if (parsed != null)
                            {
                                foreach (var p in parsed)
                                    Partitions.Add(p);
                            }
                        }
                        else if(PayloadBin.IsPayloadBin(FileName))
                        {
                            var parsed = PayloadBin.ParsePayloadBin(FileName); ;
                            if (parsed != null)
                            {
                                foreach (var p in parsed)
                                    Partitions.Add(p);
                            }         
                        }
                    }
                    else if (FileName.EndsWith(".APP") || FileName.EndsWith(".app"))
                    {
                        UpdateApp.ParseUpdateApp(FileName);
                    }
                    else
                    {
                        ImageFile.ParseImage(FileName);
                    }
                }
            }
        }
    }
    public class Partition : INotifyPropertyChanged
    {
        private string _name;

        public int Index { get; set; }
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }
        public string Size { get; set; }
        public string SourceFile { get; set; }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}