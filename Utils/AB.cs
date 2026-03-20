using AssetsTools.NET;
using AssetsTools.NET.Extra;
using PVRTexLib;
using AssetsTools.NET.Texture;
using System.IO;
using static System.Net.Mime.MediaTypeNames;
using AssetsTools.NET.Texture.TextureDecoders.CrnUnity;
using System.Security.Cryptography;
using System.Reflection.Metadata;

namespace ResourceModLoader.Utils
{
    public class SimpleLogProgress : IAssetBundleCompressProgress
    {
        public void SetProgress(float progress)
        {
            Log.StepProgress($"Compressing {progress}");
        }
    }
    class AB
    {
        private static int PID = 172001;
        public static string createImageAbSingle(string path)
        {
            int pid = ++PID;
            string dirName = Path.GetDirectoryName(path);
            string bundleName = Path.GetFileNameWithoutExtension(path) + ".bundle";
            if (!Path.Exists(Path.Combine(dirName, "_generated")))
                Directory.CreateDirectory(Path.Combine(dirName, "_generated"));
            string pathAb = Path.Combine(dirName, "_generated", bundleName);
            AssetsManager manager = new AssetsManager();
            BundleFileInstance bundleInst = manager.LoadBundleFile(new MemoryStream(Resource1._ref), "ref.bundle");
            AssetsFileInstance assetsInst = manager.LoadAssetsFileFromBundle(bundleInst, 0);
            AssetsFile assetsFile = assetsInst.file;

            foreach (var type in Enum.GetValues(typeof(AssetClassID)))
                if ((AssetClassID)type != AssetClassID.AssetBundle)
                    assetsFile.GetAssetsOfType((int)type).ForEach(asset => { asset.SetRemoved(); });
            var abFileInfo = assetsFile.GetAssetsOfType(AssetClassID.AssetBundle).First();
            var abFileField = manager.GetBaseField(assetsInst, abFileInfo);
            abFileField["m_Name"].AsString = bundleName;
            abFileField["m_AssetBundleName"].AsString = bundleName;
            abFileField["m_Container.Array"].Children[0]["second"]["asset"]["m_PathID"].AsLong = pid;
            abFileField["m_PreloadTable.Array"].Children[0]["m_PathID"].AsLong = pid;

            abFileInfo.SetNewData(abFileField);
            var baseField = manager.CreateValueBaseField(assetsInst, (int)AssetClassID.Texture2D);

            var encoded = Encode(path);
            if (encoded == null) return "";
            int width = encoded.Item1;
            int height = encoded.Item2;

            baseField["m_Name"].AsString = Path.GetFileNameWithoutExtension(path);
            AssetTypeValueField m_StreamData = baseField["m_StreamData"];
            m_StreamData["offset"].AsInt = 0;
            m_StreamData["size"].AsInt = 0;
            m_StreamData["path"].AsString = "";

            baseField["m_Width"].AsInt = width;
            baseField["m_Height"].AsInt = height;


            baseField["m_TextureFormat"].AsInt = (int)TextureFormat.ARGB32;
            baseField["m_TextureDimension"].AsInt = 2;
            baseField["m_ImageCount"].AsInt = 1;
            baseField["m_MipCount"].AsInt = 1;
            baseField["m_ForcedFallbackFormat"].AsInt = 4;
            baseField["m_CompleteImageSize"].AsInt = encoded.Item3.Length;


            AssetTypeValueField image_data = baseField["image data"];
            image_data.Value.ValueType = AssetValueType.ByteArray;
            image_data.TemplateField.ValueType = AssetValueType.ByteArray;
            image_data.AsByteArray = encoded.Item3;
            var newInfo = AssetFileInfo.Create(assetsFile, pid, (int)AssetClassID.Texture2D);
            newInfo.SetNewData(baseField);

            assetsFile.Metadata.AddAssetInfo(newInfo);

            bundleInst.file.BlockAndDirInfo.DirectoryInfos[0].Name = "CAB-" + bundleName;
            bundleInst.file.BlockAndDirInfo.DirectoryInfos[0].SetNewData(assetsFile);
            while (bundleInst.file.BlockAndDirInfo.DirectoryInfos.Count > 1)
                bundleInst.file.BlockAndDirInfo.DirectoryInfos.RemoveAt(1);

            if (Path.Exists(pathAb))
                File.Delete(pathAb);
            using FileStream fs = File.OpenWrite(pathAb);
            AssetsFileWriter bundleWriter = new AssetsFileWriter(fs);
            bundleInst.file.Write(bundleWriter);
            return pathAb;
        }

        private static Tuple<int, int, byte[]>? Encode(string path)
        {
            try
            {
                using PVRTexture texture = new PVRTexture(path);
                // Check that PVRTexLib loaded the file successfully
                if (texture.GetTextureDataSize() == 0)
                {
                    return null;
                }
                texture.Flip(PVRTexLibAxis.Y);

                // Decompress texture to the standard RGBA8888 format.
                ulong RGBA8888 = PVRDefine.PVRTGENPIXELID4('a', 'r', 'g', 'b', 8, 8, 8, 8);

                if (!texture.Transcode(RGBA8888, PVRTexLibVariableType.UnsignedByteNorm, PVRTexLibColourSpace.BT2020))
                {
                    return null;
                }
                unsafe
                {
                    byte* result = (byte*)texture.GetTextureDataPointer();
                    byte[] resultArr = new byte[texture.GetTextureDataSize()];
                    fixed (byte* ptr = resultArr)
                    {
                        Buffer.MemoryCopy(result, ptr, texture.GetTextureDataSize(), texture.GetTextureDataSize());
                    }
                    return new Tuple<int, int, byte[]>((int)texture.GetTextureWidth(), (int)texture.GetTextureHeight(), resultArr);
                }
            }
            catch (Exception ex)
            {
                return null;
            }
        }

        public static void MergeBundles(string originalPath, List<string> bundles, string save)
        {
            Log.SetupProgress(bundles.Count);
            AssetsManager manager = new AssetsManager();
            BundleFileInstance bundle = manager.LoadBundleFile(originalPath, false);

            string localTmp = "";
            if (bundle.file.BlockAndDirInfo.DirectoryInfos.Find(t => t.DecompressedSize > 20 * 1024 * 1024) != null)
            {
                localTmp = save + ".temp";
                FileStream bundleStream = File.Open(localTmp, FileMode.Create);
                bundle.file.Unpack(new AssetsFileWriter(bundleStream));
                bundleStream.Close();

                manager = new AssetsManager();
                bundle = manager.LoadBundleFile(localTmp);
            }
            else
            {
                bundle = manager.LoadBundleFile(originalPath);
            }

            AssetsFileInstance[] assets = new AssetsFileInstance[bundle.file.GetAllFileNames().Count];
            HashSet<long>[] patched = new HashSet<long>[assets.Length];
            for (int i = 0; i < assets.Length; i++)
            {
                patched[i] = new HashSet<long>();
                assets[i] = manager.LoadAssetsFileFromBundle(bundle, i);
            }

            bundles.Reverse();
            foreach (string file in bundles)
            {
                Log.StepProgress("", 1);
                PatchBundle(manager, assets, file, patched, save + ".temp1");
            }
            Log.FinalizeProgress();

            MemoryStream[] streams = new MemoryStream[assets.Count()];
            for (int i = 0; i < assets.Count(); i++)
                if (assets[i] != null)
                {
                    streams[i] = new MemoryStream();
                    AssetsFileWriter w = new AssetsFileWriter(streams[i]);
                    assets[i].file.Write(w);
                }
            for (int i = 0; i < assets.Count(); i++)
                if (streams[i] != null)
                    bundle.file.BlockAndDirInfo.DirectoryInfos[i].Replacer = new ContentReplacerFromStream(streams[i]);
            using FileStream fs = File.OpenWrite(save);
            AssetsFileWriter bundleWriter = new AssetsFileWriter(fs);
            Log.SetupProgress(0);
            bundle.file.Pack(bundleWriter, AssetBundleCompressionType.LZ4, false, new SimpleLogProgress());
            Log.FinalizeProgress();
            if (localTmp != "")
            {
                bundle.file.Close();
                File.Delete(localTmp);
            }
        }
        private static void PatchBundle(AssetsManager manager, AssetsFileInstance[] assets, string toLoad, HashSet<long>[] patched, string cacheFile)
        {
            AssetsManager incomingManager = new AssetsManager();
            BundleFileInstance incomingBundle = incomingManager.LoadBundleFile(toLoad, false);

            string localTmp = "";
            if (incomingBundle.file.BlockAndDirInfo.DirectoryInfos.Find(t => t.DecompressedSize > 20 * 1024 * 1024) != null)
            {
                localTmp = cacheFile;
                FileStream bundleStream = File.Open(localTmp, FileMode.Create);
                incomingBundle.file.Unpack(new AssetsFileWriter(bundleStream));
                bundleStream.Close();

                incomingManager = new AssetsManager();
                incomingBundle = incomingManager.LoadBundleFile(localTmp);
            }
            else
            {
                incomingBundle = incomingManager.LoadBundleFile(toLoad);
            }
            for (int i = 0; i < incomingBundle.file.BlockAndDirInfo.DirectoryInfos.Count; i++)
            {
                AssetsFileInstance incomingAsset = incomingManager.LoadAssetsFileFromBundle(incomingBundle, i);
                if (incomingAsset == null)
                    continue;
                var incomingContainers = GetContainerDic(incomingManager, incomingAsset);
                AssetsFile incomingAssetsFile = incomingAsset.file;
                for (var ai = 0; ai < assets.Length; ai++)
                {
                    var asset = assets[ai];
                    if (asset == null)
                        continue;
                    var originalContainers = GetContainerDic(manager, asset);
                    foreach (var file in asset.file.AssetInfos)
                    {
                        if (patched[ai].Contains(file.PathId))
                            continue;
                        var oField = manager.GetBaseField(asset, file);
                        var oName = oField["m_Name"];
                        if (oName.IsDummy) continue;
                        foreach (var incomingFile in incomingAssetsFile.AssetInfos)
                        {
                            if (!incomingContainers.ContainsKey(incomingFile.PathId) || !originalContainers.ContainsKey(file.PathId) || incomingContainers[incomingFile.PathId] != originalContainers[file.PathId])
                                continue;
                            var iField = incomingManager.GetBaseField(incomingAsset, incomingFile);
                            var iName = iField["m_Name"];
                            if (iName.IsDummy) continue;

                            if (iName.AsString != oName.AsString)
                                continue;

                            Log.StepProgress(iName.AsString, 0);
                            long v1Start = file.GetAbsoluteByteOffset(asset.file);
                            long v1Size = file.ByteSize;
                            asset.file.Reader.Position = (int)v1Start;
                            var buf1 = asset.file.Reader.ReadBytes((int)v1Size);

                            long v2Start = incomingFile.GetAbsoluteByteOffset(incomingAssetsFile);
                            long v2Size = incomingFile.ByteSize;
                            incomingAssetsFile.Reader.Position = (int)v2Start;
                            var buf2 = incomingAssetsFile.Reader.ReadBytes((int)v2Size);


                            if (!buf1.Equals(buf2))
                            {
                                file.Replacer = new ContentReplacerFromBuffer(buf2);
                                patched[ai].Add(file.PathId);
                                Log.Debug($"Patched {iName.AsString} -> {toLoad}");
                            }
                        }
                    }
                }
            }
            if (localTmp != "")
            {
                incomingBundle.file.Close();
                File.Delete(localTmp);
            }
        }
        public static Dictionary<long, string> GetContainerDic(AssetsManager manager, AssetsFileInstance assets)
        {
            Dictionary<long, string> dic = new Dictionary<long, string>();
            foreach (var asset in assets.file.GetAssetsOfType(AssetClassID.AssetBundle))
            {
                var field = manager.GetBaseField(assets, asset);


                foreach (var containerDesc in field["m_Container.Array"].Children)
                {
                    var ctr = containerDesc["first"].AsString;
                    var pathId = containerDesc["second"]["asset"]["m_PathID"].AsLong;
                    dic[pathId] = ctr;
                }
            }
            return dic;
        }
    }
}

