using AssetsTools.NET;
using AssetsTools.NET.Extra;
using PVRTexLib;
using AssetsTools.NET.Texture;
using System.IO;
using static System.Net.Mime.MediaTypeNames;
using AssetsTools.NET.Texture.TextureDecoders.CrnUnity;
using System.Security.Cryptography;
using System.Reflection.Metadata;
using ResourceModLoader.Mod;
using System.Drawing;
using System.Drawing.Imaging;

namespace ResourceModLoader.Utils
{
    public class SimpleLogProgress : IAssetBundleCompressProgress
    {
        public void SetProgress(float progress)
        {
            Log.StepProgress($"正在压缩合并结果 {((int)(progress * 100))}%");
        }
    }
    class AB
    {
        private static int PID = 172001;
        public static string CreateTextAbSingle(string path, string? fileName)
        {
            int pid = ++PID;
            string dirName = Path.GetDirectoryName(path);
            if (fileName == null)
                fileName = Path.GetFileNameWithoutExtension(path);
            string bundleName = fileName + ".bundle";
            if (!Path.Exists(Path.Combine(dirName, "_generated")))
                Directory.CreateDirectory(Path.Combine(dirName, "_generated"));
            string pathAb = Path.Combine(dirName, "_generated", bundleName);
            AssetsManager manager = new AssetsManager();
            BundleFileInstance bundleInst = manager.LoadBundleFile(new MemoryStream(Resource1.ref2), "ref2.bundle");
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
            var baseField = manager.CreateValueBaseField(assetsInst, (int)AssetClassID.TextAsset);


            baseField["m_Name"].AsString = fileName;
            baseField["m_Script"].AsByteArray= File.ReadAllBytes(path);

            var newInfo = AssetFileInfo.Create(assetsFile, pid, (int)AssetClassID.TextAsset);
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

        public static string CreateImageAbSingle(string path,string? fileName)
        {
            int pid = ++PID;
            string dirName = Path.GetDirectoryName(path);
            if (fileName == null)
                fileName = Path.GetFileNameWithoutExtension(path);
            string bundleName = fileName + ".bundle";
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

            baseField["m_Name"].AsString = fileName;
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
            baseField["m_ColorSpace"].AsInt = 1;
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

        public static Tuple<int, int, byte[]>? EncodeFromBitmap(string path)
        {
            try
            {
                using Bitmap original = new Bitmap(path);
                // 确保像素格式为 32 位 ARGB（实际 BGRA 顺序）
                Bitmap bitmap = original.PixelFormat == PixelFormat.Format32bppArgb
                    ? original
                    : new Bitmap(original.Width, original.Height, PixelFormat.Format32bppArgb);

                // 锁定像素数据
                BitmapData data = bitmap.LockBits(
                    new Rectangle(0, 0, bitmap.Width, bitmap.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);

                try
                {
                    int width = bitmap.Width;
                    int height = bitmap.Height;
                    int stride = data.Stride;
                    int bytesPerPixel = 4;

                    // 如果 stride 不等于 width * bytesPerPixel，需要复制到连续缓冲区
                    byte[] pixelData;
                    if (stride == width * bytesPerPixel)
                    {
                        // 直接使用扫描指针
                        pixelData = new byte[width * height * bytesPerPixel];
                        unsafe
                        {
                            fixed (byte* dest = pixelData)
                            {
                                Buffer.MemoryCopy((void*)data.Scan0, dest, pixelData.Length, pixelData.Length);
                            }
                        }
                    }
                    else
                    {
                        // 逐行复制，去除行填充
                        pixelData = new byte[width * height * bytesPerPixel];
                        unsafe
                        {
                            byte* src = (byte*)data.Scan0;
                            fixed (byte* dest = pixelData)
                            {
                                for (int y = 0; y < height; y++)
                                {
                                    int srcOffset = y * stride;
                                    int destOffset = y * width * bytesPerPixel;
                                    Buffer.MemoryCopy(src + srcOffset, dest + destOffset, width * bytesPerPixel, width * bytesPerPixel);
                                }
                            }
                        }
                    }

                    // 定义纹理头：使用 BGRA 顺序（因为 System.Drawing 的 32bppArgb 实际为 BGRA）
                    ulong bgra8888 = PVRDefine.PVRTGENPIXELID4('b', 'g', 'r', 'a', 8, 8, 8, 8);
                    using PVRTextureHeader header = new PVRTextureHeader(
                        bgra8888,
                        (uint)width,
                        (uint)height,
                        1,  // depth
                        1,  // mipmaps
                        1,  // array members
                        1   // faces
                    );

                    // 从像素数据创建纹理
                    unsafe
                    {
                        fixed (byte* ptr = pixelData)
                        {
                            using PVRTexture texture = new PVRTexture(header, ptr);

                            // 可选：翻转 Y 轴（如果需要与原方法保持一致）
                            texture.Flip(PVRTexLibAxis.Y);
                            ulong RGBA8888 = PVRDefine.PVRTGENPIXELID4('a', 'r', 'g', 'b', 8, 8, 8, 8);

                            // 转码为压缩格式
                            if (!texture.Transcode(RGBA8888,
                                                   PVRTexLibVariableType.UnsignedByteNorm,
                                                   PVRTexLibColourSpace.sRGB,
                                                   0, false))
                            {
                                return null;
                            }

                            // 获取压缩后的数据
                            byte* compressedData = (byte*)texture.GetTextureDataPointer(0);
                            int compressedSize = (int)texture.GetTextureDataSize(0);
                            byte[] result = new byte[compressedSize];
                            fixed (byte* dest = result)
                            {
                                Buffer.MemoryCopy(compressedData, dest, compressedSize, compressedSize);
                            }

                            return new Tuple<int, int, byte[]>((int)texture.GetTextureWidth(),
                                                               (int)texture.GetTextureHeight(),
                                                               result);
                        }
                    }
                }
                finally
                {
                    bitmap.UnlockBits(data);
                    if (bitmap != original) bitmap.Dispose();
                }
            }
            catch
            {
                return null;
            }
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
                try
                {
                    return EncodeFromBitmap(path);
                }catch (Exception ex2) { }
                return null;
            }
        }

        public static Tuple<bool, List<Tuple<string, string, string>>> MergeBundles(string originalPath, List<string> bundles, string save, Action<AssetsManager, BundleFileInstance, AssetsFileInstance[], Dictionary<long, string>[], List<List<Tuple<int, long, byte[]>>>>? post = null)
        {
            Log.Debug("开始修补" + originalPath);
            List<Tuple<string, string, string>> conflictResults = new List<Tuple<string, string, string>>();
            Log.SetupProgress(bundles.Count);
            AssetsManager manager = new AssetsManager();
            BundleFileInstance bundle = manager.LoadBundleFile(originalPath, false);
            bool result = true;

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
            Dictionary<long, string>[] patched = new Dictionary<long, string>[assets.Length];
            for (int i = 0; i < assets.Length; i++)
            {
                patched[i] = new Dictionary<long, string>();
                assets[i] = manager.LoadAssetsFileFromBundle(bundle, i);
            }

            List<List<Tuple<int, long, byte[]>>> patches = new List<List<Tuple<int, long, byte[]>>>();
            foreach (string file in bundles)
            {
                Log.StepProgress(file, 1);
                var r = PatchBundle(manager,bundle, assets, file, patched, save + ".temp1", conflictResults);
                if (r == null)
                {
                    result = false;
                    conflictResults.Clear();
                    conflictResults.Add(new Tuple<string, string, string>(file, "", ""));
                    break;
                }
                patches.Add(r);
            }
            if (!result) {
                Log.FinalizeProgress();
                return new Tuple<bool, List<Tuple<string, string, string>>>(result, conflictResults);
            }
            if (post != null)
                post(manager,bundle, assets, patched,patches);

            Log.FinalizeProgress();

            foreach (var pl in patches)
            {
                foreach(var (ai,fi,p) in pl)
                {
                    assets[ai].file.GetAssetInfo(fi).Replacer = new ContentReplacerFromBuffer(p);
                }
            }

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

            using (FileStream fsw = File.OpenWrite(save + ".uncompressed"))
            {
                AssetsFileWriter tmpWriter = new AssetsFileWriter(fsw);
                bundle.file.Write(tmpWriter);
            }
            bundle.file.Close();
            using FileStream fsr = File.OpenRead(save + ".uncompressed");
            AssetsFileReader bundleRead = new AssetsFileReader(fsr);
            bundle.file.Read(bundleRead);
            using FileStream fs = File.OpenWrite(save);
            AssetsFileWriter bundleWriter = new AssetsFileWriter(fs);
            Log.SetupProgress(0);
            bundle.file.Pack(bundleWriter, AssetBundleCompressionType.LZ4, false, new SimpleLogProgress());
            Log.FinalizeProgress();
            fsr.Close();
            File.Delete(save + ".uncompressed");            
            if (localTmp != "")
            {
                bundle.file.Close();
                File.Delete(localTmp);
            }
            return new Tuple<bool,List<Tuple<string,string,string>>>(result, conflictResults);
        }
        private static List<Tuple<int,long, byte[]>>? PatchBundle(AssetsManager manager,BundleFileInstance bundleFileInst, AssetsFileInstance[] assets, string toLoad, Dictionary<long,string>[] patched, string cacheFile,List<Tuple<string, string,string>> conflictResults)
        {
            List<Tuple<int,long, byte[]>> result = new List<Tuple<int,long, byte[]>>();
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
            var ian = incomingBundle.file.GetAllFileNames();
            var oan = bundleFileInst.file.GetAllFileNames();
            for (int i = 0; i < ian.Count; i++)
            {
                for(int j = 0; j < oan.Count; j++)
                {
                    if (ian[i] != oan[j]) continue;
                    if (incomingBundle.file.IsAssetsFile(i) != bundleFileInst.file.IsAssetsFile(i)) return null;
                    if (incomingBundle.file.IsAssetsFile(i)) continue;
                    incomingBundle.file.GetFileRange(i, out long iStart, out long iLength);
                    bundleFileInst.file.GetFileRange(j, out long oStart, out long oLength);
                    if (iLength != oLength) return null;
                    incomingBundle.file.DataReader.Position = iStart;
                    byte[] iBytes = incomingBundle.file.DataReader.ReadBytes((int)iLength);
                    byte[] oBytes = bundleFileInst.file.DataReader.ReadBytes((int)oLength);
                    if(iBytes != oBytes) return null;
                }
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

                                if (patched[ai].ContainsKey(file.PathId))
                                {
                                    conflictResults.Add(new Tuple<string, string, string>(iName.AsString, toLoad, patched[ai][file.PathId]));
                                    continue;
                                }
                                patched[ai][file.PathId] = toLoad;
                                result.Add(new Tuple<int, long, byte[]>(ai, file.PathId, buf2));
                                Log.StepProgress($"Patched {iName.AsString} -> {toLoad}",0);
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
            return result;
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

