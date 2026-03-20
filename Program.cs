using System;
using System.IO;
using System.Reflection.Metadata;
using AddressablesTools;
using AddressablesTools.Binary;
using AddressablesTools.Catalog;
using AddressablesTools.Classes;
using AddressablesTools.JSON;
using ResourceModLoader.Module;
using ResourceModLoader.util;
using ResourceModLoader.Utils;

namespace ResourceModLoader.Log
{
    class Program
    {
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
            ProcessMods();
            ApplyAll();
            addressableMgr.Save();
            Report.Print(Path.Combine(basePath, "mods"));
            Log.Wait();
        }
        static void StartGame()
        {
            if (executable == "") return;

        }
        static void ProcessMods()
        {
            string modsDirectory = Path.Combine(basePath, "mods");

            if (!Directory.Exists(modsDirectory))
            {
                Directory.CreateDirectory(modsDirectory);
            }
            ApplyMod(modsDirectory, 100);
        }
        static string basePath = "";
        static string executable = "";
        static BundleScan scan;
        static AddressableMgr addressableMgr;
        static List<Tuple<int, string, string, string, string>> collected = new List<Tuple<int, string, string, string, string>>();
        static void Init()
        {
            string localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow");
            string currentPath = Directory.GetCurrentDirectory();
            string appName = "";
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
            addressableMgr.Add(Path.Combine(presistDir, "catalog_" + version + ".json"));
            addressableMgr.Add(Path.Combine(currentPath, appName + "_Data", "StreamingAssets", "aa", "catalog.bundle"));
            scan = new BundleScan(addressableMgr, Path.Combine(currentPath, appName + "_Data"), Path.Combine(presistDir, "AssetBundles"));
        }
        static void ApplyMod(string modPath, int priority)
        {
            bool performCommonReplace = true;
            if (File.Exists(Path.Combine(modPath, "priority.txt")))
                try { priority = int.Parse(File.ReadAllText(Path.Combine(modPath, "priority.txt"))); } catch (Exception _) { }
            if (File.Exists(Path.Combine(modPath, "优先级.txt")))
                try { priority = int.Parse(File.ReadAllText(Path.Combine(modPath, "优先级.txt"))); } catch (Exception _) { }

            if (File.Exists(Path.Combine(modPath, "replace.txt")))
            {
                Report.AddModFile(Path.Combine(modPath, "replace.txt"));
                performCommonReplace = false;
                string[] files = File.ReadAllLines(Path.Combine(modPath, "replace.txt"));
                foreach (string file in files)
                {
                    if (file.Trim().StartsWith("#"))
                        continue;
                    string[] def = file.Split(':');
                    if (def.Length < 2)
                        continue;
                    string name = def[0];
                    string bundle = def[1];
                    string req = def.Length < 3 ? def[1] : def[2];
                    string bundleFile = Path.Combine(modPath, bundle);
                    Report.AddModFile(bundleFile);
                    Report.AddTaintFile(Path.Combine(modPath, "replace.txt"), name);
                    CollectApplyBundleMod(name, bundleFile, "", req, priority);
                }
            }
            else
            {
                var filesAll = Directory.GetFiles(modPath);
                Array.Sort(filesAll);
                foreach (var file in filesAll)
                {
                    if (Path.GetExtension(file).ToLower() == ".png" || Path.GetExtension(file).ToLower() == ".jpg" || Path.GetExtension(file).ToLower() == ".gif")
                    {
                        Report.AddModFile(file);
                        if (addressableMgr.IsAddressableName(Path.GetFileNameWithoutExtension(file)))
                        {
                            string bundle = AB.createImageAbSingle(file);
                            if (bundle != "")
                            {
                                CollectApplyBundleMod(Path.GetFileNameWithoutExtension(file), bundle, "d", "", priority);
                            }
                            else
                            {
                                Report.Error(file, "图片bundle创建失败");
                            }
                        }
                    }
                    if ((Path.GetExtension(file).ToLower() == ".bundle" || Path.GetFileName(file) == "__data") && performCommonReplace)
                    {
                        Report.AddModFile(file);
                        var list = scan.CalculateToReplaceItems(file);
                        foreach (var item in list.Item2)
                        {
                            CollectApplyBundleMod(item.Item1, file, item.Item2, list.Item1, priority);
                        }
                    }
                    if (Path.GetExtension(file).ToLower() == ".zip")
                    {
                        string tp = Zip.ExtractAndGetPath(file);
                        if (tp != "")
                            ApplyMod(tp, priority);
                    }
                }
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
        static void CollectApplyBundleMod(string name, string bundleFile, string containerRedir = "", string depReq = "", int priority = 100)
        {
            var t = new Tuple<int, string, string, string, string>(priority, name, bundleFile, containerRedir, depReq);
            if (!collected.Contains(t))
            {
                collected.Add(t);
            }
        }
        static void MergeAndPatchBundles()
        {
            List<Tuple<string, string>> merged = new List<Tuple<string, string>>();
            for (int i = 0; i < collected.Count; i++)
            {
                var (p, name, file, ctr, req) = collected[i];

                if (name.EndsWith(".bundle"))
                {
                    Report.AddTaintFile(file, name);
                    var bundlePath = scan.GetBundleLocalPath(name);
                    if (bundlePath == "")
                        continue;
                    Log.Debug($"合并修补 ->{name}");
                    Log.Debug($"  | - {file}");

                    collected.RemoveAt(i);
                    i--;
                    List<string> files = new List<string> { file };
                    for (int j = i + 1; j < collected.Count; j++)
                    {
                        if (collected[j].Item2 == name)
                        {
                            Report.AddTaintFile(collected[j].Item3, collected[j].Item2);
                            Log.Debug($"  | - {collected[j].Item3}");
                            files.Add(collected[j].Item3);
                            collected.RemoveAt(j);
                            j--;
                        }
                    }
                    if (!Path.Exists(Path.Combine(basePath, "_generated")))
                        Directory.CreateDirectory(Path.Combine(basePath, "_generated"));
                    AB.MergeBundles(bundlePath, files, Path.Combine(basePath, "_generated", name));
                    merged.Add(new Tuple<string, string>(name, Path.Combine(basePath, "_generated", name)));
                }
            }
            foreach (var (name, file) in merged)
            {
                collected.Add(new Tuple<int, string, string, string, string>(10000, name, file, "", ""));
            }
        }
        static void FilterPatches()
        {
            for (int i = 0; i < collected.Count; i++)
            {
                List<int> sameId = new List<int> { i };
                for (int j = i + 1; j < collected.Count; j++)
                {
                    if (collected[j].Item2 != collected[i].Item2 || collected[j].Item5 != collected[i].Item5) continue;
                    if (collected[j].Item1 > collected[i].Item1)
                    {
                        collected.RemoveAt(j);
                        j--;
                        continue;
                    }
                    sameId.Add(j);
                }
                if (sameId.Count > 1)
                {
                    Log.Debug($"{collected[i].Item2} 有 {sameId.Count} 个优先级相同的备选项");
                    foreach (int id in sameId)
                    {
                        Log.Debug($" |-> {collected[id].Item3}");
                    }
                    //对于多个相同优先级的相同项目的Filter
                    string localPath = scan.GetBundleLocalPath(collected[i].Item5);
                    if (localPath != "")
                    {
                        int maxEq = 0;
                        int maxId = -1;
                        for (int ii = 0; ii < sameId.Count; ii++)
                        {
                            if (collected[sameId[ii]].Item3 == "UNK") continue;
                            int curEq = Util.TailEqualLen(localPath, collected[sameId[ii]].Item3);
                            if (curEq > maxEq)
                            {
                                maxEq = curEq;
                                maxId = ii;
                            }
                        }
                        if (maxEq > 0)
                        {
                            Report.Warning(collected[sameId[maxId]].Item3, "项目来自多个同名项目的自动选择");
                            Log.Info($" --> 使用 {collected[sameId[maxId]].Item3}来执行重定向");
                            for (int ii = sameId.Count - 1; ii >= 0; ii--)
                            {
                                if (ii != maxId) collected.RemoveAt(sameId[ii]);
                            }
                        }
                    }
                }
            }
        }
        static void ApplyAll()
        {
            collected.Sort((a, b) => a.Item1 - b.Item1);
            MergeAndPatchBundles();
            FilterPatches();

            Dictionary<string, string> applied = new Dictionary<string, string>();
            foreach (var item in collected)
            {
                Log.Debug($"重定向 {item.Item2} -> {item.Item3}");
                if (applied.ContainsKey(item.Item2))
                {
                    Log.Warn($"{item.Item2} 正在被多次patch。上次重定向到 {applied[item.Item2]}");
                    Report.Error(applied[item.Item2], $"曾经被应用，但是被 {item.Item3} 覆盖");
                }
                applied[item.Item2] = item.Item3;
                if (!item.Item2.EndsWith(".bundle"))
                    Report.AddTaintFile(item.Item3, item.Item2);
                addressableMgr.ApplyBundleMod(item.Item2, item.Item3, item.Item4, item.Item5);
            }
        }
    }
}