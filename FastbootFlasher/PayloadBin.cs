using ChromeosUpdateEngine;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace FastbootFlasher
{
    internal class PayloadBin
    {
        private const string Magic = "CrAU";
        public static ObservableCollection<Partition> ParsePayloadBin(string filePath)
        {
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            using var br = new BinaryReader(fs);
            string magic = Encoding.ASCII.GetString(br.ReadBytes(4));
            ulong version = BitConverter.ToUInt64(br.ReadBytes(8).Reverse().ToArray(), 0);
            ulong manifestLen = BitConverter.ToUInt64(br.ReadBytes(8).Reverse().ToArray(), 0);
            uint metadataSigLen = BitConverter.ToUInt32(br.ReadBytes(4).Reverse().ToArray(), 0);
            var manifest = DeltaArchiveManifest.Parser.ParseFrom(br.ReadBytes((int)manifestLen));
            var Partitions = new ObservableCollection<Partition>();
            foreach (var partition in manifest.Partitions)
            {
                string partName = partition.PartitionName;
                long blockSize = (long)manifest.BlockSize;
                long partSize=partition.Operations.Sum(op =>
                            (op.DstExtents?.Sum(e => (long)e.NumBlocks) ?? 0L) * blockSize );
               Partitions.Add(new Partition
                {
                    Index = Partitions.Count+1,
                    Name = partName,
                    Size = ImageFile.FormatImageSize(partSize),
                    SourceFile = filePath
                });
            }
            return Partitions;
        }

        public static bool IsPayloadBin(string filePath)
        {
            using BinaryReader br = new BinaryReader(new FileStream(filePath, FileMode.Open, FileAccess.Read));
            string header = Encoding.ASCII.GetString(br.ReadBytes(4));
            if (header == Magic)
            {  
                return true;
            }
            else
            {
                return false;
            }
        }
    }
}
