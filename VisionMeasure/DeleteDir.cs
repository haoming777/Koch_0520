using CommonLib;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMeasure
{
    public static class DeleteDir
    {
        public static void DeleteMethod()
        {
            DeleteChildDir(Class_Config._Config.ImagePath, Class_Config._Config.ImageDays);
        }
        /// <summary>
        /// 递归得到子目录
        /// </summary>
        /// <param name="filePath">父级目录</param>
        /// <param name="directoryList"></param>
        /// <returns></returns>
        private static void GetDirectoryList(string filePath, List<DirectoryInfo> directoryList)
        {
            //目录不存在
            if (!Directory.Exists(filePath)) return;
            DirectoryInfo thisDirectory = new DirectoryInfo(filePath);
            DirectoryInfo[] array = thisDirectory.GetDirectories().OrderBy(p => p.CreationTime).ToArray();
            foreach (DirectoryInfo directory in array)
            {
                directoryList.Add(directory);
            }
        }
        /// <summary>
        /// 删除目录下指定时间之前的子目录
        /// </summary>
        /// <param name="dirPath">指定目录</param>
        /// <param name="keepDays">保留几天内的目录</param>
        private static void DeleteChildDir(string dirPath, int keepDays = 7)
        {
            List<DirectoryInfo> directoryList = new List<DirectoryInfo>();
            GetDirectoryList(dirPath, directoryList);
            DateTime dt = DateTime.Now.AddDays(-1 * keepDays);
            foreach (DirectoryInfo directory in directoryList)
            {
                if (directory.CreationTime.Date < dt.Date)
                {
                    System.IO.Directory.Delete(directory.FullName, true);
                }
            }
        }
    }
}
