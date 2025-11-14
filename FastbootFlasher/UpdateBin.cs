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
    internal class UpdateBin
    {
        public static ObservableCollection<Partition> ParseUpdateBin(string filePath)
        {
            var Partitions = new ObservableCollection<Partition>();
            using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
            using (BinaryReader reader = new BinaryReader(fs))
            {
                // 检测版本
                reader.BaseStream.Seek(0, SeekOrigin.Begin);
                ushort tlvType = reader.ReadUInt16();
                bool isL2 = tlvType==1;
                int compinfoSize = isL2 ? 87 : 71;
                int addrSize = isL2 ? 32 : 16;

                // 读取组件信息长度
                fs.Seek(178, SeekOrigin.Begin); // COMPINFO_LEN_OFFSET
                ushort compinfoLen = reader.ReadUInt16();

                // 计算组件数量
                int componentCount = compinfoLen / compinfoSize;

                // 解析每个组件
                long currentOffset = 180; // COMPONENT_ADDR_OFFSET

                for (int i = 0; i < componentCount; i++)
                {
                    // 读取分区名
                    fs.Seek(currentOffset, SeekOrigin.Begin);
                    byte[] addrBytes = reader.ReadBytes(addrSize);
                    string partitionName = Encoding.UTF8.GetString(addrBytes).TrimStart('/');

                    // 读取分区大小
                    int typeOffset = isL2 ? 36 : 20; // 组件类型偏移量
                    int sizeOffset = typeOffset + 11; // 大小字段在类型后的偏移

                    fs.Seek(currentOffset + sizeOffset, SeekOrigin.Begin);
                    uint partitionSize = reader.ReadUInt32();


                    currentOffset += compinfoSize;
                    Partitions.Add(new Partition
                    {
                        Index = i+1,
                        Name = partitionName,
                        Size = ImageFile.FormatImageSize(partitionSize),
                        SourceFile = filePath
                    });
                }           
            }
            return Partitions;
        }

        public static bool IsUpdateBin(string filePath)
        {
            using BinaryReader br = new BinaryReader(new FileStream(filePath, FileMode.Open, FileAccess.Read));
            string header = Encoding.ASCII.GetString(br.ReadBytes(96));
            if (header.Contains("OpenHarmony"))
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
