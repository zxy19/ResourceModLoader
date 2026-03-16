using System;
using System.IO;
using AddressablesTools;
using AddressablesTools.Binary;
using AddressablesTools.Catalog;
using AddressablesTools.Classes;
using AddressablesTools.JSON;

namespace ModLoader
{
    class Program
    {
        static void Main(string[] args)
        {
            Init();
            if (skip)
            {
                Console.WriteLine("当前状态无法进行该操作，请先启动游戏下载资源");
                return;
            }
            if(savePath == "" || ccd == null)
            {
                return;
            }
            ProcessMods();
            Save();
        }

        static void ProcessMods()
        {
            string modsDirectory = Path.Combine(Directory.GetCurrentDirectory(), "mods");

            if (!Directory.Exists(modsDirectory))
            {
                Directory.CreateDirectory(modsDirectory);
            }
            foreach (string modDir in Directory.GetDirectories(modsDirectory))
            {
                if (File.Exists(Path.Combine(modDir, "replace.txt")))
                {
                    Console.WriteLine($"Processing {modDir}");
                    ApplyMod(modDir);
                }
            }
        }
        static ContentCatalogData ccd;
        static string savePath = "";
        static string executable = "";
        static bool skip = false;
        static Dictionary<String,ResourceLocation> generatedAbDict = new Dictionary<String,ResourceLocation>();
        static void Init()
        {
            string localPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "AppData", "LocalLow"); 
            string currentPath = Directory.GetCurrentDirectory();
            string[] allSubDirs = Directory.GetDirectories(currentPath);
            string appName = "";
            foreach (string subDir in allSubDirs){
                if (subDir.EndsWith("_Data")){
                    appName = subDir.Substring(0,subDir.Length - 5);
                }
            }
            if(appName == "")
            {
                Console.Error.WriteLine("在游戏运行目录下执行该软件");
                return;
            }
            executable = Path.Combine(currentPath, appName + ".exe");
            Console.WriteLine($"使用 {executable} 作为可执行文件");
            string[] appData = File.ReadAllLines(Path.Combine(currentPath, appName+"_Data","app.info"));
            if(appData.Length < 2) {
                Console.Error.WriteLine("Appinfo 不合法");
                return;
            }
            Console.WriteLine($"{appData[0]} / {appData[1]} ");
            string presistDir = Path.Combine(localPath, appData[0], appData[1], "com.unity.addressables");
            string addressableSettings = File.ReadAllText(Path.Combine(currentPath, appName + "_Data", "StreamingAssets", "aa", "settings.json"));
            int offset1 = addressableSettings.IndexOf("/catalog_")+9;
            int offset2 = addressableSettings.IndexOf(".hash",offset1);
            string version = addressableSettings.Substring(offset1, offset2 - offset1);
            Console.WriteLine($"Game Version {version}");

            string currentHash = File.ReadAllText(Path.Combine(presistDir, "catalog_" + version + ".hash"));
            string lastHash = "";
            if(Path.Exists(Path.Combine(presistDir, "catalog_" + version + ".hash_modded")))
            {
                lastHash = File.ReadAllText(File.ReadAllText(Path.Combine(presistDir, "catalog_" + version + ".hash_modded")));
            }
            savePath = Path.Combine(presistDir, "catalog_" + version + ".json");
            if (!Path.Exists(savePath))
            {
                skip = true;
                return;
            }
            string catalogFile = Path.Combine(presistDir, "catalog_" + version + ".json.modded_bak");
            if(lastHash != currentHash || !Path.Exists(catalogFile))
            {
                File.Copy(savePath, catalogFile,true);
            }
            File.Copy(Path.Combine(presistDir, "catalog_" + version + ".hash"), Path.Combine(presistDir, "catalog_" + version + ".hash_modded"), true);
            ccd = AddressablesCatalogFileParser.FromJsonString(File.ReadAllText(catalogFile));
        }
        static void Save()
        {
            File.WriteAllText(savePath, AddressablesCatalogFileParser.ToJsonString(ccd));
        }
        static void ApplyMod(string modPath)
        {
            string[] files = File.ReadAllLines(Path.Combine(modPath,"replace.txt"));
            foreach (string file in files)
            {
                if (file.Trim().StartsWith("#"))
                    continue;
                string[] def = file.Split(':');
                if (def.Length < 2) 
                    continue;
                string name = def[0];
                string bundle= def[1];
                string bundleFile = Path.Combine(modPath, bundle);
                if (!Path.Exists(bundleFile))
                {
                    Console.WriteLine($"[W] {bundleFile} not exist");
                    continue;
                }

                if (!ccd.Resources.ContainsKey(name))
                {
                    Console.WriteLine($"[W] {name} not found in addressable");
                    continue;
                }

                foreach (var location in ccd.Resources[name])
                {
                    if (location.ProviderId != "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider")
                    {
                        Console.WriteLine($"[W] When processing {name}");
                        Console.WriteLine($"[W] Unsupported provider type {location.ProviderId}"); 
                        continue;
                    }
                    ResourceLocation? firstDep = null;
                    if (location.Dependencies != null)
                    {
                        firstDep = location.Dependencies.First();
                    }
                    else if (location.DependencyKey != null)
                    {
                        firstDep = ccd.Resources[location.DependencyKey].First();
                    }
                    if (firstDep == null)
                    {
                        Console.WriteLine($"[W] When processing {name}");
                        Console.WriteLine($"[W] No Dep Found");
                        continue;
                    }
                    var rl = getAbIdFor(bundleFile, firstDep);

                    if (rl== null)
                    {
                        Console.WriteLine($"[W] When processing {name}");
                        Console.WriteLine($"[W] Fail creating dummy Bundle");
                        continue;
                    }


                    if (location.Dependencies != null)
                    {
                        location.Dependencies.Clear();
                        location.Dependencies.Add(rl);
                    }
                    else if (location.DependencyKey != null)
                    {
                        location.DependencyKey = rl.PrimaryKey;
                    }
                }
            }
        }
        static ResourceLocation? getAbIdFor(string path, ResourceLocation reference)
        {
            if (generatedAbDict.ContainsKey(path))
                return generatedAbDict[path];

            var rl = new ResourceLocation();
            rl.ProviderId = reference.ProviderId;
            rl.InternalId = "file://" + path;
            rl.PrimaryKey = "patched." + reference.PrimaryKey;
            rl.Type = reference.Type;
            AssetBundleRequestOptions opt = new AssetBundleRequestOptions();
            opt.Hash = "";
            opt.BundleName = rl.PrimaryKey;
            if (reference.Data is WrappedSerializedObject { Object: AssetBundleRequestOptions abro, Type: SerializedType t })
            {
                opt.ComInfo = abro.ComInfo;
                rl.Data = new WrappedSerializedObject(t, opt);
            }
            else
            {
                return null;
            }

            ccd.Resources[rl.PrimaryKey] = new List<ResourceLocation> { rl };
            generatedAbDict[path] = rl;
            return rl;
        }
    }
}