using AssetsTools.NET;
using AssetsTools.NET.Extra;
using PVRTexLib;
using AssetsTools.NET.Texture;
using System.Drawing;
using System.Drawing.Imaging;
using static ResourceModLoader.Mod.Item.ModJsonItem;

namespace ResourceModLoader.Utils
{
    public class SimpleLogProgress : IAssetBundleCompressProgress
    {
        public void SetProgress(float progress)
        {
            Log.StepProgress($"正在压缩合并结果 {((int)(progress * 100))}%");
        }
    }
    public class ResSRec
    {
        public List<byte[]> bytes;
        public long len;
        public string name;
        public byte[] ConcatAndGet()
        {
            byte[] result = new byte[len];
            int offset = 0;
            for (int i = 0; i < bytes.Count; i++)
            {
                byte[] current = bytes[i];
                Buffer.BlockCopy(current, 0, result, offset, current.Length);
                offset += current.Length;
            }
            return result;
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
            baseField["m_Name"].AsString = fileName;
            if (!SetAssetFieldForTexture(baseField, path)) return "";

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
        public static bool SetAssetFieldForTexture(AssetTypeValueField baseField,string path)
        {
            var encoded = Encode(path);
            if (encoded == null) return false;
            int width = encoded.Item1;
            int height = encoded.Item2;

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

            baseField["m_TextureSettings"]["m_FilterMode"].AsInt = 1;
            baseField["m_TextureSettings"]["m_Aniso"].AsInt = 1;
            baseField["m_TextureSettings"]["m_WrapU"].AsInt = 1;
            baseField["m_TextureSettings"]["m_WrapV"].AsInt = 1;
            baseField["m_TextureSettings"]["m_WrapW"].AsInt = 1;

            AssetTypeValueField image_data = baseField["image data"];
            image_data.Value.ValueType = AssetValueType.ByteArray;
            image_data.TemplateField.ValueType = AssetValueType.ByteArray;
            image_data.AsByteArray = encoded.Item3;

            return true;
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
                    new System.Drawing.Rectangle(0, 0, bitmap.Width, bitmap.Height),
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

        public static Tuple<bool, List<Tuple<string, string, string>>> MergeBundles(string originalPath, List<string> bundles, string save, Action<AssetsManager, BundleFileInstance, AssetsFileInstance[], Dictionary<long, string>[], List<List<Tuple<int, long, byte[], int?>>>>? post = null)
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

            var inBundleFileNames = bundle.file.GetAllFileNames();
            if (inBundleFileNames.Count == 0)
            {
                Log.FinalizeProgress();
                return new Tuple<bool, List<Tuple<string, string, string>>>(false, []);
            }
            var name0 = inBundleFileNames[0];
            ResSRec? resSRec = null;
            for (int i = 1; i < inBundleFileNames.Count; i++)
            {
                if (bundle.file.IsAssetsFile(i)) continue;
                bundle.file.GetFileRange(i, out long iStart, out long iLength);
                bundle.file.DataReader.Position = iStart;
                byte[] iBytes = bundle.file.DataReader.ReadBytes((int)iLength);
                resSRec = new ResSRec(){bytes=new List<byte[]> { iBytes }, len = iLength, name = $"archive:/{name0}/{inBundleFileNames[i]}" };
                break;
            }
            List<List<Tuple<int, long, byte[], int?>>> patches = new List<List<Tuple<int, long, byte[], int?>>>();
            foreach (string file in bundles)
            {
                Log.StepProgress(file, 1);
                var r = PatchBundle(manager,bundle, assets, file, patched, resSRec, save + ".temp1", conflictResults);
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
                foreach(var (ai,fi,p,type) in pl)
                {
                    if (type == null)
                        assets[ai].file.GetAssetInfo(fi).Replacer = new ContentReplacerFromBuffer(p);
                    else
                    {
                        var asif = AssetFileInfo.Create(assets[ai].file, fi, (int)type);
                        if (asif != null)
                        {
                            asif.Replacer = new ContentReplacerFromBuffer(p);
                            assets[ai].file.Metadata.AddAssetInfo(asif);
                        }
                    }
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

            using (FileStream fsw = File.OpenWrite(save + ".preload"))
            {
                AssetsFileWriter tmpWriter = new AssetsFileWriter(fsw);
                bundle.file.Write(tmpWriter);
            }
            bundle.file.Close();

            if (resSRec != null)
                ABMergeTransformStreamData(save, resSRec, patches);
            else
                File.Move(save + ".preload", save + ".uncompressed");

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
        private static List<Tuple<int,long, byte[], int?>>? PatchBundle(AssetsManager manager,BundleFileInstance bundleFileInst, AssetsFileInstance[] assets, string toLoad, Dictionary<long,string>[] patched,ResSRec? resS, string cacheFile,List<Tuple<string, string,string>> conflictResults)
        {
            List<Tuple<int,long, byte[], int?>> result = new List<Tuple<int,long, byte[],int?>>();
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
            bool found = false;
            for (int i = 0; i < ian.Count; i++)
            {
                if (incomingBundle.file.IsAssetsFile(i)) continue;
                //to patch资产有resS，但是现在的没有，这种情况下合并很复杂，暂时不处理
                if (resS == null) return null;
                //有多个resS，也暂时不处理
                if (found) return null;
                incomingBundle.file.GetFileRange(i, out long iStart, out long iLength);
                incomingBundle.file.DataReader.Position = iStart;
                byte[] iBytes = incomingBundle.file.DataReader.ReadBytes((int)iLength);
                resS.bytes.Add(iBytes);
                resS.len += iLength;
                found = true;
            }
            //topatch没有ress，但是现在的有，这情况直接加一个空的进去占位就可以了
            if (!found && resS != null)
                resS.bytes.Add([]);
            List<(int fileId,long pathId, byte[] buf)> toCopyList = new List<(int fileId, long pathId, byte[] buf)> ();
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

                    foreach (var incomingFile in incomingAssetsFile.AssetInfos)
                    {
                        var iField = incomingManager.GetBaseField(incomingAsset, incomingFile);
                        var iName = iField["m_Name"];
                        if (iName.IsDummy) continue;

                        long v2Start = incomingFile.GetAbsoluteByteOffset(incomingAssetsFile);
                        long v2Size = incomingFile.ByteSize;
                        incomingAssetsFile.Reader.Position = (int)v2Start;
                        var buf2 = incomingAssetsFile.Reader.ReadBytes((int)v2Size);

                        bool needCreate = true;
                        foreach (var file in asset.file.AssetInfos)
                        {
                            var oField = manager.GetBaseField(asset, file);
                            var oName = oField["m_Name"];
                            if (oName.IsDummy) continue;
                            if (!incomingContainers.ContainsKey(incomingFile.PathId) || !originalContainers.ContainsKey(file.PathId) || incomingContainers[incomingFile.PathId] != originalContainers[file.PathId])
                                continue;

                            if (iName.AsString != oName.AsString)
                                continue;

                            long v1Start = file.GetAbsoluteByteOffset(asset.file);
                            long v1Size = file.ByteSize;
                            asset.file.Reader.Position = (int)v1Start;
                            var buf1 = asset.file.Reader.ReadBytes((int)v1Size);

                            if (!buf1.Equals(buf2))
                            {

                                if (patched[ai].ContainsKey(file.PathId))
                                {
                                    conflictResults.Add(new Tuple<string, string, string>(iName.AsString, toLoad, patched[ai][file.PathId]));
                                    continue;
                                }
                                patched[ai][file.PathId] = toLoad;
                                result.Add(new Tuple<int, long, byte[], int?>(ai, file.PathId, buf2,null));
                                if (file.PathId == incomingFile.PathId)
                                {
                                    needCreate = false;
                                }
                                Log.StepProgress($"Patched {iName.AsString} -> {toLoad}", 0);
                            }
                            else
                            {
                                needCreate = false;
                            }
                        }
                        if (needCreate)
                        {
                            var existing = asset.file.GetAssetInfo(incomingFile.PathId);
                            if (existing == null)
                            {
                                result.Add(new Tuple<int, long, byte[], int?>(ai, incomingFile.PathId, buf2, incomingFile.TypeId));
                                Log.StepProgress($"Add {iName.AsString} -> {toLoad}", 0);
                            }else if(existing.TypeId == incomingFile.TypeId)
                            {
                                result.Add(new Tuple<int, long, byte[], int?>(ai, incomingFile.PathId, buf2,null));
                            }
                            else
                            {
                                string name = incomingFile.PathId.ToString();
                                var field = incomingManager.GetBaseField(incomingAsset, incomingFile);
                                if (field != null && !field["m_Name"].IsDummy)
                                    name = field["m_Name"].AsString;
                                conflictResults.Add(new Tuple<string, string, string>(iName.AsString, toLoad, name));
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

        private static void ABMergeTransformStreamData(string save,ResSRec resSRec, List<List<Tuple<int, long, byte[],int?>>> patches)
        {
            using FileStream fsr = File.OpenRead(save + ".preload");
            AssetsManager manager = new AssetsManager();
            var bundle = manager.LoadBundleFile(fsr);

            ulong offset = (ulong)resSRec.bytes[0].Length;
            for(int i = 0; i < patches.Count; i++)
            {
                foreach (var (fileIdx, pathId, _,_) in patches[i])
                {
                    var asf = manager.LoadAssetsFileFromBundle(bundle, fileIdx);
                    var asif = asf.file.GetAssetInfo(pathId);
                    var field = manager.GetBaseField(asf, asif);
                    if (field["m_StreamData"].IsDummy) continue;
                    if (field["m_StreamData"]["size"].AsULong == 0) continue;
                    field["m_StreamData"]["path"].AsString = resSRec.name;
                    field["m_StreamData"]["offset"].AsULong += offset;
                    if (!field["m_Name"].IsDummy)
                        Log.StepProgress("更新Streaming "+ field["m_Name"].AsString, 0);
                    asif.SetNewData(field);
                    bundle.file.BlockAndDirInfo.DirectoryInfos[fileIdx].SetNewData(asf.file);
                }

                offset += (ulong) resSRec.bytes[i + 1].Length;
            }
            var ian = bundle.file.GetAllFileNames();
            Log.StepProgress("更新并写回ResS", 0);
            for (int i = 0; i < ian.Count; i++)
            {
                if (bundle.file.IsAssetsFile(i)) continue;
                bundle.file.BlockAndDirInfo.DirectoryInfos[i].SetNewData(resSRec.ConcatAndGet());
                break;
            }

            using (FileStream fsw = File.OpenWrite(save + ".uncompressed"))
            {
                AssetsFileWriter tmpWriter = new AssetsFileWriter(fsw);
                bundle.file.Write(tmpWriter);
            }
            manager.UnloadAll();
            File.Delete(save + ".preload");
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

