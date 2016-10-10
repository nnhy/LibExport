using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
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
            PathHelper.BaseDirectory = @"E:\Auto\STM32F1\Apollo0901";

            Export(false);
            Export(true);

            Console.WriteLine("OK!");
            Console.ReadKey();
        }

        static void Export(Boolean debug)
        {
            var map = ".map";

            // 调试版
            if (debug) map = "D" + map;

            // 找到映射文件
            var mp = "List".AsDirectory().GetAllFiles("*" + map).FirstOrDefault();
            Console.WriteLine(mp.FullName);
            var name = mp.Name.TrimEnd(".map");

            // 读取所有需要用到的对象文件
            var txt = File.ReadAllText(mp.FullName).Substring("Library Member Name", "Library Totals");
            var ns = txt.Trim().Split("\r\n");
            var objs = ns.Select(e => e.Split().Last()).Where(e => e.EndsWith(".o")).ToArray();

            // 找到原始对象文件
            var list = new List<String>();
            var headers = new List<String>();
            foreach (var obj in objs)
            {
                // 注意，不同目录可能有同名对象文件，所以需要同时在两个目录里面找
                var fs = FindObjs(obj, debug);
                if (fs.Length > 0)
                {
                    foreach (var item in fs)
                    {
                        if (!list.Contains(item))
                        {
                            list.Add(item);
                            break;
                        }
                    }
                }

                // 查找头文件
                var header = Path.ChangeExtension(obj, ".h");
                fs = FindHeaders(header);
                if (fs.Length > 0) headers.Add(fs[0]);
            }

            // 分析cpp文件得到头文件
            headers.Clear();
            foreach (var item in ".".AsDirectory().GetAllFiles("*.cpp"))
            {
                GetHeaders(item.FullName, null, headers);
            }

            Console.WriteLine("objs={0} list={1} headers={2}", objs.Length, list.Count, headers.Count);
            list.Sort();
            headers.Sort();

            // 临时目录
            var tmp = XTrace.TempPath.CombinePath(name);
            if (Directory.Exists(tmp)) tmp.EnsureDirectory(false);

            // 链接打包
            var lib = tmp.CombinePath(name + ".lib");
            BuildLib(lib, list);

            // 拷贝头文件
            var root = "../SmartOS/".GetFullPath();
            foreach (var item in headers)
            {
                var dst = item.TrimStart(root).TrimStart("/");
                dst = tmp.CombinePath(dst);
                dst.EnsureDirectory(true);
                File.Copy(item, dst, true);
            }

            // 压缩打包头文件
            var zip = (name + ".zip").GetFullPath();
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(tmp, zip, CompressionLevel.Optimal, false);

            Directory.Delete(tmp, true);
        }

        static void BuildLib(String lib, ICollection<String> objs)
        {
            var Ar = @"D:\Keil\ARM\ARMCC\bin\armar.exe";
            lib.EnsureDirectory(true);

            var sb = new StringBuilder();
            sb.Append("--create -c");
            sb.AppendFormat(" -r \"{0}\"", lib);

            foreach (var item in objs)
            {
                sb.Append(" ");
                sb.Append(item);
                Console.WriteLine(item);
            }

            var rs = Ar.Run(sb.ToString(), 3000, XTrace.WriteLine);
        }

        static String[] FindObjs(String obj, Boolean debug)
        {
            var dir1 = "../SmartOS/Tool/Obj";
            var dir2 = "../SmartOS/Platform/STM32F1/Obj";

            // 调试版
            if (debug)
            {
                dir1 += "D";
                dir2 += "D";
            }

            var fs = dir1.AsDirectory().GetAllFiles(obj, true).Union(dir2.AsDirectory().GetAllFiles(obj, true)).ToArray();
            return fs.Select(e => e.FullName).ToArray();
        }

        static FileInfo[] _headers;
        static String[] FindHeaders(String header)
        {
            if (_headers == null)
            {
                var dir1 = "../SmartOS/";
                _headers = dir1.AsDirectory().GetAllFiles("*.h", true).ToArray();
            }

            return _headers.Where(e => e.Name.EqualIgnoreCase(header) || e.FullName.EqualIgnoreCase(header) || e.FullName.EndsWithIgnoreCase(header.EnsureStart("\\"))).Select(e => e.FullName).ToArray();
        }

        static Int32 GetHeaders(String file, ICollection<String> list, ICollection<String> headers)
        {
            if (!File.Exists(file)) return 0;

            var root = Path.GetDirectoryName(file);

            var count = 0;
            var lines = File.ReadAllLines(file);
            foreach (var item in lines)
            {
                var line = item.Trim();
                if (line.StartsWith("#include"))
                {
                    line = line.TrimStart("#include").Trim().Trim('\"', '<', '>').Trim();
                    //var h = root.CombinePath(line).GetFullPath();
                    var h = line.Replace("/", "\\");

                    var fs = FindHeaders(h);
                    if (fs.Length > 0)
                    {
                        if (!headers.Contains(fs[0]))
                        {
                            Console.WriteLine(fs[0]);
                            headers.Add(fs[0]);
                            count++;

                            count += GetHeaders(fs[0], list, headers);
                        }
                    }
                    else
                        Console.WriteLine("无法找到 {0}", h);
                }
            }

            return count;
        }
    }
}