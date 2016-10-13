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

            var pkg = new Package();
            pkg.ParseObj(false);
            pkg.ParseObj(true);
            pkg.ParseHeader();
            pkg.Build();

            Console.WriteLine("OK!");
            Console.ReadKey();
        }

    }

    class Package
    {
        public String Name { get; set; }
        public List<String> Objs { get; set; } = new List<String>();
        public List<String> Headers { get; set; }
        public String Ar { get; set; }
        public String Root { get; set; }
        public String Temp { get; set; }
        public String Output { get; set; }

        public Package()
        {
            Name = ".".AsDirectory().Name;

            var rt = "../SmartOS/";
            if (!Directory.Exists(rt.GetFullPath())) rt = "../" + rt;
            if (Directory.Exists(rt.GetFullPath())) Root = rt;

            // 临时目录
            //var tmp = XTrace.TempPath.CombinePath(Name);
            var tmp = XTrace.TempPath;
            var di = tmp.AsDirectory();
            if (di.Exists) di.Delete(true);
            Temp = tmp.EnsureDirectory(false);

            tmp = "{0}-SDK".F(Name);
            di = tmp.AsDirectory();
            if (di.Exists) di.Delete(true);
            Output = tmp.EnsureDirectory(false);
        }

        public void ParseObj(Boolean debug)
        {
            var map = ".map";

            // 调试版
            if (debug) map = "D" + map;

            // 找到映射文件
            var mp = "List".AsDirectory().GetAllFiles("*" + map).FirstOrDefault();
            if (mp == null || !mp.Exists) return;

            Console.WriteLine(mp.FullName);
            var name = mp.Name.TrimEnd(".map");

            // 读取所有需要用到的对象文件
            var txt = File.ReadAllText(mp.FullName).Substring("Library Member Name", "Library Totals");
            var ns = txt.Trim().Split("\r\n");
            var objs = ns.Select(e => e.Split().Last()).Where(e => e.EndsWith(".o")).ToArray();

            // 找到原始对象文件
            var list = new List<String>();
            foreach (var obj in objs)
            {
                // 注意，不同目录可能有同名对象文件，所以需要同时在两个目录里面找
                var fs = FindObjs(obj, debug);
                if (fs.Length > 0)
                {
                    foreach (var item in fs)
                    {
                        // 无法识别需要哪个同名文件，全部加入
                        if (!list.Contains(item)) list.Add(item);
                    }
                }
            }

            Console.WriteLine("objs={0} list={1}", objs.Length, list.Count);

            // 引入基础库以及核心库的全部对象
            var bs = Root.CombinePath("Tool/Obj");
            if (debug) bs += "D";
            foreach (var item in bs.CombinePath("Core").AsDirectory().GetAllFiles("*.o"))
            {
                if (!list.Contains(item.FullName)) list.Add(item.FullName);
            }
            foreach (var item in bs.CombinePath("Kernel").AsDirectory().GetAllFiles("*.o"))
            {
                if (!list.Contains(item.FullName)) list.Add(item.FullName);
            }

            list.Sort();

            //Objs = list;

            // 链接打包
            name = "SmartOS";
            if (debug) name += "D";
            var lib = Temp.CombinePath(name + ".lib");
            BuildLib(lib, list);
            Objs.Add(lib);
        }

        String[] FindObjs(String obj, Boolean debug)
        {
            var dir1 = Root.CombinePath("Tool/Obj");
            var dir2 = Root.CombinePath("Platform/STM32F1/Obj");

            // 调试版
            if (debug)
            {
                dir1 += "D";
                dir2 += "D";
            }

            var fs = dir1.AsDirectory().GetAllFiles(obj, true).Union(dir2.AsDirectory().GetAllFiles(obj, true)).ToArray();
            return fs.Select(e => e.FullName).ToArray();
        }

        FileInfo[] _headers;
        String[] FindHeaders(String header)
        {
            if (_headers == null) _headers = Root.AsDirectory().GetAllFiles("*.h", true).ToArray();

            return _headers.Where(e => e.Name.EqualIgnoreCase(header) || e.FullName.EqualIgnoreCase(header) || e.FullName.EndsWithIgnoreCase(header.EnsureStart("\\"))).Select(e => e.FullName).ToArray();
        }

        public void ParseHeader()
        {
            // 分析cpp文件得到头文件
            var headers = new List<String>();
            foreach (var item in ".".AsDirectory().GetAllFiles("*.cpp"))
            {
                GetHeaders(item.FullName, headers);
            }

            Headers = headers;
        }

        Int32 GetHeaders(String file, ICollection<String> headers)
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

                            count += GetHeaders(fs[0], headers);
                        }
                    }
                    else
                        Console.WriteLine("无法找到 {0}", h);
                }
            }

            return count;
        }

        public void Build()
        {
            var tmp = Temp;
            var sos = Output.CombinePath("SmartOS").EnsureDirectory(true);

            // 拷贝库文件
            foreach (var item in Objs)
            {
                var dst = sos.CombinePath(Path.GetFileName(item));
                dst.EnsureDirectory(true);
                if (File.Exists(dst)) File.Delete(dst);
                File.Move(item, dst);
            }

            // 拷贝头文件
            var root = Root.GetFullPath();
            foreach (var item in Headers)
            {
                var dst = item.TrimStart(root).TrimStart("/");
                dst = sos.CombinePath(dst);
                dst.EnsureDirectory(true);
                File.Copy(item, dst, true);
            }

            // 拷贝编译脚本
            var tool = sos.CombinePath("Tool");
            var src = root.CombinePath("Tool").GetFullPath();
            src.AsDirectory().CopyTo(tool, "MDK.cs", false, XTrace.WriteLine);

            // 拷贝文档手册
            var doc = Output.CombinePath("Doc");
            foreach (var item in ".".AsDirectory().GetAllFiles("*.pdf"))
            {
                var dst = doc.CombinePath(item.Name).EnsureDirectory(true);
                item.CopyTo(dst, true);
            }

            // 拷贝固件库
            //var lib = Output.CombinePath("Lib");
            var lib = sos;
            src = Root.CombinePath("../Lib").GetFullPath();
            src.AsDirectory().CopyTo(lib, "*.lib", false, XTrace.WriteLine);
            //src.AsDirectory().CopyTo(lib, "*.h", true, XTrace.WriteLine);

            // 拷贝例程
            var sm = Output.CombinePath("Sample");
            src = Root.CombinePath("../Sample").GetFullPath();
            src.AsDirectory().CopyTo(sm, "*.uvprojx;*.uvoptx;Build.cs", false, XTrace.WriteLine);
            ".".AsDirectory().CopyTo(sm, "*.cpp", false, XTrace.WriteLine);

            // 压缩打包
            var zip = "{0}_{1:yyyyMMddHHmmss}.zip".F(Output.AsDirectory().Name, DateTime.Now).GetFullPath();
            if (File.Exists(zip)) File.Delete(zip);
            ZipFile.CreateFromDirectory(Output, zip, CompressionLevel.Optimal, false);

            //// 删除临时文件
            //Directory.Delete(sos, true);

            var di = tmp.AsDirectory();
            if (di.GetFiles().Length == 0) di.Delete(true);
        }

        void BuildLib(String lib, ICollection<String> objs)
        {
            if (Ar.IsNullOrEmpty()) Ar = @"D:\Keil\ARM\ARMCC\bin\armar.exe";
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
    }
}