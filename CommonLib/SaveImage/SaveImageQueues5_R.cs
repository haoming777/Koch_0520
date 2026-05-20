//using Crsdet.Loggers;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CommonLib
{
    public struct ImageFile5_R
    {
        public string FileName;
        public string Path;
        public Bitmap Image;
        public int IsJpg;
    }
    public sealed class SaveImageQueues5_R
    {
        //private static readonly IEasyLogger logger = EasyLogger.GetInstance("logs");
        /// <summary>
        /// 记录消息Queue
        /// </summary>
        private readonly ConcurrentQueue<ImageFile5_R> _que;

        /// <summary>
        /// 信号
        /// </summary>
        private readonly ManualResetEvent _mre;

        private static SaveImageQueues5_R _instance = new SaveImageQueues5_R();

        private SaveImageQueues5_R()
        {
            _que = new ConcurrentQueue<ImageFile5_R>();
            _mre = new ManualResetEvent(false);
        }
        public static SaveImageQueues5_R Instance()
        {
            return _instance;
        }

        public void Start()
        {
            Thread t = new Thread(new ThreadStart(doSave));
            t.IsBackground = true;
            t.Start();
        }
       
        private void doSave()
        {
            while (true)
            {
                // 等待信号通知
                _mre.WaitOne();

                ImageFile5_R imagefile;
                // 判断是否有内容需要如磁盘 从列队中获取内容，并删除列队中的内容
                while (_que.Count > 0 && _que.TryDequeue(out imagefile))
                {
                    if (imagefile.Image != null)
                    {
                        try
                        {
                            CreateDir(imagefile.Path);
                            if (imagefile.IsJpg == 0)
                            {
                                string sfile = imagefile.Path + imagefile.FileName + ".bmp";
                                imagefile.Image.Save(sfile, ImageFormat.Bmp);
                            }
                            else
                            {
                                string sfile = imagefile.Path + imagefile.FileName + ".jpg";
                                imagefile.Image.Save(sfile, ImageFormat.Jpeg);
                            }
                        }
                        catch (Exception ex)
                        {
                            //logger.error(ex.Message + @"\r\n" + ex.StackTrace);
                        }
                        
                    }
                }
                // 重新设置信号
                _mre.Reset();
                Thread.Sleep(5);
            }
        }
        private void CreateDir(string dirPath)
        {
            //创建目录
            if (!Directory.Exists(dirPath))
                System.IO.Directory.CreateDirectory(dirPath);
        }
        public static void PushImage(ImageFile5_R image)
        {
            if (Instance()._que.Count > 1000)
            {
                return;
            }
            Instance()._que.Enqueue(image);
            Instance()._mre.Set();
        }

    }
}
