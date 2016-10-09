using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using NewLife.Log;

namespace LibExport
{
    class Program
    {
        static void Main(string[] args)
        {
            XTrace.UseConsole();

            // E:\Auto\STM32F1\Apollo0801
            PathHelper.BaseDirectory = @"E:\Auto\STM32F1\Apollo0801";

            Export(false);
            Export(true);

            Console.WriteLine("OK!");
            Console.ReadKey();
        }

        static void Export(Boolean debug)
        {
            var dir1 = "../SmartOS/Tool/Obj";
            var dir2 = "../SmartOS/Platform/STM32F1/Obj";
            var map = ".map";

            // 调试版
            if (debug)
            {
                dir1 += "D";
                dir2 += "D";
                map = "D" + map;
            }

            // 找到映射文件
            var mp = "List".AsDirectory().GetAllFiles("*" + map).FirstOrDefault();
            Console.WriteLine(mp.FullName);
            var name = mp.Name.TrimEnd(".map");

            // 读取所有需要用到的对象文件
            var txt = File.ReadAllText(mp.FullName).Substring("Library Member Name", "Library Totals");
            var ns = txt.Trim().Split("\r\n");
            var objs = ns.Select(e => e.Split().Last()).Where(e => e.EndsWith(".o")).ToArray();
            // 找到原始对象文件。注意，不同目录可能有同名对象文件
            var list = new List<String>();
            foreach (var obj in objs)
            {
                var fs = dir1.AsDirectory().GetAllFiles(obj, true).Union(dir2.AsDirectory().GetAllFiles(obj, true)).ToArray();
                if (fs.Length > 0)
                {
                    foreach (var item in fs)
                    {
                        if (!list.Contains(item.FullName))
                        {
                            list.Add(item.FullName);
                            break;
                        }
                    }
                    //list.Add(fs[0].FullName);
                }
            }
            Console.WriteLine("objs={0} list={1}", objs.Length, list.Count);
            list.Sort();

            // 链接打包
            var Ar = @"C:\Keil\ARM\ARMCC\bin\armar.exe";
            var lib = name.EnsureEnd(".lib").GetFullPath();
            var sb = new StringBuilder();
            sb.Append("--create -c");
            sb.AppendFormat(" -r \"{0}\"", lib);

            foreach (var item in list)
            {
                sb.Append(" ");
                sb.Append(item);
                Console.WriteLine(item);
            }

            var rs = Ar.Run(sb.ToString(), 3000, XTrace.WriteLine);
        }
    }
}