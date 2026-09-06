using AddressablesTools;
using AddressablesTools.Catalog;
using AddressablesTools.Classes;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace ResourceModLoader.Module
{
    class AddressableMgr
    {
        private static readonly Regex ScriptCabRegex = new(@"CAB-[0-9a-f]{32}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private Random random = new Random();
        List<ContentCatalogData> contentCatalogDatas = new List<ContentCatalogData>();
        List<string> contentCatalogPath = new List<string>();
        List<bool> createBackup = new List<bool>();
        List<Dictionary<string, ResourceLocation>> generatedAbDictList = new List<Dictionary<string, ResourceLocation>>();
        List<Tuple<string,string, string>> bundleRedirects = new List<Tuple<string,string, string>>();
        List<Tuple<string,string,string>> addressableRedirects = new List<Tuple<string,string, string>>();

        /// <summary>游戏 *_Data 目录，用于解析官方 AB 的 RuntimePath。</summary>
        public string? GameDataDir { get; set; }
        /// <summary>LocalLow AssetBundles 缓存目录。</summary>
        public string? CacheDir { get; set; }

        public List<Tuple<string, string, string>> GetBundleRedirects()
        {
            return bundleRedirects;
        }
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
            string genHash = path + ".modded_hash";
            string toload = path;
            bool needBackup = true;
            if (Path.Exists(refer) && Path.Exists(genHash))
            {
                string hash = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path)));
                string hashBefore = File.ReadAllText(genHash);
                if (hash == hashBefore)
                {
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
            generatedAbDictList.Add(new Dictionary<string, ResourceLocation>());
            createBackup.Add(needBackup);
        }
        public List<Tuple<string,string>> GetAllBundles()
        {
            List<Tuple<string,string>> results = new List<Tuple<string,string>>();
            HashSet<string> seen = new HashSet<string>();
            foreach(var ccd in contentCatalogDatas)
            {
                foreach(var rll in ccd.Resources)
                {
                    foreach(var rl in rll.Value)
                    {
                        if (rl.ProviderId != "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider")
                            continue;
                        if (seen.Contains(rl.PrimaryKey))
                            continue;
                        if(rl.Data is WrappedSerializedObject wo && wo.Object is AssetBundleRequestOptions abro)
                        {
                            results.Add(new Tuple<string, string>(rll.Key.ToString(), abro.BundleName));
                            seen.Add(rl.PrimaryKey);
                            break;
                        }
                    }
                }
            }
            return results;
        }
        public List<Tuple<string, string, int>> GetAllBundlesWithCatalog()
        {
            List<Tuple<string, string, int>> results = new List<Tuple<string, string, int>>();
            HashSet<string> seen = new HashSet<string>();
            for (int i = 0; i < contentCatalogDatas.Count; i++)
            {
                var ccd = contentCatalogDatas[i];
                foreach (var rll in ccd.Resources)
                {
                    foreach (var rl in rll.Value)
                    {
                        if (rl.ProviderId != "UnityEngine.ResourceManagement.ResourceProviders.AssetBundleProvider")
                            continue;
                        if (seen.Contains(rl.PrimaryKey))
                            continue;
                        if (rl.Data is WrappedSerializedObject wo && wo.Object is AssetBundleRequestOptions abro)
                        {
                            results.Add(new Tuple<string, string, int>(rll.Key.ToString(), abro.BundleName, i));
                            seen.Add(rl.PrimaryKey);
                            break;
                        }
                    }
                }
            }
            return results;
        }
        public void Reset()
        {
            List<string> toLoad = new List<string>();
            while(contentCatalogPath.Count > 0) { 
                string path = contentCatalogPath[0];
                contentCatalogPath.RemoveAt(0);
                contentCatalogDatas.RemoveAt(0);
                generatedAbDictList.RemoveAt(0);
                createBackup.RemoveAt(0);
                toLoad.Add(path);
            }
            generatedAbDictList.Clear();
            foreach (string path in toLoad) {
                Add(path);
            }
            bundleRedirects.Clear();
            addressableRedirects.Clear();
        }
        public List<Tuple<string,string>> GetAllResources()
        {
            List<Tuple<string, string>> result = new List<Tuple<string, string>>();

            foreach (ContentCatalogData data in contentCatalogDatas)
            {
                foreach(var locations in data.Resources) {
                    foreach(var location in locations.Value) {
                        ResourceLocation? firstDep = null;
                        if (location.Dependencies != null)
                        {
                            firstDep = location.Dependencies.First();
                        }
                        else if (location.DependencyKey != null)
                        {
                            firstDep = data.Resources[location.DependencyKey].First();
                        }

                        if (firstDep == null)
                            firstDep = location;

                        result.Add(new Tuple<string, string>(location.PrimaryKey, firstDep.PrimaryKey));
                    }
                } 
            }
            return result;
        }
        public bool IsAddressableName(string name)
        {
            foreach (ContentCatalogData data in contentCatalogDatas)
            {
                if (data.Resources.ContainsKey(name))
                    return true;
            }
            return false;
        }
        public List<ResourceLocation> GetFirstAvailableResourceLocationList(string name)
        {
            foreach (ContentCatalogData data in contentCatalogDatas)
            {
                if (!data.Resources.ContainsKey(name))
                    continue;

                var rl = data.Resources[name];
                if (rl != null && rl.Count > 0)
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
            for (int i = 0; i < contentCatalogDatas.Count; i++)
            {
                if (ApplyBundleModPreCCD(i, name, bundleFile, containerRedir, depReq))
                    patched = true;
            }
            if (patched) return;
            Log.Warn($"{name} 的来源位置和之前不同. 匹配来源{depReq}");
            foreach (ContentCatalogData ccd in contentCatalogDatas)
            {
                if (!ccd.Resources.ContainsKey(name))
                    continue;
                foreach (var location in ccd.Resources[name])
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
            foreach (ContentCatalogData ccd in contentCatalogDatas)
            {
                if (!ccd.Resources.ContainsKey(name))
                {
                    continue;
                }
                if (ccd.Resources[name].Count > 1)
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
                    bundleRedirects.Add(new Tuple<string, string, string>(name,location.InternalId, bundleFile));
                    location.InternalId = bundleFile;
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
                var rl = getAbIdFor(idx, bundleFile, firstDep);

                if (rl == null)
                {
                    Log.Warn($"处理中 {name}");
                    Log.Warn($"无法创建虚拟Bundle");
                    continue;
                }


                if (location.Dependencies != null)
                {
                    if (location.Dependencies.Any())
                        addressableRedirects.Add(new Tuple<string, string, string>(name, location.Dependencies.First().InternalId, bundleFile));
                    location.Dependencies.Clear();
                    location.Dependencies.Add(rl);
                }
                else if (location.DependencyKey != null)
                {
                    addressableRedirects.Add(new Tuple<string, string, string>(name, location.DependencyKey.ToString(), bundleFile));
                    location.DependencyKey = rl.PrimaryKey;
                    location.DependencyHashCode = rl.HashCode;
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
        private ResourceLocation? getAbIdFor(int idx, string path, ResourceLocation reference)
        {
            Dictionary<string, ResourceLocation> generatedAbDict = generatedAbDictList[idx];
            ContentCatalogData ccd = contentCatalogDatas[idx];

            if (generatedAbDict.ContainsKey(path))
                return generatedAbDict[path];

            var rl = new ResourceLocation();
            rl.ProviderId = reference.ProviderId;
            rl.InternalId = path;
            string hash = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path)));
            rl.PrimaryKey = "patched." + Path.GetFileNameWithoutExtension(path)+"."+ hash + ".bundle";
            rl.Type = reference.Type;
            rl.HashCode = random.Next();
            AssetBundleRequestOptions opt = new AssetBundleRequestOptions();
            opt.Hash = "";
            opt.BundleName = rl.PrimaryKey;
            if (reference.Data is WrappedSerializedObject { Object: AssetBundleRequestOptions abro, Type: SerializedType t })
            {
                opt.ComInfo = abro.ComInfo;
                opt.Crc = 0;
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

                if (needUpdateRef)
                {
                    if (File.Exists(refer))
                        File.Delete(refer);
                    File.Copy(path, refer);
                }

                if (path.EndsWith(".bundle"))
                    AddressablesCatalogFileParser.ToBundle(ccd, refer, path);
                else
                    File.WriteAllText(path, AddressablesCatalogFileParser.ToJsonString(ccd));

                if (File.Exists(genHash))
                    File.Delete(genHash);

                string hash = Convert.ToHexString(MD5.HashData(File.ReadAllBytes(path)));
                File.WriteAllText(genHash, hash);

                Console.WriteLine($"已保存 {path}@{hash}");
            }
        }

        public void NewAddressableName(string name, string bundleFile, string container, string refName)
        {
            // 已存在时：若 mod.json 指定了 Container，强制覆盖 InternalId，并刷新依赖 Bundle
            if (IsAddressableName(name))
            {
                if (!string.IsNullOrEmpty(container))
                    UpdateExistingAddressableAdd(name, bundleFile, container, refName);
                return;
            }
            for(int i=0;i< contentCatalogDatas.Count;i++)
            {
                var ccd = contentCatalogDatas[i];
                string? resolvedRef = ResolveReferenceKey(ccd, refName);
                if (resolvedRef == null)
                    continue;
                if (resolvedRef != refName)
                    Log.Warn($"Reference {refName} 不存在，改用本服 {resolvedRef}");

                var reference = ccd.Resources[resolvedRef]
                    .FirstOrDefault(r => r.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider");
                if (reference == null) continue;

                // 跨服：把 mod AB 的 MonoScript External CAB 改成与本服 Reference 一致
                RetargetModBundleScriptCab(bundleFile, ccd, reference);

                // Container 未写时回退到 Reference 的 InternalId，避免写入空字符串导致白卡
                string internalId = string.IsNullOrEmpty(container) ? reference.InternalId : container;
                var rl = new ResourceLocation();
                rl.ProviderId = "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider";
                rl.InternalId = internalId;
                rl.PrimaryKey = name;
                rl.Type = reference.Type;
                rl.HashCode = random.Next();
                ResourceLocation? refDep = GetBundledAssetDependency(ccd, reference);
                if (refDep != null)
                {
                    var dep = getAbIdFor(i, bundleFile, refDep);
                    if (dep == null) continue;
                    rl.DependencyKey = dep.PrimaryKey;
                    rl.DependencyHashCode = dep.HashCode;
                    AppendSiblingBundleDependencies(ccd, reference, dep);
                    ccd.Resources[rl.PrimaryKey] = new List<ResourceLocation> { rl };
                    Log.SuccessPartial($"New {rl.PrimaryKey} InternalId={rl.InternalId}");
                }
            }
        }

        /// <summary>
        /// 对已存在的 Add 条目强制写入 Container（InternalId），并指向新的 Bundle 文件。
        /// </summary>
        private void UpdateExistingAddressableAdd(string name, string bundleFile, string container, string refName)
        {
            for (int i = 0; i < contentCatalogDatas.Count; i++)
            {
                var ccd = contentCatalogDatas[i];
                if (!ccd.Resources.ContainsKey(name))
                    continue;

                string? resolvedRef = ResolveReferenceKey(ccd, refName);
                if (resolvedRef == null || !ccd.Resources.ContainsKey(resolvedRef))
                    continue;
                if (resolvedRef != refName)
                    Log.Warn($"Reference {refName} 不存在，改用本服 {resolvedRef}");

                var reference = ccd.Resources[resolvedRef]
                    .FirstOrDefault(r => r.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider");
                if (reference == null)
                    continue;

                RetargetModBundleScriptCab(bundleFile, ccd, reference);

                ResourceLocation? refDep = GetBundledAssetDependency(ccd, reference);
                if (refDep == null)
                    continue;

                var dep = getAbIdFor(i, bundleFile, refDep);
                if (dep == null)
                    continue;
                AppendSiblingBundleDependencies(ccd, reference, dep);

                foreach (var location in ccd.Resources[name])
                {
                    if (location.ProviderId != "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider")
                        continue;

                    location.InternalId = container;
                    if (location.Dependencies != null)
                    {
                        location.Dependencies.Clear();
                        location.Dependencies.Add(dep);
                        foreach (var sibling in ccd.Resources[dep.PrimaryKey].Skip(1))
                            location.Dependencies.Add(sibling);
                    }
                    else
                    {
                        location.DependencyKey = dep.PrimaryKey;
                        location.DependencyHashCode = dep.HashCode;
                    }
                    Log.SuccessPartial($"Force Container {name} InternalId={container}");
                }
            }
        }

        private static ResourceLocation? GetBundledAssetDependency(ContentCatalogData ccd, ResourceLocation reference)
        {
            if (reference.Dependencies != null && reference.Dependencies.Any())
                return reference.Dependencies[0];
            if (reference.DependencyKey != null && ccd.Resources.ContainsKey(reference.DependencyKey))
                return ccd.Resources[reference.DependencyKey].First();
            return null;
        }

        private static List<ResourceLocation>? GetReferenceDependencyList(ContentCatalogData ccd, ResourceLocation reference)
        {
            if (reference.Dependencies != null && reference.Dependencies.Count > 0)
                return reference.Dependencies;
            if (reference.DependencyKey != null && ccd.Resources.ContainsKey(reference.DependencyKey))
                return ccd.Resources[reference.DependencyKey];
            return null;
        }

        /// <summary>
        /// 官方 CriMana 通常依赖 [内容包, 共享 MonoScript 包]。getAbIdFor 只建了内容包，这里补上其余依赖。
        /// </summary>
        private static void AppendSiblingBundleDependencies(ContentCatalogData ccd, ResourceLocation reference, ResourceLocation contentDep)
        {
            var refDeps = GetReferenceDependencyList(ccd, reference);
            if (refDeps == null || refDeps.Count <= 1)
                return;

            var contentList = ccd.Resources[contentDep.PrimaryKey];
            for (int i = 1; i < refDeps.Count; i++)
            {
                var sibling = refDeps[i];
                if (sibling == null) continue;
                if (contentList.Any(d => d.InternalId == sibling.InternalId && Equals(d.PrimaryKey, sibling.PrimaryKey)))
                    continue;
                contentList.Add(sibling);
                Log.SuccessPartial($"Keep sibling dep {sibling.PrimaryKey} -> {sibling.InternalId}");
            }
        }

        private static string? ResolveReferenceKey(ContentCatalogData ccd, string refName)
        {
            if (ccd.Resources.ContainsKey(refName))
                return refName;

            string[] prefer = ["VHandCard_13020002", "VHandCard_13021002", "VHandCard_13020031"];
            foreach (var k in prefer)
            {
                if (!ccd.Resources.ContainsKey(k)) continue;
                if (ccd.Resources[k].Any(r => r.ProviderId.Contains("BundledAssetProvider") && !string.IsNullOrEmpty(r.InternalId)))
                    return k;
            }

            return ccd.Resources.Keys
                .OfType<string>()
                .Where(k => k.StartsWith("VHandCard_", StringComparison.Ordinal))
                .OrderBy(k => k)
                .FirstOrDefault(k => ccd.Resources[k].Any(r =>
                    r.ProviderId.Contains("BundledAssetProvider") && !string.IsNullOrEmpty(r.InternalId)));
        }

        /// <summary>
        /// 将 mod AB 内 External 的 CAB-xxxxxxxx 改写为本服 Reference 模板的脚本 CAB。
        /// 作者用任一服打的包，在玩家本机 RML 安装时自动适配。
        /// </summary>
        private void RetargetModBundleScriptCab(string bundleFile, ContentCatalogData ccd, ResourceLocation reference)
        {
            if (!File.Exists(bundleFile))
                return;

            string? targetCab = FindLocalScriptCab(ccd, reference);
            if (string.IsNullOrEmpty(targetCab))
            {
                Log.Warn($"无法解析本服脚本 CAB，跳过 retarget: {Path.GetFileName(bundleFile)}");
                return;
            }

            int hits = ReplaceScriptCabsInFile(bundleFile, targetCab);
            if (hits > 0)
                Log.SuccessPartial($"Retarget script CAB x{hits} -> {targetCab} ({Path.GetFileName(bundleFile)})");
        }

        private string? FindLocalScriptCab(ContentCatalogData ccd, ResourceLocation reference)
        {
            var deps = GetReferenceDependencyList(ccd, reference);
            if (deps == null) return null;

            // 优先小的共享脚本包，再试内容包
            foreach (var dep in deps.OrderBy(d => EstimateDepSize(d)))
            {
                string path = ResolveBundlePath(dep);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;
                string? cab = ReadFirstExternalScriptCab(path);
                if (!string.IsNullOrEmpty(cab))
                    return cab;
            }
            return null;
        }

        private static long EstimateDepSize(ResourceLocation dep)
        {
            // 共享 MonoScript 包通常很小；排前面优先读
            if (dep.Data is WrappedSerializedObject { Object: AssetBundleRequestOptions opt } && opt.BundleSize > 0)
                return opt.BundleSize;
            return 1_000_000;
        }

        private string ResolveBundlePath(ResourceLocation dep)
        {
            string id = dep.InternalId ?? "";
            if (string.IsNullOrEmpty(id))
                return "";

            if (Path.IsPathRooted(id) && File.Exists(id))
                return id;

            if (id.StartsWith("{UnityEngine.AddressableAssets.Addressables.RuntimePath}", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(GameDataDir))
            {
                string path = id.Replace(
                    "{UnityEngine.AddressableAssets.Addressables.RuntimePath}",
                    Path.Combine(GameDataDir, "StreamingAssets", "aa"),
                    StringComparison.Ordinal);
                if (File.Exists(path)) return path;
            }

            if (id.StartsWith("{App.WebServerConfig.Path}", StringComparison.Ordinal)
                && !string.IsNullOrEmpty(CacheDir)
                && dep.Data is WrappedSerializedObject { Object: AssetBundleRequestOptions abro })
            {
                string abn1 = Path.GetFileNameWithoutExtension(dep.PrimaryKey?.ToString() ?? "");
                string abn2 = Path.GetFileNameWithoutExtension(abro.BundleName ?? "");
                string path = Path.Combine(CacheDir, abn2, abn1, "__data");
                if (File.Exists(path)) return path;
            }

            return id;
        }

        private static string? ReadFirstExternalScriptCab(string bundlePath)
        {
            try
            {
                var am = new AssetsManager();
                var bun = am.LoadBundleFile(bundlePath);
                for (int i = 0; i < bun.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
                {
                    if (!bun.file.IsAssetsFile(i)) continue;
                    var asset = am.LoadAssetsFileFromBundle(bun, i);
                    if (asset == null) continue;
                    foreach (var e in asset.file.Metadata.Externals)
                    {
                        var m = ScriptCabRegex.Match(e.PathName ?? "");
                        if (m.Success) return m.Value;
                    }
                }
            }
            catch
            {
                // 压缩包里也可能有明文 CAB，再扫一遍字节
            }

            try
            {
                byte[] data = File.ReadAllBytes(bundlePath);
                var m = ScriptCabRegex.Match(Encoding.ASCII.GetString(data));
                if (m.Success) return m.Value;
            }
            catch { }

            return null;
        }

        private static int ReplaceScriptCabsInFile(string bundlePath, string targetCab)
        {
            byte[] target = Encoding.ASCII.GetBytes(targetCab);
            byte[] data = File.ReadAllBytes(bundlePath);
            string latin = Encoding.ASCII.GetString(data);
            var matches = ScriptCabRegex.Matches(latin);
            if (matches.Count == 0) return 0;

            var replaceFrom = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in matches)
            {
                if (!m.Value.Equals(targetCab, StringComparison.OrdinalIgnoreCase))
                    replaceFrom.Add(m.Value);
            }

            int hits = 0;
            foreach (var from in replaceFrom)
            {
                byte[] src = Encoding.ASCII.GetBytes(from);
                if (src.Length != target.Length) continue;
                for (int i = 0; i <= data.Length - src.Length; i++)
                {
                    bool ok = true;
                    for (int j = 0; j < src.Length; j++)
                    {
                        if (data[i + j] != src[j]) { ok = false; break; }
                    }
                    if (!ok) continue;
                    Buffer.BlockCopy(target, 0, data, i, target.Length);
                    hits++;
                    i += src.Length - 1;
                }
            }

            if (hits > 0)
                File.WriteAllBytes(bundlePath, data);
            return hits;
        }
    }
}