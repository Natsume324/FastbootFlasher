using FastbootCS;
using Microsoft.Win32;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using static FastbootCS.Fastboot;


namespace FastbootFlasher
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        public ObservableCollection<Partition> Partitions { get; set; }
        public FirmwareType CurrentFirmwareType { get; set; }
        public MainWindow()
        {
            InitializeComponent();
            SwitchLanguage("en-us");
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
                var FileNames = openFileDialog.FileNames;
                for (int i=0;i< openFileDialog.FileNames.Length; i++)
                {
                    Log_Box.Text+=(string)FindResource("LoadFile") +FileNames[i] + "\n\r";
                    Log_Box.ScrollToEnd();
                    FilePath_Box.Text += FileNames[i] + "\n";
                    if (FileNames[i].EndsWith(".bat"))
                    {
                        var parsed = BatFile.ParseBat(FileNames[i]);
                        if (parsed != null)
                        {
                            foreach (var p in parsed)
                                Partitions.Add(p);
                        }
                        CurrentFirmwareType = FirmwareType.BatFile;
                        break;
                    }
                    else if (FileNames[i].EndsWith(".bin")&&(UpdateBin.IsUpdateBin(FileNames[i])|| PayloadBin.IsPayloadBin(FileNames[i])))
                    {
                        if (UpdateBin.IsUpdateBin(FileNames[i]))
                        {
                            var parsed = UpdateBin.ParseUpdateBin(FileNames[i]);
                            if (parsed != null)
                            {
                                foreach (var p in parsed)
                                    Partitions.Add(p);
                            }
                            CurrentFirmwareType = FirmwareType.UpdateBin;
                            break;
                        }
                        else if (PayloadBin.IsPayloadBin(FileNames[i]))
                        {
                            var parsed = PayloadBin.ParsePayloadBin(FileNames[i]); ;
                            if (parsed != null)
                            {
                                foreach (var p in parsed)
                                    Partitions.Add(p);
                            }
                            CurrentFirmwareType = FirmwareType.PayloadBin;
                            break;
                        }
                    }
                    else if (FileNames[i].EndsWith(".APP") || FileNames[i].EndsWith(".app"))
                    {
                        var parsed = UpdateApp.ParseUpdateApp(FileNames[i]);
                        if (parsed != null)
                        {
                            foreach (var p in parsed)
                                Partitions.Add(p);
                        }
                        CurrentFirmwareType = FirmwareType.UpdateApp;
                    }
                    else
                    {
                        Partitions.Add(ImageFile.ParseImage(FileNames[i],i));    
                        CurrentFirmwareType = FirmwareType.Image;
                    }
                }
            }
        }

        private async void Flash_Btn_Click(object sender, RoutedEventArgs e)
        {
            var devices = Fastboot.GetDevices();
            if (Log_Box.Text == "")
            {
                MessageBox.Show((string)FindResource("NoFileLoaded"), (string)FindResource("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            else if (PartitionDataGrid.SelectedItems.Count == 0)
            {
                MessageBox.Show((string)FindResource("NoPartitionSelected"), (string)FindResource("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            else if (devices.Length == 0 || devices.Length > 1)
            {
                MessageBox.Show((string)FindResource("DeviceError"), (string)FindResource("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Log_Box.Text += (string)FindResource("DetectDevice") + devices[0] + "\n";
            var fb = new Fastboot(devices[0]);
            Log_Box.Text += (string)FindResource("ConnectDevice") + "\n";
            fb.Connect();
            Log_Box.Text += (string)FindResource("ConnectSuccess") + "\n\r";
            var progress = new Progress<double>(value =>
            {
                progressBar.Value = value;
                progressText.Text = $"{value:F1}%";
                if (value >= 100)
                {
                    progressText.Text = "100%";
                }
            });
            var sum = PartitionDataGrid.SelectedItems.Count;
            int okay = 0;
            int fail = 0;
            string[] failParts = [];
            DisableControl();
            switch (CurrentFirmwareType)
            {
                case FirmwareType.BatFile:
                    foreach (var item in PartitionDataGrid.SelectedItems.Cast<Partition>())
                    {
                        Log_Box.Text += string.Format((string)FindResource("FlashingPartition"), item.Name) + "   ";
                        Fastboot.Response respone = await fb.FlashPartition(item.Name, item.SourceFile, progress); 
                        if (respone.Status == Fastboot.Status.OKAY)
                        {
                            Log_Box.Text += (string)FindResource("Successful") + "\n";
                            okay++;
                        }
                        else
                        {
                            Log_Box.Text += (string)FindResource("Failed") + "\n";
                            fail++;
                            failParts.Append(item.Name);
                        }
                        Log_Box.ScrollToEnd();
                    }
                    break;
                case FirmwareType.UpdateBin:
                    foreach (var item in PartitionDataGrid.SelectedItems.Cast<Partition>())
                    {
                        Log_Box.Text += string.Format((string)FindResource("FlashingPartition"), item.Name) + "   ";
                        Fastboot.Response respone = await fb.FlashPartition(item.Name, item.SourceFile, progress);
                        if (respone.Status == Fastboot.Status.OKAY)
                        {
                            Log_Box.Text += (string)FindResource("Successful") + "\n";
                            okay++;
                        }
                        else
                        {
                            Log_Box.Text += (string)FindResource("Failed") + "\n";
                            fail++;
                            failParts.Append(item.Name);
                        }
                        Log_Box.ScrollToEnd();
                    }
                    break;
                case FirmwareType.PayloadBin:
                    await PayloadBinExtractPart(progress);
                    foreach (var item in PartitionDataGrid.SelectedItems.Cast<Partition>())
                    {
                        Log_Box.Text += string.Format((string)FindResource("FlashingPartition"), item.Name) + "   ";
                        Fastboot.Response respone = await fb.FlashPartition(item.Name, @$".\images\{item.Name}.img", progress);
                        if (respone.Status == Fastboot.Status.OKAY)
                        {
                            Log_Box.Text += (string)FindResource("Successful") + "\n";
                            okay++;
                        }
                        else
                        {
                            Log_Box.Text += (string)FindResource("Failed") + "\n";
                            fail++;
                            failParts.Append(item.Name);
                        }
                        Log_Box.ScrollToEnd();
                    }
                    break;
                //case FirmwareType.UpdateApp:
                //    UpdateApp.FlashUpdateApp(fb, Partitions, Log_Box, FindResource);
                //    break;
                //case FirmwareType.Image:
                //    ImageFile.FlashImageFiles(fb, Partitions, Log_Box, FindResource);
                //    break;
                default:
                    MessageBox.Show((string)FindResource("UnknownFirmwareType"), (string)FindResource("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                    break;
            }
            Log_Box.Text += string.Format((string)FindResource("FlashStatistic"), sum, okay, fail) + "\n";
            if (failParts.Length > 0)
            {
                Log_Box.Text += (string)FindResource("FlashFailPart") + "\n";
                foreach (var failPart in failParts)
                {
                    Log_Box.Text += failPart + "\n";
                    Log_Box.ScrollToEnd();
                }
            }
            Log_Box.Text += (string)FindResource("Disconnect") + "\n\r";
            fb.Disconnect();
            EnableControl();
            Log_Box.ScrollToEnd();

        }

        private async void ExtractSelectedPart_Click(object sender, RoutedEventArgs e)
        {
            if (PartitionDataGrid.SelectedItems.Count == 0)
            {
                MessageBox.Show((string)FindResource("NoPartitionSelected"), (string)FindResource("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }
            Directory.CreateDirectory("images");
            var progress = new Progress<double>(value =>
            {
                progressBar.Value = value;
                progressText.Text = $"{value:F1}%";
                if (value >= 100)
                {
                    progressText.Text = "100%";
                }
            });
            DisableControl();
            switch (CurrentFirmwareType)
            {
                case FirmwareType.UpdateBin:
                    await UpdateBinExtractPart(progress);
                    break;
                case FirmwareType.PayloadBin:
                    await PayloadBinExtractPart(progress);
                    break;
                case FirmwareType.UpdateApp:
                    await UpdateAppExtractPart(progress);
                    break;
                default: 
                    break;
            }
            
            EnableControl();



        }
        private async Task PayloadBinExtractPart(Progress<double> progress)
        {
            bool result;
            foreach (var item in PartitionDataGrid.SelectedItems.Cast<Partition>())
            {
                Log_Box.Text += string.Format((string)FindResource("ExtractingPartition"), item.Name) + "   ";
                result = await PayloadBin.ExtractPartitionImage(item.Name, item.SourceFile, progress);
                if (result)
                {
                    Log_Box.Text += (string)FindResource("Successful") + "\n";
                }
                else
                {
                    Log_Box.Text += (string)FindResource("Failed") + "\n";
                }
                Log_Box.ScrollToEnd();
            }
            Log_Box.Text += "\n" + (string)FindResource("ExtractFinished")+"\n\r";
            Log_Box.ScrollToEnd();
        }
        private async Task UpdateBinExtractPart(Progress<double> progress)
        {
            bool result;
            foreach (var item in PartitionDataGrid.SelectedItems.Cast<Partition>())
            {
                Log_Box.Text += string.Format((string)FindResource("ExtractingPartition"), item.Name) + "   ";
                result = await UpdateBin.ExtractPartitionImage(item.Name, item.SourceFile, progress);
                if (result)
                {
                    Log_Box.Text += (string)FindResource("Successful") + "\n";
                }
                else
                {
                    Log_Box.Text += (string)FindResource("Failed") + "\n";
                }
                Log_Box.ScrollToEnd();
            }
            Log_Box.Text += "\n" + (string)FindResource("ExtractFinished")+"\n\r";
            Log_Box.ScrollToEnd();
        }
        private async Task UpdateAppExtractPart(Progress<double> progress)
        {
            bool result;
            foreach (var item in PartitionDataGrid.SelectedItems.Cast<Partition>())
            {
                Log_Box.Text += string.Format((string)FindResource("ExtractingPartition"), item.Name) + "   ";
                result = await UpdateApp.ExtractPartitionImage(item.Name, item.SourceFile, progress);
                if (result)
                {
                    Log_Box.Text += (string)FindResource("Successful") + "\n";
                }
                else
                {
                    Log_Box.Text += (string)FindResource("Failed") + "\n";
                }
                Log_Box.ScrollToEnd();
            }
            Log_Box.Text += "\n" + (string)FindResource("ExtractFinished") + "\n\r";
            Log_Box.ScrollToEnd();
        }
        private void EnableControl()
        {
            PartitionDataGrid.IsEnabled = true;
            Flash_Btn.IsEnabled = true;
            Reboot_Btn.IsEnabled = true;
            Load_Btn.IsEnabled = true;
        }
        private void DisableControl()
        {
            PartitionDataGrid.IsEnabled = false;
            Flash_Btn.IsEnabled = false;
            Reboot_Btn.IsEnabled = false;
            Load_Btn.IsEnabled = false;
        }

        private void Reboot_Btn_Click(object sender, RoutedEventArgs e)
        {
            var devices = Fastboot.GetDevices();
            if (devices.Length == 0 || devices.Length > 1)
            {
                MessageBox.Show((string)FindResource("DeviceError"), (string)FindResource("Error"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            Log_Box.Text += (string)FindResource("DetectDevice") + devices[0] + "\n";
            var fb = new Fastboot(devices[0]);
            Log_Box.Text += (string)FindResource("ConnectDevice") + "\n";
            fb.Connect();
            Log_Box.Text += (string)FindResource("ConnectSuccess") + "\n\r";
            Log_Box.Text += (string)FindResource("RebootingDevice") + "   ";
            if (fb.Command("reboot").Status == Fastboot.Status.OKAY)
            {
                Log_Box.Text += (string)FindResource("Successful") + "\n";
            }
            else
            {
                Log_Box.Text += (string)FindResource("Failed") + "\n";
            }
            Log_Box.Text += (string)FindResource("Disconnect") + "\n\r";
            fb.Disconnect();
            Log_Box.ScrollToEnd();

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
    public enum FirmwareType
    {
        Image,
        UpdateBin,
        PayloadBin,
        UpdateApp,
        BatFile
    }   
}