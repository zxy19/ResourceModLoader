using AddressablesTools;
using AddressablesTools.Catalog;
using AddressablesTools.Classes;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace ResourceModLoader
{
    class AddressableMgr
    {
        List<ContentCatalogData> contentCatalogDatas = new List<ContentCatalogData>();
        List<string> contentCatalogPath = new List<string>();
        List<bool> createBackup = new List<bool>();
        List<Dictionary<String, ResourceLocation>> generatedAbDictList = new List<Dictionary<string, ResourceLocation>>();

        public int Loaded()
        {
            return contentCatalogDatas.Count;
        }
        public void Add(string path)
        {
            if (!Path.Exists(path))
            {
                return;
            }
            string refer = path + ".modded_ref";
            string bak = path + ".modded_bak";
            string genHash= path + ".modded_hash";
            string toload = path;
            bool needBackup = true;
            if (Path.Exists(refer) && Path.Exists(genHash))
            {
                string hash = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path)));
                string hashBefore = File.ReadAllText(genHash);
                if(hash == hashBefore) {
                    toload = refer;
                    needBackup = false;
                }
            }
            if (!Path.Exists(bak))
            {
                File.Copy(path, bak);
            }
            if (path.EndsWith(".bundle"))
            {
                contentCatalogDatas.Add(AddressablesCatalogFileParser.FromBundle(toload));
            }
            else
            {
                contentCatalogDatas.Add(AddressablesCatalogFileParser.FromJsonString(File.ReadAllText(toload)));
            }
            contentCatalogPath.Add(path);
            generatedAbDictList.Add(new Dictionary<String, ResourceLocation>());
            createBackup.Add(needBackup);
        }
        public bool IsAddressableName(String name)
        {
            foreach (ContentCatalogData data in contentCatalogDatas)
            {
                if(data.Resources.ContainsKey(name))
                    return true;
            }
            return false;
        }
        public List<ResourceLocation> GetFirstAvailableResourceLocationList(String name)
        {
            foreach(ContentCatalogData data in contentCatalogDatas)
            {
                if (!data.Resources.ContainsKey(name))
                    continue;

                var rl = data.Resources[name];
                if(rl != null && rl.Count > 0)
                    return rl;
            }
            return new List<ResourceLocation>();
        }

        public void ApplyBundleMod(string name, string bundleFile, string containerRedir = "", string depReq = "")
        {
            if (!Path.Exists(bundleFile))
            {
                Log.Warn($"{bundleFile} 不存在");
                return;
            }

            if (!IsAddressableName(name))
            {
                Log.Warn($"{name} 不在Addressable系统中");
                return;
            }
            bool patched = false;
            for(int i = 0; i < contentCatalogDatas.Count; i++)
            {
                if(ApplyBundleModPreCCD(i, name, bundleFile, containerRedir, depReq))
                    patched = true;
            }
            if (patched) return;
            Log.Warn($"{name} 的来源位置和之前不同. 匹配来源{depReq}");
            foreach (ContentCatalogData ccd in contentCatalogDatas)
            {
                if (!ccd.Resources.ContainsKey(name))
                    continue;
                foreach(var location in ccd.Resources[name])
                {
                    ResourceLocation? firstDep = null;
                    if (location.Dependencies != null)
                    {
                        firstDep = location.Dependencies.First();
                    }
                    else if (location.DependencyKey != null)
                    {
                        firstDep = ccd.Resources[location.DependencyKey].First();
                    }
                    if (firstDep != null)
                        Log.Warn($" - 在 {firstDep.PrimaryKey} 中的 {location.InternalId}");
                }
            }
            if (IsOnlyMatchedResourceLocation(name))
            {
                Log.Warn($"上述 {name} 将被替换，因为他们是唯一满足名称条件的资源");
                for (int i = 0; i < contentCatalogDatas.Count; i++)
                {
                    if (ApplyBundleModPreCCD(i, name, bundleFile, containerRedir))
                        patched = true;
                }
            }
            if (!patched)
            {
                Log.Warn($"{name} 没有被应用到任何地址");
            }
        }
        private bool IsOnlyMatchedResourceLocation(string name)
        {
            foreach(ContentCatalogData ccd in contentCatalogDatas)
            {
                if (!ccd.Resources.ContainsKey(name))
                {
                    continue;
                }
                if(ccd.Resources[name].Count > 1)
                    return false;
            }
            return true;
        }
        private bool ApplyBundleModPreCCD(int idx, string name, string bundleFile, string containerRedir = "", string depReq = "")
        {
            ContentCatalogData ccd = contentCatalogDatas[idx];
            bool patched = false;
            if (!ccd.Resources.ContainsKey(name))
            {
                return false;
            }

            foreach (var location in ccd.Resources[name])
            {
                if (location.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider")
                {
                    location.InternalId =  bundleFile;
                    Log.SuccessPartial($"Bundle {name} --> {location.InternalId}");
                    patched = true;
                    continue;
                }
                else if (location.ProviderId != "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider")
                {
                    Log.Warn($"处理中 {name}");
                    Log.Warn($"不支持的提供者类型 {location.ProviderId}");
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
                    Log.Warn($"处理中 {name}");
                    Log.Warn($"没找到依赖的文件");
                    continue;
                }
                if (depReq != "" && depReq != firstDep.PrimaryKey && "patched." + depReq != firstDep.PrimaryKey)
                {
                    continue;
                }
                var rl = getAbIdFor(idx,bundleFile, firstDep);

                if (rl == null)
                {
                    Log.Warn($"处理中 {name}");
                    Log.Warn($"无法创建虚拟Bundle");
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

                if (containerRedir != "")
                {
                    location.InternalId = containerRedir;
                }
                Log.SuccessAll($"Resource {name} --> {rl.PrimaryKey}");
                patched = true;
            }
            return patched;
        }
        private ResourceLocation? getAbIdFor(int idx,string path, ResourceLocation reference)
        {
            Dictionary<string, ResourceLocation> generatedAbDict = generatedAbDictList[idx];
            ContentCatalogData ccd = contentCatalogDatas[idx];

            if (generatedAbDict.ContainsKey(path))
                return generatedAbDict[path];

            var rl = new ResourceLocation();
            rl.ProviderId = reference.ProviderId;
            rl.InternalId = path;
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
        public void Save()
        {
            for (int idx = 0; idx < contentCatalogDatas.Count; idx++)
            {
                ContentCatalogData ccd = contentCatalogDatas[idx];
                string path = contentCatalogPath[idx];
                bool needUpdateRef = createBackup[idx];

                string refer = path + ".modded_ref";
                string genHash = path + ".modded_hash";

                if(needUpdateRef)
                {
                    if(File.Exists(refer))
                        File.Delete(refer);
                    File.Copy(path, refer);
                }

                if (path.EndsWith(".bundle"))
                    AddressablesCatalogFileParser.ToBundle(ccd, refer, path);
                else
                    File.WriteAllText(path, AddressablesCatalogFileParser.ToJsonString(ccd));

                if(File.Exists(genHash))
                    File.Delete(genHash);

                string hash =Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path)));
                File.WriteAllText(genHash, hash);

                Console.WriteLine($"已保存 {path}@{hash}");
            }
        }
    }
}