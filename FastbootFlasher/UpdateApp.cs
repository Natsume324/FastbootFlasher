using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HuaweiUpdateLibrary;
using HuaweiUpdateLibrary.Core;


namespace FastbootFlasher
{
    internal class UpdateApp
    {
        public static ObservableCollection<Partition> ParseUpdateApp(string filePath)
        {
            var appfile = UpdateFile.Open(filePath, false);
            var Partitions = new ObservableCollection<Partition>();
            foreach (var entry in appfile)
            {
                Partitions.Add(new Partition
                {
                    Index = Partitions.Count+1,
                    Name = entry.FileType.ToLower(),
                    Size = ImageFile.FormatImageSize(entry.FileSize),
                    SourceFile = filePath
                });
            }
            return Partitions;
        }
    }
}
