using ResourceModLoader.Module;
using ResourceModLoader.Tool;
using ResourceModLoader.Tool.Creator;
using ResourceModLoader.Tool.SpriteAnimTool;
using ResourceModLoader.Tool.WWiseTool;
using ResourceModLoader.Utils;

namespace ResourceModLoader
{
    class Program
    {
        public static string VERSION = "0.1.16";
        public static GameModder Modder;
        public static bool isDevMode = false;
        static void Main(string[] args)
        {
            Modder = new GameModder(DiscoverGameDir(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)));
            if (!Modder.isValid)
            {
                Log.Wait();
                return;
            }
            if (Modder.addressableMgr.Loaded() == 0)
            {
                Log.Warn("当前状态无法进行该操作，请先启动游戏下载资源");
                Log.Wait();
                return;
            }
            Modder.Install(args);
            if (args.Length > 0)
            {
                if (args[0] == "tool")
                {
                    Tool(args);
                    return;
                }
                else if (args[0] == "dev")
                {
                    isDevMode = true;
                }
                else if (args[0] == "u" || args[0] == "update")
                {
                    var a = new AddressableUpdator(Modder);
                    a.startCollectUrl();
                    return;
                }
                else
                {
                    Log.Warn("未识别的参数");
                    return;
                }
            }
            TryCopy();
            do
            {
                Modder.ProcessMods(isDevMode);
                if(isDevMode)
                    Log.Info("继续运行将重新应用mod");
                Log.Wait();
            } while (isDevMode);
        }
        static void Tool(string[] args)
        {
            if (args.Length < 2)
            {
                PrintToolHelp();
                return;
            }

            string toolName = args[1];
            string[] remain = new string[Math.Max(args.Length - 2, 0)];
            Array.Copy(args, 2, remain, 0, remain.Length);

            if (toolName == "proto-export")
            {
                ProtoExportTool.Invoke(remain, Modder.addressableMgr, Modder.scan);
            }
            else if (toolName == "wwise-export")
            {
                WWiseExtractTool.Invoke(remain, Modder);
            }
            else if (toolName == "merge")
            {
                MergedExportTool.ExportRedirectedBundle(Modder);
            }
            else if (toolName == "create")
                CreateTool.Invoke(Modder);
            else if (toolName == "sprite-anim")
            {
                SpriteAnimTool.HandleSpriteAnimTool(remain);
            }
            else
            {
                Log.Warn($"未知的工具: {toolName}");
                PrintToolHelp();
            }
        }

        static void PrintToolHelp()
        {
            Log.Info("可用工具:");
            Log.Info("  create  - 模组包创建工具");
            Log.Info("  proto-export  - 导出Proto");
            Log.Info("  wwise-export  - 导出WWise SoundBnk");
            Log.Info("  merge  - 使用合并模式运行ModProcess，并将结果整理导出到exported");
            Log.Info("  sprite-anim   - AssetBundle动画导出/回填工具");
            Log.Info("");
            Log.Info("使用 'tool <toolName> help' 查看详细用法");
        }
        static string DiscoverGameDir(string user)
        {
            string currentPath = Directory.GetCurrentDirectory();
            if (Path.Exists(Path.Combine(currentPath, "possible_names.txt")))
            {
                const string DETECT_STR = "[Subsystems] Discovering subsystems at path ";
                var possibleNames = File.ReadAllText(Path.Combine(currentPath, "possible_names.txt")).Split("\n");
                foreach (var possibleName in possibleNames)
                {
                    var detectPath = Path.Combine(possibleName.Trim().Replace("{User}", user), "Player.log");
                    if (File.Exists(detectPath))
                    {
                        var unityLog = "";
                        try
                        {
                            unityLog = File.ReadAllText(detectPath);
                        }catch (Exception ex)
                        {
                            Log.Warn("无法打开" + possibleName + "的日志文件。如果对应的游戏正在运行，请关闭后重试");
                            continue;
                        }
                        var sp = unityLog.IndexOf(DETECT_STR);
                        if (sp >= 0)
                        {
                            sp += DETECT_STR.Length;
                            var ep = unityLog.IndexOf("\n", sp);
                            var path = unityLog.Substring(sp, ep - sp);
                            path = Path.GetDirectoryName(Path.GetDirectoryName(path));
                            return path;
                        }
                    }
                }
            }
            return currentPath;
        }
        static void TryCopy()
        {
            string currentPath = Directory.GetCurrentDirectory();

            if (Path.Exists(Path.Combine(currentPath, "copies.txt")))
            {
                string modsDirectory = Path.Combine(Modder.basePath, "mods");
                var copyItems = File.ReadAllText(Path.Combine(currentPath, "copies.txt")).Split("\n");
                foreach (var copyItem in copyItems)
                {
                    string sourceItem = copyItem;
                    string targetItem = copyItem;
                    if (copyItem.Contains(":"))
                    {
                        var t = copyItem.Split(':');
                        sourceItem= t[0];
                        targetItem = t[1];
                    }
                    var p = Path.Combine(currentPath, sourceItem.Trim());
                    var target = Path.Combine(modsDirectory, targetItem.Trim());
                    var dir = Path.GetDirectoryName(target);
                    if(!Path.Exists(dir))
                        Directory.CreateDirectory(dir);
                    if (File.Exists(p)) 
                    {
                        File.Copy(p, target,true);
                        Log.SuccessAll($"已复制文件 {target}");
                    }
                    else if (Directory.Exists(p))
                    {
                        if(Directory.Exists(target))
                            Directory.Delete(target,true);
                        Util.CopyDirectory(p, target,true);
                        Log.SuccessAll($"已复制目录 {target}");
                    }
                    else
                    {
                        Log.Warn($"要拷贝的文件{p}不存在");
                    }
                }
            }
        }
    }
}