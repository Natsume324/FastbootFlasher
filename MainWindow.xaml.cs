using HuaweiUpdateLibrary.Core;
using SharpCompress.Common;
using System.Collections.Concurrent;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Security.Policy;
using System.Text;
using System.Text.RegularExpressions;
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
        public static MainWindow Instance { get; private set; }
        private dynamic langModel;
        public ObservableCollection<ListViewItem> Items { get; }
            = new ObservableCollection<ListViewItem>();
        public static ProgressBar pb;
        public string[] skipPartitions = ["sha256rsa", "crc", "curver", "verlist", "package_type", "base_verlist", "base_ver", "base_package_info","ptable_cust", "cust_verlist", "cust_ver", "cust_package_info","preload_verlist", "preload_ver", "preload_package_info","ptable_preload", "board_list", "package_info", "permission_hash_bin"];


        public MainWindow()
        {
            InitializeComponent();
            Instance = this;
            string iniPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "lang.ini");
            langModel = new DynamicLanguageModel(iniPath);
            DataContext = langModel;
            LangList.ItemsSource = langModel.AvailableLanguages;
            if (langModel.AvailableLanguages.Contains("zh"))
                LangList.SelectedItem = "zh";
            else
                LangList.SelectedIndex = 0;
            PartitionList.ItemsSource = Items;
            this.ProgressBar.SetBinding(System.Windows.Controls.ProgressBar.ValueProperty,
                new Binding("Value") { Source = pb = new ProgressBar() });
        }

       

        private void LoadButton_Click(object sender, RoutedEventArgs e)
        {
            Items.Clear();
            FilePathBox.Clear();
            LogBox.Clear();
            Microsoft.Win32.OpenFileDialog openFileDialog = new()
            {
                Filter = langModel.Firmware+ "|*.app;update.bin;payload.bin;flash_all.bat;*.img",
                Multiselect = true,
                Title = langModel.SelectFile
            };

            if (openFileDialog.ShowDialog() == true)
            {
                if (openFileDialog.SafeFileName == "flash_all.bat")
                {
                    FilePathBox.Text += openFileDialog.FileName;
                    string[] lines = File.ReadAllLines(openFileDialog.FileName);
                    Regex flashRegex = new(@"\bflash\s+(\w+)\s+(?:%~dp0)?images[\\/]([\w\.\-\\/]+)(?=\s|\|)", RegexOptions.IgnoreCase);
                    int i = 0;
                    foreach (string line in lines)
                    {
                        Match match = flashRegex.Match(line);
                        if (match.Success)
                        {
                            i++;
                            string original = match.Groups[1].Value;
                            string partitionName;
                            partitionName = original;
                            string imgName = match.Groups[2].Value.Replace("\\", "/");
                            string batPath = openFileDialog.FileName[..^13];
                            string imgPath = @$"{batPath}images\{imgName}";
                            FileInfo fileInfo = new(imgPath);
                            long imgSize = fileInfo.Length;
                            Items.Add(new ListViewItem { Num = i, Size = FormatSize(imgSize), Addr = "0x" + imgSize.ToString("X8"), Part = partitionName, Source = imgPath });
                        }
                    }
                }
                else if (openFileDialog.SafeFileName == "payload.bin")
                {
                    var entries = PayloadFile.ParsePayloadFile(openFileDialog.FileName);
                    foreach (var entry in entries)
                    {
                        Items.Add(entry);
                    }

                    FilePathBox.Text += $"{openFileDialog.FileName}";
                    string rootDir = Path.GetDirectoryName(FilePathBox.Text)!;

                    string[] found = Directory.GetFiles(rootDir, "metadata", SearchOption.AllDirectories);

                    if (found.Length > 0)
                    {
                        string metadataPath = found[0];
                        LogBox.Text+= langModel.Log_FoundMetadata+"\n";
                        string[] lines = File.ReadAllLines(metadataPath);

                        var metadata = new MetadataInfo(metadataPath);
                        LogBox.Text += langModel.Metadata_Model+metadata.ProductName + "\n"+
                                       langModel.Metadata_AndroidVer + metadata.AndroidVersion+"\n"+
                                       langModel.Metadata_SecurePatch + metadata.SecurityPatch + "\n"+
                                       langModel.Metadata_Version + metadata.VersionName;
                    }
                    else
                    {
                        LogBox.Text += langModel.Log_MetadataNotFound+"\n\r";
                    }
                }
                else if (openFileDialog.SafeFileName.EndsWith(".APP")|| openFileDialog.SafeFileName.EndsWith(".app"))
                {
                    foreach (var filePath in openFileDialog.FileNames)
                    {
                        var entries = APPFile.ParseAPPFile(filePath, out var versionInfo);


                        LogBox.AppendText(langModel.Log_VersionInfo + $"\n{versionInfo}\n");
                        LogBox.ScrollToEnd();

                        foreach (var entry in entries)
                        {
                            Items.Add(entry);
                        }

                        FilePathBox.Text += $"{filePath}\n";
                    }
                }
                else if (openFileDialog.SafeFileName == "update.bin")
                {
                    var entries = UpdatebinFile.ParseUpdatebinFile(openFileDialog.FileName);
                    foreach (var entry in entries)
                    {
                        Items.Add(entry);
                    }

                    FilePathBox.Text += $"{openFileDialog.FileName}";
                }
                else if (openFileDialog.SafeFileName.EndsWith(".img"))
                {
                    var num = 1;
                    for (int i = 0; i < openFileDialog.FileNames.Length; i++)
                    {
                        string filePath = openFileDialog.FileNames[i];
                        string filename = openFileDialog.SafeFileNames[i];

                        if (!filename.EndsWith(".img", StringComparison.OrdinalIgnoreCase))
                            continue;

                        FileInfo fileInfo = new(filePath);
                        long imgSize = fileInfo.Length;

                        Items.Add(new ListViewItem
                        {
                            Num = num,
                            Size = FormatSize(imgSize),
                            Addr = "0x" + imgSize.ToString("X8"),
                            Part = Path.GetFileNameWithoutExtension(filename), 
                            Source = filePath
                        });

                        num++;
                        FilePathBox.Text += $"{filePath}\n";
                    }

                }

            }

        }
        
        public static string FormatSize(long size)
        {
            if (size < 1024)
                return $"{size}B";
            else if (size < 1024 * 1024)
                return $"{size / 1024.0:F2}KB";
            else if (size < 1024 * 1024 * 1024)
                return $"{size / (1024.0 * 1024.0):F2}MB";
            else
                return $"{size / (1024.0 * 1024.0 * 1024.0):F2}GB";
        }

        private async void ExtractButton_Click(object sender, RoutedEventArgs e)
        {

            if (FilePathBox.Text.EndsWith("flash_all.bat"))
                return;
            else if (FilePathBox.Text.EndsWith(".img\n"))
                return;
            else if (FilePathBox.Text.EndsWith("payload.bin"))
                await PayExtractParts();
            else if (FilePathBox.Text.EndsWith("update.bin"))
                await UpdatebinExtractParts();
            else if (FilePathBox.Text.EndsWith(".APP\n") || FilePathBox.Text.EndsWith(".app\n"))
                await APPExtractParts();
        }

        private async Task APPExtractParts(bool skip=false)
        {
            DisabledControls();
            foreach (var item in PartitionList.SelectedItems.Cast<ListViewItem>())
            {
                string partition = item.Part;
                string FilePath = item.Source;
                if (skip == true && skipPartitions.Contains(partition))
                {
                    LogBox.Text += string.Format(langModel.Log_SkipExtract, partition) + "\n\r";
                    continue;
                }
                if (partition == "hisiufs_gpt")
                    partition = "ptable";
                if (partition == "ufsfw")
                    partition = "ufs_fw";
                LogBox.Text += string.Format(langModel.Log_ExtractingPartition, partition);
                await APPFile.ExtractPartition(FilePath, item.Num);
                LogBox.ScrollToEnd();
                LogBox.Text += "   "+langModel.Log_Success + "\n\r";
                
            }
            LogBox.Text += langModel.Log_AllExtracted + "\n\r";
            string[] superFragments = Directory.GetFiles(@".\images", "super.*.img")
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (superFragments.Length >= 2)
            {
                LogBox.Text += langModel.Log_MergingSuper; 
                await APPFile.MergerSuperSparse(superFragments);
                LogBox.Text += "   " + langModel.Log_Success + "\n\r";
                foreach (string fragment in superFragments) File.Delete(fragment);
                
            }
            LogBox.ScrollToEnd();
            EnabledControls();
        }

        private async Task PayExtractParts()
        {
            DisabledControls();
            var tasks = new List<Task>();
            foreach (var item in PartitionList.SelectedItems.Cast<ListViewItem>())
            {
                string partition = item.Part;
                string FilePath = item.Source;
                LogBox.Text += string.Format(langModel.Log_ExtractingPartition, partition) + "\n\r";
                
                tasks.Add(Task.Run(async () =>
                {
                    await PayloadFile.ExtractPartition(FilePath, item.Num);
                    Dispatcher.Invoke(() =>
                    {
                        LogBox.Text += string.Format(langModel.Log_ExtractSuccess, partition) + "\n\r";
                        LogBox.ScrollToEnd();
                    });
                }));
            }
            await Task.WhenAll(tasks);
            LogBox.Text += langModel.Log_AllExtracted + "\n\r";
            LogBox.ScrollToEnd();
            EnabledControls();
        }
        private async Task UpdatebinExtractParts(bool skip = false)
        {
            DisabledControls();
            foreach (var item in PartitionList.SelectedItems.Cast<ListViewItem>())
            {
                string partition = item.Part;
                string FilePath = item.Source;
                if (skip == true && skipPartitions.Contains(partition))
                {
                    LogBox.Text += string.Format(langModel.Log_SkipExtract, partition) + "\n\r";
                    continue;
                }  
                LogBox.Text += string.Format(langModel.Log_ExtractingPartition, partition);
                await UpdatebinFile.ExtractPartition(FilePath, item.Num);
                LogBox.ScrollToEnd();
                LogBox.Text += "   " + langModel.Log_Success + "\n\r";
            }
            LogBox.Text += langModel.Log_AllExtracted + "\n\r";
            LogBox.ScrollToEnd();
            EnabledControls();
        }

        private async void FlashButton_Click(object sender, RoutedEventArgs e)
        {
            int sum = 0;
            int fail=0;
            int okay=0;
            LogBox.Text += langModel.Log_DetectingDevice+"\n";
            string outText = await FastbootCmd.Command("devices");

            
            var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("fastboot"))
                .ToList();

            if (lines.Count == 0)
            {
                LogBox.Text += "\n"+langModel.Log_DeviceNotFound+"\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else if (lines.Count > 1)
            {
                LogBox.Text += langModel.Log_Success + "\n";
                foreach (var line in lines)
                {
                    LogBox.Text += line + "\n";
                }
                LogBox.Text += langModel.Log_MultipleDevices + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else
            {
                LogBox.Text += langModel.Log_Success + "\n\r";
                
                LogBox.ScrollToEnd();

                if (PartitionList.SelectedItems.Count == 0)
                {
                    MessageBox.Show(langModel.Log_NoPartitionSelected, langModel.Log_NoPartitionSelected, MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                
                if (FilePathBox.Text.EndsWith("flash_all.bat"))
                {
                    LogBox.Text += langModel.Log_ReadingLockState + "\n";
                    outText = await FastbootCmd.Command("oem device-info");
                    LogBox.Text += "\n";
                    DisabledControls();
                    sum = PartitionList.SelectedItems.Count;
                    foreach (var item in PartitionList.SelectedItems.Cast<ListViewItem>())
                    {
                        string partition = item.Part;
                        string FilePath = item.Source;
                        LogBox.Text += string.Format(langModel.Log_FlashingPartition, partition) + "\n";
                        outText=await FastbootCmd.Command($@"flash {partition} {FilePath}");
                       
                        if (outText.Contains("Command failed"))
                        {
                            LogBox.Text += string.Format(langModel.Log_FlashFail, partition) + "\n\r";
                            fail++;
                        }
                        else if(outText.Contains("Finished"))
                        {
                            LogBox.Text += string.Format(langModel.Log_FlashSuccess, partition) + "\n\r";
                            okay++;
                        }
                        LogBox.ScrollToEnd();
                    }
                }
                else if (FilePathBox.Text.EndsWith("payload.bin"))
                {
                    LogBox.Text += langModel.Log_ReadingLockState + "\n";
                    outText = await FastbootCmd.Command("oem device-info");
                    LogBox.Text += "\n";
                    await PayExtractParts();
                    DisabledControls();
                    sum = PartitionList.SelectedItems.Count;
                    foreach (var item in PartitionList.SelectedItems.Cast<ListViewItem>())
                    {
                        string partition = item.Part;
                        LogBox.Text += string.Format(langModel.Log_FlashingPartition, partition) + "\n";
                        outText=await FastbootCmd.Command($@"flash {partition} .\images\{partition}.img");
                        
                        if (outText.Contains("Command failed"))
                        {
                            LogBox.Text += string.Format(langModel.Log_FlashFail, partition) + "\n\r";
                            fail++;
                        }
                        else if(outText.Contains("Finished"))
                        {
                            LogBox.Text += string.Format(langModel.Log_FlashSuccess, partition) + "\n\r";
                            okay++;
                        }
                        File.Delete($@".\images\{partition}.img");
                        LogBox.ScrollToEnd();
                    }
                }
                else if (FilePathBox.Text.EndsWith("update.bin"))
                {
                    LogBox.Text += langModel.Log_ReadingLockState + "\n";
                    outText = await FastbootCmd.Command("oem lock-state info");
                    LogBox.Text += "\n";
                    await UpdatebinExtractParts(true);
                    DisabledControls();
                    sum = PartitionList.SelectedItems.Count;
                    foreach (var item in PartitionList.SelectedItems.Cast<ListViewItem>())
                    {
                        string partition = item.Part;
                        if (skipPartitions.Contains(partition))
                        {
                            LogBox.Text += string.Format(langModel.Log_SkipFlash, partition) + "\n\r";
                            okay++;
                            continue;
                        }
                        if (partition == "ptable")
                        {
                            LogBox.Text += string.Format(langModel.Log_ErasingPartition, partition) + "\n";
                            outText = await FastbootCmd.Command("erase ptable");

                            if (outText.Contains("Command failed"))
                            {
                                LogBox.Text += string.Format(langModel.Log_EraseFail, partition) + "\n\r";
                            }
                            else if (outText.Contains("Finished"))
                            {
                                LogBox.Text += string.Format(langModel.Log_EraseSuccess, partition) + "\n\r";
                            }
                        }
                        LogBox.Text += "\n" + string.Format(langModel.Log_FlashingPartition, partition) + "\n";
                        outText = await FastbootCmd.Command($@"flash {partition} .\images\{partition}.img");

                        if (outText.Contains("Command failed"))
                        {
                            LogBox.Text += string.Format(langModel.Log_FlashFail, partition) + "\n\r";
                            fail++;
                        }
                        else if (outText.Contains("Finished"))
                        {
                            LogBox.Text += string.Format(langModel.Log_FlashSuccess, partition) + "\n\r";
                            okay++;
                        } 
                        File.Delete($@".\images\{partition}.img");
                        LogBox.ScrollToEnd();
                    }
                }
                else if(FilePathBox.Text.EndsWith(".APP\n") || FilePathBox.Text.EndsWith(".app\n"))
                {
                    LogBox.Text += langModel.Log_ReadingLockState + "\n";
                    outText = await FastbootCmd.Command("oem lock-state info");
                    LogBox.Text += "\n";
                    await APPExtractParts(true);
                    DisabledControls();
                    sum = PartitionList.SelectedItems.Count;
                    bool superFlashed = false;
                    foreach (var item in PartitionList.SelectedItems.Cast<ListViewItem>())
                    {
                        string partition = item.Part;
                        if (skipPartitions.Contains(partition))
                        {
                            LogBox.Text += string.Format(langModel.Log_SkipFlash, partition) + "\n\r";
                            okay++;
                            continue;
                        }

                        if (partition == "hisiufs_gpt")
                        {
                            partition = "ptable";
                            LogBox.Text += string.Format(langModel.Log_ErasingPartition, partition) + "\n";
                            outText=await FastbootCmd.Command("erase ptable");
                            
                            if (outText.Contains("Command failed"))
                            {
                                LogBox.Text += string.Format(langModel.Log_EraseFail, partition) + "\n\r";
                            }
                            else if (outText.Contains("Finished"))
                            {
                                LogBox.Text += string.Format(langModel.Log_EraseSuccess, partition) + "\n\r";
                            }
                        }
                        if (partition == "ufsfw") partition = "ufs_fw";
                        if (partition == "super")
                        {
                            if (superFlashed) continue;
                            if (!File.Exists($@".\images\super.img"))
                            {
                                continue;
                            }
                            superFlashed = true;
                            LogBox.Text += string.Format(langModel.Log_ErasingPartition, partition) + "\n";
                            outText = await FastbootCmd.Command("erase super");
                            
                            if (outText.Contains("Command failed"))
                            {
                                LogBox.Text += string.Format(langModel.Log_EraseFail, partition) + "\n\r";
                            }
                            else if (outText.Contains("Finished"))
                            {
                                LogBox.Text += string.Format(langModel.Log_EraseSuccess, partition) + "\n\r";
                                LogBox.ScrollToEnd();
                            }
                        }
                        LogBox.Text += "\n"+ string.Format(langModel.Log_FlashingPartition, partition) + "\n";
                        outText = await FastbootCmd.Command($@"flash {partition} .\images\{partition}.img");

                        if (outText.Contains("Command failed"))
                        {
                            LogBox.Text += string.Format(langModel.Log_FlashFail, partition) + "\n\r";
                            fail++;
                        }
                        else if (outText.Contains("Finished"))
                        {
                            LogBox.Text += string.Format(langModel.Log_FlashSuccess, partition) + "\n\r";
                            okay++;
                        }
                        else if (outText.Contains("No such file or directory"))
                        {
                            if (partition == "version" || partition == "preload")
                            {
                                LogBox.Text += string.Format(langModel.Log_SkipFlash, partition) + "\n\r";
                                okay++;
                            }
                            else
                            {
                                LogBox.Text += string.Format(langModel.Log_FlashFail, partition) + "\n\r";
                                fail++;
                            }
                        }


                        File.Delete($@".\images\{partition}.img");
                        LogBox.ScrollToEnd();
                    }
                }
                else if (FilePathBox.Text.EndsWith(".img\n"))
                {
                    DisabledControls();
                    sum = PartitionList.SelectedItems.Count;
                    foreach (var item in PartitionList.SelectedItems.Cast<ListViewItem>())
                    {
                        string partition = item.Part;
                        string FilePath = item.Source;
                        LogBox.Text += string.Format(langModel.Log_FlashingPartition, partition) + "\n";
                        outText = await FastbootCmd.Command($@"flash {partition} {FilePath}");

                        if (outText.Contains("Command failed"))
                        {
                            LogBox.Text += string.Format(langModel.Log_FlashFail, partition) + "\n\r";
                            fail++;
                        }
                        else if (outText.Contains("Finished"))
                        {
                            LogBox.Text += string.Format(langModel.Log_FlashSuccess, partition) + "\n\r";
                            okay++;
                        }
                       
                        LogBox.ScrollToEnd();
                    }
                }
                EnabledControls();
                LogBox.Text += string.Format(langModel.Log_Summary, sum, okay, fail) + "！\n\r";
                LogBox.ScrollToEnd();
            }
        }

        private async void RebootButton_Click(object sender, RoutedEventArgs e)
        {
            LogBox.Text += langModel.Log_DetectingDevice+"\n";
            string outText = await FastbootCmd.Command("devices");
            var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("fastboot"))
                .ToList();
            if (lines.Count == 0)
            {
                LogBox.Text += "\n" + langModel.Log_DeviceNotFound + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else if (lines.Count > 1)
            {
                LogBox.Text += langModel.Log_Success + "\n";
                foreach (var line in lines)
                {
                    LogBox.Text += line + "\n";
                }
                LogBox.Text += langModel.Log_MultipleDevices + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else
            {
                LogBox.Text += langModel.Log_Success + "\n";
                LogBox.Text += lines[0] + "\n\r";
                LogBox.ScrollToEnd();
                outText = await FastbootCmd.Command("reboot");
                if (outText.Contains("Command failed"))
                {
                    LogBox.Text += langModel.Log_RebootFail+ "\n\r";
                }
                else if (outText.Contains("Finished"))
                {
                    LogBox.Text += langModel.Log_RebootSuccess + "\n\r";
                }
                LogBox.ScrollToEnd();
            }
        }

        private void DisabledControls()
        {
            PartitionList.IsEnabled = false;
            FlashButton.IsEnabled = false;
            RebootButton.IsEnabled = false;
            LoadButton.IsEnabled = false;

        }
        private void EnabledControls()
        {
            PartitionList.IsEnabled = true;
            FlashButton.IsEnabled = true;
            RebootButton.IsEnabled = true;
            LoadButton.IsEnabled = true;
        }
        private void LangList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (LangList.SelectedItem is string selectedLang)
            {
                langModel.SetLanguage(selectedLang);
            }
        }
        private void Hyperlink_RequestNavigate(object sender, RequestNavigateEventArgs e)
        {
            Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri)
            {
                UseShellExecute = true
            });
            e.Handled = true;
        }

        private async void ToBLButton_Click(object sender, RoutedEventArgs e)
        {
            Tab.SelectedItem = TabMain;
            LogBox.Text += langModel.Log_DetectingDevice+"\n";
            string outText = await FastbootCmd.Command("devices");
            var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("fastboot"))
                .ToList();
            if (lines.Count == 0)
            {
                LogBox.Text += "\n" + langModel.Log_DeviceNotFound + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else if (lines.Count > 1)
            {
                LogBox.Text += langModel.Log_Success + "\n";
                foreach (var line in lines)
                {
                    LogBox.Text += line + "\n";
                }
                LogBox.Text += langModel.Log_MultipleDevices + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else
            {
                LogBox.Text += langModel.Log_Success + "\n";
                LogBox.Text += lines[0] + "\n\r";
                LogBox.ScrollToEnd();
                outText = await FastbootCmd.Command("reboot bootloader");
                if (outText.Contains("Command failed"))
                {
                    LogBox.Text += langModel.Log_EnterFail + "\n\r";
                }
                else if (outText.Contains("Finished"))
                {
                    LogBox.Text += langModel.Log_EnterSuccess + "\n\r";
                }
                LogBox.ScrollToEnd();
            }
        }

        private async void ToFBDButton_Click(object sender, RoutedEventArgs e)
        {
            Tab.SelectedItem = TabMain;
            LogBox.Text += langModel.Log_DetectingDevice+"\n";
            string outText = await FastbootCmd.Command("devices");
            var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("fastboot"))
                .ToList();
            if (lines.Count == 0)
            {
                LogBox.Text += "\n" + langModel.Log_DeviceNotFound + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else if (lines.Count > 1)
            {
                LogBox.Text += langModel.Log_Success + "\n";
                foreach (var line in lines)
                {
                    LogBox.Text += line + "\n";
                }
                LogBox.Text += langModel.Log_MultipleDevices + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else
            {
                LogBox.Text +=  langModel.Log_Success + "\n";
                LogBox.Text += lines[0] + "\n\r";
                LogBox.ScrollToEnd();
                outText = await FastbootCmd.Command("reboot fastboot");
                if (outText.Contains("Command failed"))
                {
                    LogBox.Text += langModel.Log_EnterFail + "\n\r";
                }
                else if (outText.Contains("Finished"))
                {
                    LogBox.Text += langModel.Log_EnterSuccess + "\n\r";
                }
                LogBox.ScrollToEnd();
            }
        }

        private async void UnBLButton_Click(object sender, RoutedEventArgs e)
        {
            Tab.SelectedItem = TabMain;
            LogBox.Text += langModel.Log_DetectingDevice+"\n";
            string outText = await FastbootCmd.Command("devices");
            var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("fastboot"))
                .ToList();
            if (lines.Count == 0)
            {
                LogBox.Text += "\n" + langModel.Log_DeviceNotFound + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else if (lines.Count > 1)
            {
                LogBox.Text += langModel.Log_Success + "\n";
                foreach (var line in lines)
                {
                    LogBox.Text += line + "\n";
                }
                LogBox.Text += langModel.Log_MultipleDevices + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else
            {
                LogBox.Text += langModel.Log_Success + "\n";
                LogBox.Text += lines[0] + "\n\r";
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.Log_ExecUnlock + "\n";
                LogBox.Text += langModel.Log_SelectUnlock + "\n";
                outText = await FastbootCmd.Command("flashing unlock");
                if (outText.Contains("Command failed"))
                {
                    LogBox.Text += langModel.Log_ExecFail + "\n\r";
                }
                else if (outText.Contains("Finished"))
                {
                    LogBox.Text += langModel.Log_ExecSuccess + "\n\r";
                    MessageBoxResult result = MessageBox.Show(
                        langModel.Message_Content,
                        langModel.Log_Tips,                        
                        MessageBoxButton.YesNo,           
                        MessageBoxImage.Question,        
                        MessageBoxResult.No             
                    );

                    if (result == MessageBoxResult.Yes)
                    {
                        LogBox.Text += langModel.Log_ExecCritical + "\n";
                        LogBox.Text += langModel.Log_SelectUnlock + "\n";
                        outText = await FastbootCmd.Command("flashing unlock_critical");
                        if (outText.Contains("Command failed"))
                        {
                            LogBox.Text += langModel.Log_ExecFail + "\n\r";
                        }
                        else if (outText.Contains("Finished"))
                        {
                            LogBox.Text += langModel.Log_ExecSuccess + "\n\r";
                        }
                    }
                }
                LogBox.ScrollToEnd();
            }
        }

        private async void ReadBLButton_Click(object sender, RoutedEventArgs e)
        {
            Tab.SelectedItem = TabMain;
            LogBox.Text += langModel.Log_DetectingDevice+"\n";
            string outText = await FastbootCmd.Command("devices");
            var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("fastboot"))
                .ToList();
            if (lines.Count == 0)
            {
                LogBox.Text += "\n" + langModel.Log_DeviceNotFound + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else if (lines.Count > 1)
            {
                LogBox.Text +=  langModel.Log_Success + "\n";
                foreach (var line in lines)
                {
                    LogBox.Text += line + "\n";
                }
                LogBox.Text += langModel.Log_MultipleDevices + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else
            {
                LogBox.Text +=  langModel.Log_Success + "\n";
                LogBox.Text += lines[0] + "\n\r";
                LogBox.ScrollToEnd();
                outText = await FastbootCmd.Command("oem device-info");
                if (outText.Contains("Command failed"))
                {
                    LogBox.Text += langModel.Log_ReadFail + "\n\r";
                }
                else if (outText.Contains("Finished"))
                {
                    LogBox.Text += langModel.Log_ReadSuccess + "\n\r";
                }
                LogBox.ScrollToEnd();
            }
        }

        private async void ReadInfoButton_Click(object sender, RoutedEventArgs e)
        {
            Tab.SelectedItem = TabMain;
            LogBox.Text += langModel.Log_DetectingDevice+"\n";
            string outText = await FastbootCmd.Command("devices");
            var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("fastboot"))
                .ToList();
            if (lines.Count == 0)
            {
                LogBox.Text += "\n" + langModel.Log_DeviceNotFound + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else if (lines.Count > 1)
            {
                LogBox.Text += langModel.Log_Success + "\n";
                foreach (var line in lines)
                {
                    LogBox.Text += line + "\n";
                }
                LogBox.Text += langModel.Log_MultipleDevices + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else
            {
                LogBox.Text += langModel.Log_Success + "\n";
                LogBox.Text += lines[0] + "\n\r";
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.HW_Model + "\n";
                outText = await FastbootCmd.Command("getvar devicemodel");
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.HW_VendorCountry + "\n";
                outText = await FastbootCmd.Command("getvar vendorcountry");
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.HW_CPU + "\n";
                outText = await FastbootCmd.Command("getvar preoduct");
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.HW_LockState + "\n";
                outText = await FastbootCmd.Command("oem lock-state info");
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.HW_Version + "\n";
                outText = await FastbootCmd.Command("oem get-build-number");
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.HW_AndroidVer + "\n";
                outText = await FastbootCmd.Command("oem oeminforead-ANDROID_VERSION");
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.HW_PSID + "\n";
                outText = await FastbootCmd.Command("oem get-psid");
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.HW_Battery + "\n";
                outText = await FastbootCmd.Command("oem battery_present_check");
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.HW_DongleInfo + "\n";
                outText = await FastbootCmd.Command("getvar dongle_info");
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.HW_KeyVer + "\n";
                outText = await FastbootCmd.Command("oem get_key_version");
                LogBox.ScrollToEnd();
            }
        }

        private async void FBtoUpButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.FilePathBox.Text == "" || this.FilePathBox.Text.EndsWith("flash_all.bat") || this.FilePathBox.Text.EndsWith("payload.bin"))
            {
                MessageBox.Show(langModel.Message_HWSelect, langModel.Log_Tips, MessageBoxButton.OK, MessageBoxImage.Warning);
                this.Tab.SelectedItem = TabMain;
                return;
            }
            bool found = false;
            foreach (var item in PartitionList.Items.Cast<ListViewItem>())
            {
                if (item.Part == "recovery_ramdisk")
                {
                    found = true;
                    break;
                }
            }
            if (!found)
            {
                MessageBox.Show(langModel.Message_HWBase, langModel.Log_Tips, MessageBoxButton.OK, MessageBoxImage.Warning);
                this.Tab.SelectedItem = TabMain;
                return;
            }
            Tab.SelectedItem = TabMain;
            LogBox.Text += langModel.Log_DetectingDevice+"\n";
            string outText = await FastbootCmd.Command("devices");
            var lines = outText.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => line.Contains("fastboot"))
                .ToList();
            if (lines.Count == 0)
            {
                LogBox.Text += "\n" + langModel.Log_DeviceNotFound + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else if (lines.Count > 1)
            {
                LogBox.Text += langModel.Log_Success + "\n";
                foreach (var line in lines)
                {
                    LogBox.Text += line + "\n";
                }
                LogBox.Text += langModel.Log_MultipleDevices + "\n\r";
                LogBox.ScrollToEnd();
                return;
            }
            else
            {
                LogBox.Text += langModel.Log_Success + "\n";
                LogBox.Text += lines[0] + "\n\r";
                LogBox.ScrollToEnd();
                LogBox.Text += langModel.Log_HWRescue+"\n";
                outText = await FastbootCmd.Command("getvar rescue_version");
                LogBox.Text +=  "\n";
                foreach (var item in PartitionList.Items.Cast<ListViewItem>())
                {
                    string partition = item.Part;
                    int num = item.Num;
                    string FilePath = item.Source;
                    if (partition == "kernel")
                    {
                        LogBox.Text += string.Format(langModel.Log_ExtractingPartition, partition);
                        await APPFile.ExtractPartition(FilePath, num);
                        LogBox.ScrollToEnd();
                        LogBox.Text += "   " + langModel.Log_Success + "\n\r";
                        LogBox.ScrollToEnd();
                    }
                    if (partition == "recovery_ramdisk")
                    {
                        LogBox.Text += string.Format(langModel.Log_ExtractingPartition, partition);
                        await APPFile.ExtractPartition(FilePath, num);
                        LogBox.ScrollToEnd();
                        LogBox.Text += "   " + langModel.Log_Success + "\n\r";
                        LogBox.ScrollToEnd();
                    }
                    if (partition == "recovery_vendor")
                    {
                        LogBox.Text += string.Format(langModel.Log_ExtractingPartition, partition);
                        await APPFile.ExtractPartition(FilePath, num);
                        LogBox.ScrollToEnd();
                        LogBox.Text += "   " + langModel.Log_Success + "\n\r";
                        LogBox.ScrollToEnd();
                    }
                    LogBox.ScrollToEnd();
                }
                LogBox.Text += "\n" + string.Format(langModel.Log_FlashingPartition, "recovery_kernel") + "\n";
                outText = await FastbootCmd.Command(@"flash rescue_recovery_kernel .\images\kernel.img");
                if (outText.Contains("Command failed"))
                {
                    LogBox.Text += string.Format(langModel.Log_FlashFail, "recovery_kernel") + "\n\r";
                }
                else if (outText.Contains("Finished"))
                {
                    LogBox.Text += string.Format(langModel.Log_FlashSuccess, "recovery_kernel") + "\n\r";
                }
                File.Delete(@".\images\recovery_kernel.img");
                LogBox.ScrollToEnd();

                LogBox.Text += "\n" + string.Format(langModel.Log_FlashingPartition, "recovery_ramdisk") + "\n";
                outText = await FastbootCmd.Command(@"flash rescue_recovery_ramdisk .\images\recovery_ramdisk.img");

                if (outText.Contains("Command failed"))
                {
                    LogBox.Text += string.Format(langModel.Log_FlashFail, "recovery_ramdisk") + "\n\r";
                }
                else if (outText.Contains("Finished"))
                {
                    LogBox.Text += string.Format(langModel.Log_FlashSuccess, "recovery_ramdisk") + "\n\r";
                }
                File.Delete(@".\images\recovery_ramdisk.img");
                LogBox.ScrollToEnd();

                LogBox.Text += "\n" + string.Format(langModel.Log_FlashingPartition, "recovery_vendor") + "\n";
                outText = await FastbootCmd.Command(@"flash rescue_recovery_vendor .\images\recovery_vendor.img");
         
                if (outText.Contains("Command failed"))
                {
                    LogBox.Text += string.Format(langModel.Log_FlashFail, "recovery_vendor") + "\n\r";
                }
                else if (outText.Contains("Finished"))
                {
                    LogBox.Text += string.Format(langModel.Log_FlashSuccess, "recovery_vendor") + "\n\r";
                }
                File.Delete(@".\images\recovery_vendor.img");
                LogBox.ScrollToEnd();

                LogBox.Text +=langModel.Log_HWTo + "\n";
                outText = await FastbootCmd.Command("getvar rescue_enter_recovery");
               
                if (outText.Contains("FAILED"))
                {
                    LogBox.Text += langModel.Log_HWToFail+ "\n\r";
                }
                else if (outText.Contains("Finished"))
                {
                    LogBox.Text += langModel.Log_HWToSuccess + "\n\r";
                }
                LogBox.ScrollToEnd();
            }
        }

    }

    public class ListViewItem
    {
        public int Num { get; set; }
        public string Part { get; set; }
        public string Size { get; set; }
        public string Addr { get; set; }
        public string Source { get; set; }
    }

    public class ProgressBarValue : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private double _value;
        public double Value
        {
            get => _value;
            set
            {
                _value = value;

                if (PropertyChanged!=null)
                {
                    PropertyChanged.Invoke(this,new PropertyChangedEventArgs("Value"));
                    
                }
            }
        }
    }

}
