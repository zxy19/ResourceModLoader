using AssetsTools.NET.Extra;
using ResourceModLoader.Mod.Patch;
using ResourceModLoader.Module;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResourceModLoader.Mod.Item
{
    class CommonPatchItem : IModItem
    {
        string name;
        string bundle;
        string container = "";
        string ext = "";
        List<string> source = new List<string>();
        List<IPatch> patchContexes = new List<IPatch>();    
        public CommonPatchItem(int priority,string source) : base(priority)
        {
            this.source = new List<string> { source };
            (this.name, this.bundle, this.container) = GetName(source);
            ext = Path.GetExtension(source);
        }

        private IPatch GetContext()
        {
            if (ext == ".proto") return new ProtobufPatch();
            if (ext == ".bin") return new BinPatch();
            if (ext == ".fgui") return new FUIPatch();
            return null;
        }
        public override bool MergeToThis(IModItem modItem)
        {
            if(modItem is CommonPatchItem cpi && cpi.name == name && cpi.container == container && cpi.bundle == bundle && cpi.ext == ext)
            {
                source.AddRange(cpi.source);
            }
            return base.MergeToThis(modItem);
        }
        public override bool RequirePatch(string name, string addressableName)
        {
            return name == this.bundle || (addressableName == this.name && this.name != "");
        }
        public override void PostPatch(string bundleName, string addressableName, AssetsManager manager, BundleFileInstance bundle, AssetsFileInstance[] assets, Dictionary<long, string>[] patched, List<List<Tuple<int, long, byte[]>>> patches)
        {
            if (ext == "" || (bundleName != this.bundle && addressableName != name)) return;
            foreach (var asset in assets)
            {
                var container = Utils.AB.GetContainerDic(manager, asset);
                foreach(var file in asset.file.AssetInfos)
                {
                    if (file.GetTypeId(asset.file) == (int)AssetClassID.AssetBundle) continue;
                    var field = manager.GetBaseField(asset, file);
                    if (field == null) continue;
                    var nameField = field["m_Name"];
                    if (nameField == null || nameField.IsDummy) continue;
                    if(nameField.AsString == this.name)
                    {
                        var patch = GetContext();

                        patch.Init(manager, asset, file);
                        foreach (var src in this.source)
                        {
                            Log.StepProgress($"{bundleName}应用修补{Path.GetFileName(src)}", 0);
                            if (patch.PerformPatch(src))
                                Report.AddTaintFile(src, bundleName);
                        }
                        patch.Finalize(manager, asset, file);
                        patchContexes.Add(patch);
                    }
                }
            }
        }
        public override void Apply(ModContext context)
        {
            foreach(IPatch patch in patchContexes)
            {
                patch.AfterPatch(this, context);
            }
        }
        public static bool IsValid(string source)
        {
            return Path.GetFileNameWithoutExtension(source).EndsWith(".patch") && GetName(source).Item2 != "";
        }
        public static Tuple<string,string,string> GetName(string source)
        {
            var s = Path.GetFileNameWithoutExtension(source);
            if (s.EndsWith(".patch"))
                s = s.Substring(0, s.Length - 6);
            var t= (s +"@@").Split('@');
            return new Tuple<string, string, string>(t[0], t[1], t[2]);
        }
    }
}
