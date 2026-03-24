using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Reflection.Metadata;
using System.Xml.Linq;
using AddressablesTools;
using AddressablesTools.Binary;
using AddressablesTools.Catalog;
using AddressablesTools.Classes;
using AddressablesTools.JSON;
using ResourceModLoader.Mod;
using ResourceModLoader.Mod.Item;
using ResourceModLoader.Module;
using ResourceModLoader.Tool;
using ResourceModLoader.Tool.Creator;
using ResourceModLoader.Utils;

namespace ResourceModLoader
{
    class Program
    {
        static string VERSION = "0.0.7";
        static void Main(string[] args)
        {

            Init();
            if (addressableMgr == null)
            {
                Log.Wait();
                return;
            }
            if (addressableMgr.Loaded() == 0)
            {
                Log.Warn("当前状态无法进行该操作，请先启动游戏下载资源");
                Log.Wait();
                return;
            }
            if (args.Length > 0)
            {
                if (args[0] == "tool")
                {
                    Tool(args);
                    return;
                }else if (args[0] == "dev")
                {
                    isDevMode = true;
                }
                else
                {
                    Log.Warn("未识别的参数");
                    return;
                }
            }
            InstallAndRecordInfo(args);
            TryCopy();
            scan.Scan();
            do
            {
                ProcessMods();
                ApplyAll();
                addressableMgr.Save();
                Report.Print(Path.Combine(basePath, "mods"));
                if (isDevMode)
                {
                    addressableMgr.Reset();
                    Report.Reset();
                    modContext = new ModContext(addressableMgr, scan);
                    Log.Info("继续运行将重新应用mod");
                }
                Log.Wait();
            } while (isDevMode);
        }
        static void Tool(string[] args)
        {
            string toolName = args[1];
            string[] remain = new string[Math.Max(args.Length - 2,0)];
            Array.Copy(args, 2,remain,0,remain.Length);
            if(toolName == "proto-export")
            {
                ProtoExportTool.Invoke(remain, addressableMgr, scan);
            }
            if (toolName == "create")
                CreateTool.Invoke(Path.Combine(basePath, "mods"), addressableMgr, scan,appName);
        }
        static void ProcessMods()
        {
            string modsDirectory = Path.Combine(basePath, "mods");
            Log.Info("扫描Mods");
            Log.SetupProgress(-1);
            ApplyMod(modsDirectory, 100);
            Log.FinalizeProgress("搜索结束");
        }
        static string basePath = "";
        static string executable = "";
        static string appName = "";
        static BundleScan scan;
        static AddressableMgr addressableMgr;
        static ModContext modContext;
        static bool isDevMode = false;
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
                string modsDirectory = Path.Combine(basePath, "mods");
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
        static void InstallAndRecordInfo(string[] args)
        {
            int result = 1;
            string lastInstall = "";
            bool firstRun = true;
            if (Path.Exists(Path.Combine(basePath, "rml.info")))
            {
                firstRun = false;
                var infos = (File.ReadAllText(Path.Combine(basePath, "rml.info")) + "\n\n").Split("\n");
                string tVer = infos[1];
                lastInstall = infos[2];
                string[] curVer = VERSION.Split(".");
                string[] installedVer = tVer.Split(".");
                result = 0;
                for (int i = 0; i < curVer.Length + 1 && i < installedVer.Length + 1; i++)
                {
                    if (i == curVer.Length || i == installedVer.Length) break;
                    if (int.Parse(curVer[i]) == int.Parse(installedVer[i])) continue;
                    if (int.Parse(curVer[i]) < int.Parse(installedVer[i]))
                        result = -1;
                    else
                        result = 1;
                    break;
                }
            }
            if(lastInstall != "" && !Path.Exists(Path.Combine(basePath, lastInstall)))
            {
                Log.Warn("虽然有安装记录，但是没有找到对应的可执行程序。重新安装当前版本");
                result = 1;
            }
            if (result == 0)
            {
                Log.Debug("当前版本 "+VERSION);
                return;
            }
            if (result < 0)
            {
                Log.Error("已经安装更新版本，使用更新的版本进行安装");
                Process.Start(Path.Combine(basePath, lastInstall), args).WaitForExit();
                Environment.Exit(1);
                return;
            }


            string self = Process.GetCurrentProcess().MainModule.FileName;
            string targetFileName = lastInstall;
            if(targetFileName == "")
                targetFileName = Path.GetFileName(self);
            if (Path.GetDirectoryName(self) != basePath && Path.GetDirectoryName(Path.GetDirectoryName(self)) != basePath)
            {
                if (lastInstall != "" && Path.Exists(Path.Combine(basePath, lastInstall)))
                {
                    try
                    {
                        File.Delete(Path.Combine(basePath, lastInstall));
                    }catch (Exception e)
                    {
                        Log.Warn("更新失败，无法删除目标文件。请确定不在运行 " + lastInstall);
                        Log.Warn("注意：请稍后再次尝试更新本程序，使用低版本modloader加载新版mod描述可能会产生不可控的影响");
                        return;
                    }
                }
                string dep = Path.Combine(Path.GetDirectoryName(self), "PVRTexLib.dll");
                if (!Path.Exists(Path.Combine(basePath, targetFileName)))
                {
                    File.Copy(self, Path.Combine(basePath, targetFileName));
                    if (Path.Exists(dep))
                        File.Copy(dep, Path.Combine(basePath, Path.GetFileName(dep)), true);

                    Log.SuccessAll("已将本程序拷贝到 " + Path.Combine(basePath, targetFileName));
                    if (firstRun)
                    {
                        Log.Info("将来如果要撤销该程序的影响，请到该目录下删除mods文件夹后再次运行目录下的该程序");
                        Log.Info("即将复制文件并修补游戏文件，如果你已经了解，请按下回车键来继续操作。这条信息之后不会再显示");
                        Log.Wait();
                    }
                }    
            }
            File.WriteAllText(Path.Combine(basePath, "rml.info"), "#这是ResourceModLoader的安装信息记录文件，用于管理版本\n" + VERSION + "\n" + targetFileName);
            Log.Info("版本号更新到" + VERSION);
        }
        static void Init()
        {
            string localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
            string currentPath = DiscoverGameDir(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
            for (int i = 0; i < 2; i++)
            {
                string[] allSubDirs = Directory.GetDirectories(currentPath);
                foreach (string subDir in allSubDirs)
                {
                    if (subDir.EndsWith("_Data"))
                    {
                        appName = subDir.Substring(0, subDir.Length - 5);
                    }
                }
                if (appName != "")
                {
                    break;
                }
                currentPath = Directory.GetParent(currentPath).FullName;
            }
            if (appName == "")
            {
                Log.Error("在游戏运行目录下安装该软件");
                return;
            }
            basePath = currentPath;
            executable = Path.Combine(currentPath, appName + ".exe");
            Log.Debug($"使用 {executable} 作为可执行文件");
            string[] appData = File.ReadAllLines(Path.Combine(currentPath, appName + "_Data", "app.info"));
            if (appData.Length < 2)
            {
                Log.Error("Appinfo 不合法");
                return;
            }
            Log.Debug($"{appData[0]} / {appData[1]} ");
            string presistDir = Path.Combine(localPath, appData[0], appData[1], "com.unity.addressables");


            string addressableSettings = File.ReadAllText(Path.Combine(currentPath, appName + "_Data", "StreamingAssets", "aa", "settings.json"));
            int offset1 = addressableSettings.IndexOf("/catalog_") + 9;
            int offset2 = addressableSettings.IndexOf(".hash", offset1);
            string version = addressableSettings.Substring(offset1, offset2 - offset1);
            Log.Debug($"Game Version {version}");

            addressableMgr = new AddressableMgr();
            try
            {
                addressableMgr.Add(Path.Combine(presistDir, "catalog_" + version + ".json"));
            }catch (Exception)
            {
                Log.Error("Addressable 初始化Catalog失败，请检查" + Path.Combine(presistDir, "catalog_" + version + ".json")+" 是否损坏");
                addressableMgr = null;
                return;
            }

            try
            {
                addressableMgr.Add(Path.Combine(currentPath, appName + "_Data", "StreamingAssets", "aa", "catalog.bundle"));
            }
            catch (Exception)
            {
                Log.Error("Addressable 初始化Catalog失败，请检查" + Path.Combine(currentPath, appName + "_Data", "StreamingAssets", "aa", "catalog.bundle") + " 是否损坏");
                addressableMgr = null;
                return;
            }

            scan = new BundleScan(addressableMgr, Path.Combine(currentPath, appName + "_Data"), Path.Combine(presistDir, "AssetBundles"));
            modContext = new ModContext(addressableMgr, scan);
            string modsDirectory = Path.Combine(basePath, "mods");

            if (!Directory.Exists(modsDirectory))
            {
                Directory.CreateDirectory(modsDirectory);
            }
        }
        static void ApplyMod(string modPath, int priority)
        {
            if (File.Exists(Path.Combine(modPath, "priority.txt")))
                try { priority = int.Parse(File.ReadAllText(Path.Combine(modPath, "priority.txt"))); } catch (Exception _) { }
            if (File.Exists(Path.Combine(modPath, "优先级.txt")))
                try { priority = int.Parse(File.ReadAllText(Path.Combine(modPath, "优先级.txt"))); } catch (Exception _) { }

            if (File.Exists(Path.Combine(modPath, "replace.txt")))
            {
                Log.StepProgress("Mod扫描 : " + Path.Combine(modPath, "replace.txt"));
                modContext.Add(new ReplaceTxtItem(priority, Path.Combine(modPath, "replace.txt")));
            }
            else if (File.Exists(Path.Combine(modPath, "mod.json")))
            {
                Log.StepProgress("Mod扫描 : " + Path.Combine(modPath, "mod.json"));
                modContext.Add(new ModJsonItem(priority, Path.Combine(modPath, "mod.json")));
            }
            else
            {
                var filesAll = Directory.GetFiles(modPath);
                Array.Sort(filesAll);
                foreach (var file in filesAll)
                {
                    Log.StepProgress("Mod扫描 : " + file);
                    Report.AddModFile(file);
                    if ((Path.GetExtension(file).ToLower() == ".bundle" || Path.GetFileName(file) == "__data"))
                    {
                        var list = scan.CalculateToReplaceItems(file);
                        modContext.Add(new BundleItem(priority, file, list.Item1, list.Item2));
                    }
                    if (Path.GetExtension(file).ToLower() == ".zip")
                    {
                        string tp = Zip.ExtractAndGetPath(file);
                        if (tp != "")
                            ApplyMod(tp, priority);
                    }
                    if (WrappableFileItem.IsValid(file, addressableMgr))
                        modContext.Add(new WrappableFileItem(priority, file));
                    if (CommonPatchItem.IsValid(file))
                        modContext.Add(new CommonPatchItem(priority, file));
                    if (FuiPatchItem.IsValid(file))
                        modContext.Add(new FuiPatchItem(priority, file));
                }
                var dirs = Directory.GetDirectories(modPath);
                Array.Sort(dirs);
                foreach (var dir in dirs)
                {
                    var dirName = Path.GetFileName(dir);
                    if (dirName != "_generated")
                    {
                        ApplyMod(Path.Combine(modPath, dir), priority);
                    }
                }
            }
        }
        static void MergeAndPatchBundles()
        {
            if(!Path.Exists(Path.Combine(basePath,"_generated")))
                Directory.CreateDirectory(Path.Combine(basePath, "_generated"));
            foreach(var bundleName in scan.GetAllBundleName())
            {
                var toPatch = modContext.CollectToPatch(bundleName);
                if (toPatch.Any() || modContext.IsRequiredPatch(bundleName)) {
                    var (result,conflicts) = AB.MergeBundles(scan.GetBundleLocalPath(bundleName), toPatch, Path.Combine(basePath, "_generated", bundleName), (m,b, a, p,r) => modContext.PostPatch(bundleName,m,b,a,p,r));

                    if (result)
                    {
                        modContext.Redirect(bundleName, Path.Combine(basePath, "_generated", bundleName), "", "", true);
                        foreach (var (name, i, c) in conflicts)
                        {
                            Report.Warning(i, $"在修补 {name} 时和 {c} 冲突");
                        }
                    }
                    else
                    {
                        foreach(var tp in toPatch)
                        {
                            foreach(var (name,_,_) in conflicts)
                                Report.Warning(tp, $" {bundleName} 的修补中存在 {name} 和当前的包不能兼容，无法完成修补");
                        }
                        if(toPatch.Count() == 1)
                        {
                            Report.Warning(toPatch[0], $" {bundleName} 被直接替换为当前文件，因为他是唯一符合要求的文件");
                            modContext.Redirect(bundleName, toPatch[0], "", "", true);
                        }
                    }
                }
            }
        }
        static void ApplyAll()
        {
            modContext.InitMod();
            modContext.Sort();
            MergeAndPatchBundles();
            modContext.ApplyAll();
        }
    }
}