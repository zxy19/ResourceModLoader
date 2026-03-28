using AddressablesTools.Catalog;
using AddressablesTools.Classes;
using AssetsTools.NET;
using AssetsTools.NET.Extra;
using ResourceModLoader.Utils;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResourceModLoader.Module
{
    class BundleScan
    {
        private AddressableMgr ccd;
        string DEBUG_CT = "";
        bool enableCache = false;
        string local;
        string cache;
        bool hasBuiltFileContainer = false;
        List<Tuple<string, string>> bundleFiles = new List<Tuple<string, string>>();
        List<string> bundleFilesNames = new List<string>();
        Dictionary<string, List<Tuple<string, string>>> bundleFileNameContainerList = new Dictionary<string, List<Tuple<string, string>>>();
        public BundleScan(AddressableMgr ccd, string local, string cache)
        {
            this.ccd = ccd;
            this.local = local;
            this.cache = cache;
            foreach(var f in ccd.GetAllBundles())
            {
                if(!Path.Exists(GetBundleLocalPath(f.Item1)))
                { continue; }
                this.bundleFiles.Add(f);
            }
            foreach(var (ff,_) in bundleFiles)
            {
                bundleFilesNames.Add(ff);
            }
        }
        private void InitFileNameContainerList()
        {
            if (hasBuiltFileContainer) return;
            Log.Warn("正在建立更加完善的索引来辅助匹配未知的文件。这可能会花费更多时间");
            Log.SetupProgress(bundleFiles.Count);
            foreach (var (ff,fn) in bundleFiles)
            {
                Log.StepProgress(ff);
                var (manager,asset)= GetBundle(ff);
                var fileContainers = GetFileContainerList(manager, asset);
                if (fileContainers.Count > 0)
                    bundleFileNameContainerList[ff] = fileContainers;
            }
            Log.FinalizeProgress("索引完成");
            hasBuiltFileContainer = true;
        }
        public List<string> GetAllBundleName()
        {
            return bundleFilesNames;
        }
        public Tuple<string, List<Tuple<string, string>>> CalculateToReplaceItems(string bundlePath)
        {
            AssetsManager manager = new AssetsManager();
            List<Tuple<string, string>> results = new List<Tuple<string, string>>();

            var incomingBundle = manager.LoadBundleFile(bundlePath);
            var asset = manager.LoadAssetsFileFromBundle(incomingBundle, 0);
            var assetFile = asset.file;
            var hasUnAddressable = false;

            var abdef = assetFile.GetAssetsOfType(AssetClassID.AssetBundle).First();
            var fab = manager.GetBaseField(asset, abdef);
            string bundleName = FindBundleFileName(fab["m_Name"].AsString);

            var targetAssetInfo = GetBundle(bundleName);
            if (targetAssetInfo == null)
            {
                var matchedBundle = MatchForKnownBundleByFileContainer(manager, asset);
                if (matchedBundle != "")
                {
                    Log.Info($"Bundle {bundleName} 被识别为 {matchedBundle} ");
                    Report.Warning(bundlePath, $"当前文件被识别为 {matchedBundle}，这不总是正确");
                    bundleName = matchedBundle;
                    targetAssetInfo = GetBundle(bundleName);
                }
            }
            if (targetAssetInfo != null)
            {
                hasUnAddressable = ScanForKnownBundle(manager, results, asset, targetAssetInfo.Item1, targetAssetInfo.Item2);
                targetAssetInfo.Item1.UnloadAllBundleFiles();
            }
            else
            {
                Log.Warn($"{bundleName} 无法对应到游戏中任何一个资产文件，它可能是一个过期资产。将尝试加载并重定向所有文件，如果游戏异常，优先考虑删除这个文件");
                Report.Warning(bundlePath, $"无法对应到游戏中任何一个资产文件，它可能是一个过期资产。将尝试加载并重定向所有文件，如果游戏异常，优先考虑删除这个文件");
                bundleName = "UNK";
                var container = AB.GetContainerDic(manager, asset);
                foreach (var assetInfo in assetFile.AssetInfos)
                {
                    if (assetInfo.GetTypeId(assetFile) == (int)AssetClassID.AssetBundle)
                        continue;
                    var bf = manager.GetBaseField(asset, assetInfo);
                    var nameObj = bf["m_Name"];
                    if (nameObj.IsDummy)
                        continue;
                    var addressableKey = nameObj.AsString;
                    if (addressableKey == null)
                        continue;
                    results.Add(new Tuple<string, string>(nameObj.AsString, container.GetValueOrDefault(assetInfo.PathId, "")));
                }
            }
            if (hasUnAddressable || results.Count == 0)
            {
                results.Clear();
            }
            manager.UnloadAllBundleFiles();

            return new Tuple<string, List<Tuple<string, string>>>(bundleName, results);
        }
        private string MatchForKnownBundleByFileContainer(AssetsManager manager, AssetsFileInstance asset)
        {
            InitFileNameContainerList();
            var fileContainers = GetFileContainerList(manager, asset);
            foreach (var bfcl in bundleFileNameContainerList)
            {
                int i = 0;
                int j = 0;

                while (i < fileContainers.Count && j < bfcl.Value.Count)
                {
                    if (fileContainers[i].Equals(bfcl.Value[j]))
                    {
                        i++;
                    }
                    j++;
                }
                if (i == fileContainers.Count)
                {
                    return bfcl.Key;
                }
            }
            return "";
        }
        private bool ScanForKnownBundle(AssetsManager manager, List<Tuple<string, string>> results, AssetsFileInstance asset, AssetsManager targetManager, AssetsFileInstance targetAsset)
        {
            bool hasUnAddressable = false;
            AssetsFile assetFile = asset.file;
            AssetsFile targetAssetFile = targetAsset.file;
            var container = AB.GetContainerDic(manager, asset);
            var containerTarget = AB.GetContainerDic(targetManager, targetAsset);
            foreach (var assetInfo in assetFile.AssetInfos)
            {
                if (assetInfo.GetTypeId(assetFile) == (int)AssetClassID.AssetBundle)
                    continue;
                var bf = manager.GetBaseField(asset, assetInfo);
                var nameObj = bf["m_Name"];
                if (nameObj.IsDummy)
                    continue;
                var addressableKey = nameObj.AsString;
                if (addressableKey == null)
                    continue;

                foreach (var targetAssetInfo in targetAssetFile.AssetInfos)
                {
                    if (containerTarget.GetValueOrDefault(targetAssetInfo.PathId, "") != container.GetValueOrDefault(assetInfo.PathId, ""))
                        continue;
                    var bf1 = manager.GetBaseField(targetAsset, targetAssetInfo);
                    var nameObj1 = bf1["m_Name"];
                    if (nameObj1.IsDummy)
                        continue;
                    var addressableKey1 = nameObj1.AsString;
                    if (addressableKey1 == null || addressableKey != addressableKey1)
                        continue;

                    long v1Start = assetInfo.GetAbsoluteByteOffset(assetFile);
                    long v1Size = assetInfo.ByteSize;
                    assetFile.Reader.Position = (int)v1Start;
                    var buf1 = assetFile.Reader.ReadBytes((int)v1Size);

                    long v2Start = targetAssetInfo.GetAbsoluteByteOffset(targetAssetFile);
                    long v2Size = targetAssetInfo.ByteSize;
                    targetAssetFile.Reader.Position = (int)v2Start;
                    var buf2 = targetAssetFile.Reader.ReadBytes((int)v2Size);


                    if (!buf1.Equals(buf2))
                    {
                        if (!ccd.IsAddressableName(addressableKey))
                        {
                            hasUnAddressable = true;
                            break;
                        }
                        results.Add(new Tuple<string, string>(nameObj.AsString, container.GetValueOrDefault(assetInfo.PathId, "")));
                    }
                    break;
                }
                if (hasUnAddressable) break;
            }

            return hasUnAddressable;
        }

        public List<Tuple<string, string>> GetFileContainerList(AssetsManager manager, AssetsFileInstance asset)
        {
            var assetFile = asset.file;
            var abdef = assetFile.GetAssetsOfType(AssetClassID.AssetBundle).First();
            var fab = manager.GetBaseField(asset, abdef);
            List<Tuple<string, string>> fileContainers = new List<Tuple<string, string>>();
            HashSet<long> pathIds = new HashSet<long>();
            foreach (var containerDesc in fab["m_Container.Array"].Children)
            {
                var ctr = containerDesc["first"];
                var file = assetFile.GetAssetInfo(containerDesc["second"]["asset"]["m_PathID"].AsLong);
                if (file == null)
                {
                    fileContainers.Clear();
                    break;
                }
                pathIds.Add(containerDesc["second"]["asset"]["m_PathID"].AsLong);
                var nameField = manager.GetBaseField(asset, file)["m_Name"];
                if (nameField.IsDummy) continue;
                fileContainers.Add(new Tuple<string, string>(ctr.AsString, nameField.AsString));
            }
            if (fileContainers.Count > 0)
            {
                fileContainers.Sort();
            }
            return fileContainers;
        }
        public string FindBundleFileName(string bundleMetaName)
        {
            string tfn = Path.GetFileNameWithoutExtension(bundleMetaName);
            foreach (var (ff,fn) in bundleFiles)
            {
                if (fn == tfn)
                    return ff;
            }
            return "";
        }
        public Tuple<AssetsManager, AssetsFileInstance>? GetBundle(string bundleName)
        {
            if (!ccd.IsAddressableName(bundleName))
                return null;
            var bundlePath = GetBundleLocalPath(bundleName);
            if (bundlePath == "") return null;
            AssetsManager managerOut = new AssetsManager();
            var bundle = managerOut.LoadBundleFile(bundlePath);
            var af = managerOut.LoadAssetsFileFromBundle(bundle, 0);
            if (af == null) return null;
            var result = new Tuple<AssetsManager, AssetsFileInstance>(managerOut, af);
            return result;
        }
        public Dictionary<string, List<Tuple<string, string>>> GetAllBundleContainerName()
        {
            InitFileNameContainerList();
            return bundleFileNameContainerList;
        }
        public string GetBundleLocalPath(string bundleName)
        {
            var rl = ccd.GetFirstAvailableResourceLocationList(bundleName);
            if (rl == null || rl.Count == 0) return "";
            var bundlePath = rl[0].InternalId;
            if (bundlePath.StartsWith("{App.WebServerConfig.Path}"))
            {
                if (rl[0].Data is WrappedSerializedObject wo && wo.Object is AssetBundleRequestOptions abro)
                {
                    string abn1 = Path.GetFileNameWithoutExtension(bundleName);
                    string abn2 = Path.GetFileNameWithoutExtension(abro.BundleName);
                    return Path.Combine(cache, abn2, abn1, "__data");
                }
                return "";
            }
            else if (bundlePath.StartsWith("{UnityEngine.AddressableAssets.Addressables.RuntimePath}"))
            {
                bundlePath = bundlePath.Replace("{UnityEngine.AddressableAssets.Addressables.RuntimePath}", Path.Combine(local, "StreamingAssets", "aa"));
            }
            return bundlePath;
        }
    }
}
