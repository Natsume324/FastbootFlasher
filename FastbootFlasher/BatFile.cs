using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace FastbootFlasher
{
    internal class BatFile
    {
        
        public static ObservableCollection<Partition> ParseBat(string filePath)
        {
            string directoryPath = Path.GetDirectoryName(filePath)+@"\";
            string imgPath;
            string imgSize;
            var Partitions = new ObservableCollection<Partition>();
            foreach (var line in File.ReadLines(filePath))
            {
                if(line.Contains(" flash "))
                {
                    var parts = line.Split(' ');
                    imgPath = directoryPath + parts[4].Replace(@"%~dp0", "");
                    imgSize = ImageFile.FormatImageSize(new FileInfo(imgPath).Length);
                    Partitions.Add(new Partition
                    {
                        Index = Partitions.Count+1,
                        Name = parts[3],
                        Size = imgSize,
                        SourceFile = imgPath
                    });
                }
            }
            return Partitions;
        }

    }
}
