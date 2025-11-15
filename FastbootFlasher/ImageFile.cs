using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;

namespace FastbootFlasher
{
    internal class ImageFile
    {
        public static Partition ParseImage(string filePath,int index)
        {
            return new Partition()
            {
                Index=index+1,
                Name= Path.GetFileNameWithoutExtension(filePath),
                Size= ImageFile.FormatImageSize(new FileInfo(filePath).Length),
                SourceFile=filePath
            };
        }

        public static string FormatImageSize(long size)
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
    }
}
