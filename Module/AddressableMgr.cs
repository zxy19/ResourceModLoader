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
        /// <summary>匹配 Unity External 中的 MonoScript CAB 名（形如 CAB- + 32 位十六进制）。</summary>
        private static readonly Regex MonoScriptCabNameRegex = new(@"CAB-[0-9a-f]{32}", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private Random random = new Random();
        List<ContentCatalogData> contentCatalogDatas = new List<ContentCatalogData>();
        List<string> contentCatalogPath = new List<string>();
        List<bool> createBackup = new List<bool>();
        List<Dictionary<string, ResourceLocation>> generatedAbDictList = new List<Dictionary<string, ResourceLocation>>();
        List<Tuple<string,string, string>> bundleRedirects = new List<Tuple<string,string, string>>();
        List<Tuple<string,string,string>> addressableRedirects = new List<Tuple<string,string, string>>();
        /// <summary>每个 catalog 上缓存「本服共享脚本 Bundle」探测结果。</summary>
        private readonly Dictionary<ContentCatalogData, ResourceLocation?> _localSharedScriptBundleCache = new();

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

        /// <summary>
        /// 向 catalog 注册新的 Addressable 名（mod.json Add）。
        /// 若同名已存在且 Container 非空，则走 <see cref="ForceOverwriteExistingAddEntry"/> 强制修补；
        /// 新建时会对齐跨服 MonoScript CAB、补全 Reference 的额外 Bundle 依赖，并避免空 InternalId。
        /// </summary>
        /// <param name="name">新 PrimaryKey（Addressable 名）。</param>
        /// <param name="bundleFile">mod 提供的内容 Bundle 路径。</param>
        /// <param name="container">写入的 InternalId；空则回退为 Reference 的 InternalId。</param>
        /// <param name="refName">用作模板的已有 Addressable 名（同类资源）。</param>
        public void NewAddressableName(string name, string bundleFile, string container, string refName)
        {
            // 已存在：仅当显式指定了 Container 时强制覆写，否则保持原条目不动
            if (IsAddressableName(name))
            {
                if (!string.IsNullOrEmpty(container))
                    ForceOverwriteExistingAddEntry(name, bundleFile, container, refName);
                return;
            }
            for(int i=0;i< contentCatalogDatas.Count;i++)
            {
                var ccd = contentCatalogDatas[i];
                string? resolvedRef = ResolveReferenceKeyOrLocalFallback(ccd, refName);
                if (resolvedRef == null)
                    continue;
                if (resolvedRef != refName)
                    Log.Warn($"Reference {refName} 不存在，改用本服 {resolvedRef}");

                var reference = ccd.Resources[resolvedRef]
                    .FirstOrDefault(r => r.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider");
                if (reference == null) continue;

                // 跨服：把 mod AB 内外来 MonoScript CAB 对齐到本服
                AlignModBundleMonoScriptCabToLocal(bundleFile, ccd, reference);

                // Container 未写时回退到 Reference 的 InternalId，避免写入空字符串导致白卡
                string internalId = string.IsNullOrEmpty(container) ? reference.InternalId : container;
                var rl = new ResourceLocation();
                rl.ProviderId = "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider";
                rl.InternalId = internalId;
                rl.PrimaryKey = name;
                rl.Type = reference.Type;
                rl.HashCode = random.Next();
                ResourceLocation? refDep = GetPrimaryBundleDependency(ccd, reference);
                if (refDep != null)
                {
                    var dep = getAbIdFor(i, bundleFile, refDep);
                    if (dep == null) continue;
                    rl.DependencyKey = dep.PrimaryKey;
                    rl.DependencyHashCode = dep.HashCode;
                    CopyExtraReferenceDependenciesOntoContent(ccd, reference, dep);
                    ccd.Resources[rl.PrimaryKey] = new List<ResourceLocation> { rl };
                    Log.SuccessPartial($"New {rl.PrimaryKey} InternalId={rl.InternalId}");
                }
            }
        }

        /// <summary>
        /// 强制覆写 catalog 中已存在的 Add 条目：写入 Container（InternalId）、改指向新 Bundle，
        /// 并同步对齐 MonoScript CAB、复制 Reference 的额外依赖。用于修空白卡或更新已注册 key。
        /// </summary>
        /// <param name="name">已存在的 Addressable 名。</param>
        /// <param name="bundleFile">新的内容 Bundle 路径。</param>
        /// <param name="container">强制写入的 InternalId（须非空才会被调用）。</param>
        /// <param name="refName">依赖结构模板的 Reference 名。</param>
        private void ForceOverwriteExistingAddEntry(string name, string bundleFile, string container, string refName)
        {
            for (int i = 0; i < contentCatalogDatas.Count; i++)
            {
                var ccd = contentCatalogDatas[i];
                if (!ccd.Resources.ContainsKey(name))
                    continue;

                string? resolvedRef = ResolveReferenceKeyOrLocalFallback(ccd, refName);
                if (resolvedRef == null || !ccd.Resources.ContainsKey(resolvedRef))
                    continue;
                if (resolvedRef != refName)
                    Log.Warn($"Reference {refName} 不存在，改用本服 {resolvedRef}");

                var reference = ccd.Resources[resolvedRef]
                    .FirstOrDefault(r => r.ProviderId == "UnityEngine.ResourceManagement.ResourceProviders.BundledAssetProvider");
                if (reference == null)
                    continue;

                AlignModBundleMonoScriptCabToLocal(bundleFile, ccd, reference);

                ResourceLocation? refDep = GetPrimaryBundleDependency(ccd, reference);
                if (refDep == null)
                    continue;

                var dep = getAbIdFor(i, bundleFile, refDep);
                if (dep == null)
                    continue;
                CopyExtraReferenceDependenciesOntoContent(ccd, reference, dep);

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

        /// <summary>
        /// 取得 Reference 的主（第一个）Bundle 依赖，用作内容包模板。
        /// 优先读 Dependencies[0]，否则经 DependencyKey 查 catalog。
        /// </summary>
        /// <returns>主依赖；没有则 null。</returns>
        private static ResourceLocation? GetPrimaryBundleDependency(ContentCatalogData ccd, ResourceLocation reference)
        {
            if (reference.Dependencies != null && reference.Dependencies.Any())
                return reference.Dependencies[0];
            if (reference.DependencyKey != null && ccd.Resources.ContainsKey(reference.DependencyKey))
                return ccd.Resources[reference.DependencyKey].First();
            return null;
        }

        /// <summary>
        /// 列出 Reference 的全部 Bundle 依赖（内容包 + 共享脚本包等），供复制与解析 CAB 使用。
        /// </summary>
        /// <returns>依赖列表；无法解析则 null。</returns>
        private static List<ResourceLocation>? ListReferenceBundleDependencies(ContentCatalogData ccd, ResourceLocation reference)
        {
            if (reference.Dependencies != null && reference.Dependencies.Count > 0)
                return reference.Dependencies;
            if (reference.DependencyKey != null && ccd.Resources.ContainsKey(reference.DependencyKey))
                return ccd.Resources[reference.DependencyKey];
            return null;
        }

        /// <summary>
        /// 把 Reference 除主内容包外的其余依赖复制到 contentDep 所在依赖列表上；
        /// 并确保挂上本服 catalog 统计出的共享脚本 Bundle（不依赖固定模板名）。
        /// </summary>
        /// <param name="reference">官方模板条目。</param>
        /// <param name="contentDep">已为 mod 新建的主内容依赖。</param>
        private void CopyExtraReferenceDependenciesOntoContent(ContentCatalogData ccd, ResourceLocation reference, ResourceLocation contentDep)
        {
            var refDeps = ListReferenceBundleDependencies(ccd, reference);
            var contentList = ccd.Resources[contentDep.PrimaryKey];

            if (refDeps != null && refDeps.Count > 1)
            {
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

            // 即使 Reference 只有内容包（或跨服 Reference 依赖不全），也挂上本服共享脚本包
            var shared = TryFindLocalSharedScriptBundle(ccd);
            if (shared != null
                && !contentList.Any(d => Equals(d.PrimaryKey, shared.PrimaryKey)))
            {
                contentList.Add(shared);
                Log.SuccessPartial($"Attach local shared script dep {shared.PrimaryKey}");
            }
        }

        /// <summary>
        /// 解析 Add 的 Reference：本服 catalog 有该 key 则原样返回；
        /// 否则在本服 catalog 中找「依赖了共享脚本包且 InternalId 非空」的 BundledAsset，不写死模板名。
        /// </summary>
        /// <param name="refName">mod.json 中的 Reference。</param>
        /// <returns>实际使用的 Reference key；找不到则 null。</returns>
        private string? ResolveReferenceKeyOrLocalFallback(ContentCatalogData ccd, string refName)
        {
            if (ccd.Resources.ContainsKey(refName))
                return refName;

            var shared = TryFindLocalSharedScriptBundle(ccd);
            string? sharedPk = shared?.PrimaryKey?.ToString();

            string? fallbackAny = null;
            foreach (var kv in ccd.Resources)
            {
                string key = kv.Key?.ToString() ?? "";
                if (key.Length == 0 || key.StartsWith("patched.", StringComparison.OrdinalIgnoreCase))
                    continue;

                foreach (var loc in kv.Value)
                {
                    if (loc.ProviderId == null
                        || !loc.ProviderId.Contains("BundledAssetProvider")
                        || string.IsNullOrEmpty(loc.InternalId))
                        continue;

                    fallbackAny ??= key;

                    if (sharedPk == null)
                        continue;

                    var deps = ListReferenceBundleDependencies(ccd, loc);
                    if (deps == null || deps.Count <= 1)
                        continue;
                    if (deps.Skip(1).Any(d => Equals(d.PrimaryKey, sharedPk)))
                        return key;
                }
            }

            return fallbackAny;
        }

        /// <summary>
        /// 将 mod 内容 AB 内所有外来 MonoScript CAB 名，对齐为本服共享脚本包中的 CAB。
        /// 优先直接取 catalog 统计出的共享脚本 Bundle；失败时再回退到 Reference 依赖链。
        /// </summary>
        /// <param name="bundleFile">待改写的 mod Bundle 文件。</param>
        /// <param name="reference">可选：本服模板条目，作 CAB 解析回退。</param>
        private void AlignModBundleMonoScriptCabToLocal(string bundleFile, ContentCatalogData ccd, ResourceLocation? reference)
        {
            if (!File.Exists(bundleFile))
                return;

            string? targetCab = TryResolveLocalSharedMonoScriptCab(ccd);
            if (string.IsNullOrEmpty(targetCab) && reference != null)
                targetCab = TryResolveMonoScriptCabFromReferenceDeps(ccd, reference);

            if (string.IsNullOrEmpty(targetCab))
            {
                Log.Warn($"无法解析本服脚本 CAB，跳过 retarget: {Path.GetFileName(bundleFile)}");
                return;
            }

            int hits = ReplaceAllForeignMonoScriptCabsInBundle(bundleFile, targetCab);
            if (hits > 0)
                Log.SuccessPartial($"Retarget script CAB x{hits} -> {targetCab} ({Path.GetFileName(bundleFile)})");
        }

        /// <summary>
        /// 从本服 catalog 直接定位共享脚本 Bundle，并读取其中的 MonoScript CAB 名。
        /// </summary>
        private string? TryResolveLocalSharedMonoScriptCab(ContentCatalogData ccd)
        {
            var shared = TryFindLocalSharedScriptBundle(ccd);
            if (shared == null)
                return null;

            string path = ResolveDependencyBundleFilePath(shared);
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                Log.Warn($"本服共享脚本包无法落到磁盘: {shared.PrimaryKey} -> {path}");
                return null;
            }

            string? cab = TryFindMonoScriptCabInBundle(path);
            if (!string.IsNullOrEmpty(cab))
                Log.SuccessPartial($"Local shared script bundle {shared.PrimaryKey} CAB={cab}");
            return cab;
        }

        /// <summary>
        /// 统计本服 catalog：作为 BundledAsset「非首依赖」出现次数最多的 Bundle，视为共享脚本包。
        /// 不依赖固定 Addressable 模板名；结果按 catalog 缓存。
        /// </summary>
        private ResourceLocation? TryFindLocalSharedScriptBundle(ContentCatalogData ccd)
        {
            if (_localSharedScriptBundleCache.TryGetValue(ccd, out var cached))
                return cached;

            var freq = new Dictionary<string, (int Count, ResourceLocation Loc)>(StringComparer.Ordinal);
            foreach (var kv in ccd.Resources)
            {
                foreach (var loc in kv.Value)
                {
                    if (loc.ProviderId == null || !loc.ProviderId.Contains("BundledAssetProvider"))
                        continue;

                    var deps = ListReferenceBundleDependencies(ccd, loc);
                    if (deps == null || deps.Count <= 1)
                        continue;

                    for (int i = 1; i < deps.Count; i++)
                    {
                        var d = deps[i];
                        string pk = d.PrimaryKey?.ToString() ?? "";
                        if (pk.Length == 0
                            || pk.StartsWith("patched.", StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!freq.TryGetValue(pk, out var cur))
                            freq[pk] = (1, d);
                        else
                            freq[pk] = (cur.Count + 1, cur.Loc);
                    }
                }
            }

            ResourceLocation? best = null;
            if (freq.Count > 0)
            {
                best = freq.Values
                    .OrderByDescending(x => x.Count)
                    .ThenBy(x => EstimateDependencyBundleSize(x.Loc))
                    .First().Loc;
            }

            _localSharedScriptBundleCache[ccd] = best;
            return best;
        }

        /// <summary>
        /// 回退：从指定 Reference 的各 Bundle 依赖中解析 MonoScript CAB（按体积从小到大尝试）。
        /// </summary>
        private string? TryResolveMonoScriptCabFromReferenceDeps(ContentCatalogData ccd, ResourceLocation reference)
        {
            var deps = ListReferenceBundleDependencies(ccd, reference);
            if (deps == null) return null;

            foreach (var dep in deps.OrderBy(d => EstimateDependencyBundleSize(d)))
            {
                string path = ResolveDependencyBundleFilePath(dep);
                if (string.IsNullOrEmpty(path) || !File.Exists(path))
                    continue;
                string? cab = TryFindMonoScriptCabInBundle(path);
                if (!string.IsNullOrEmpty(cab))
                    return cab;
            }
            return null;
        }

        /// <summary>
        /// 估算依赖 Bundle 大小，供排序：越小越优先（共享 MonoScript 包通常更小）。
        /// 读不到 BundleSize 时返回较大默认值，排到后面。
        /// </summary>
        private static long EstimateDependencyBundleSize(ResourceLocation dep)
        {
            if (dep.Data is WrappedSerializedObject { Object: AssetBundleRequestOptions opt } && opt.BundleSize > 0)
                return opt.BundleSize;
            return 1_000_000;
        }

        /// <summary>
        /// 把 catalog 依赖的 InternalId 解析为磁盘上的 Bundle 文件路径。
        /// 支持绝对路径、RuntimePath 占位符、以及 LocalLow AssetBundles 缓存布局。
        /// </summary>
        /// <returns>可读文件路径；无法解析时返回空串或未展开的 InternalId。</returns>
        private string ResolveDependencyBundleFilePath(ResourceLocation dep)
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

        /// <summary>
        /// 在单个 Bundle 中查找 MonoScript CAB 名：遍历全部 assets 的 Externals，
        /// 命中第一个即返回（不收集全集）；AssetsTools 失败时再在整文件字节里搜明文。
        /// </summary>
        /// <param name="bundlePath">官方或缓存中的 Bundle 路径。</param>
        /// <returns>形如 CAB-xxxxxxxx… 的字符串；未找到则 null。</returns>
        private static string? TryFindMonoScriptCabInBundle(string bundlePath)
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
                        var m = MonoScriptCabNameRegex.Match(e.PathName ?? "");
                        if (m.Success) return m.Value;
                    }
                }
            }
            catch
            {
                // AssetsTools 失败时：整文件字节里搜第一个 CAB- 明文
            }

            try
            {
                byte[] data = File.ReadAllBytes(bundlePath);
                var m = MonoScriptCabNameRegex.Match(Encoding.ASCII.GetString(data));
                if (m.Success) return m.Value;
            }
            catch { }

            return null;
        }

        /// <summary>
        /// 将 Bundle 内所有与 targetCab 不同的 MonoScript CAB 名原地替换为目标值。
        /// 仅处理等长 CAB（保持文件长度不变）；已与目标相同的跳过。
        /// </summary>
        /// <param name="bundlePath">要改写的 mod Bundle。</param>
        /// <param name="targetCab">本服目标 CAB。</param>
        /// <returns>成功替换的次数；无改动则 0（且不写盘）。</returns>
        private static int ReplaceAllForeignMonoScriptCabsInBundle(string bundlePath, string targetCab)
        {
            byte[] target = Encoding.ASCII.GetBytes(targetCab);
            byte[] data = File.ReadAllBytes(bundlePath);
            string latin = Encoding.ASCII.GetString(data);
            var matches = MonoScriptCabNameRegex.Matches(latin);
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