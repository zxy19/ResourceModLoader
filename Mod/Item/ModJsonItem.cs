using AssetsTools.NET.Extra;
using ResourceModLoader.Module;
using ResourceModLoader.Utils;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Xml.Linq;
using static ResourceModLoader.Mod.Item.ModJsonItem;

namespace ResourceModLoader.Mod.Item
{
    class ModJsonItem : IModItem
    {
        public class ModDescription
        {
            public string? BaseDir { get; set; }
            public List<string>? Patch { get; set; }
            public List<Bundle>? Bundle { get; set; }
            public List<Add>? Add { get; set; }
            public List<Redirect>? Redirect { get; set; }
        }
        public class Bundle
        {
            public string Target { get; set; }
            public string File { get; set; }
        }
        public class Add
        {
            public string Name { get; set; }
            public string File { get; set; }
            public string? WrapType {  get; set; }
            public string Container { get; set; }
            public string Reference { get; set; }
        }
        public class Redirect
        {
            public string Name { get; set; }
            public string File { get; set; }
            public string? WrapType { get; set; }
            public string? Container { get; set; }
        }
        ModDescription content;
        string file;
        public ModJsonItem(int priority,string jsonPath) : base(priority)
        {
            file = jsonPath;
            content = JsonSerializer.Deserialize<ModDescription>(File.ReadAllText(jsonPath));
            if(content == null)
            {
                Report.Error(file, "非法的Mod JSON");
            }
        }
        private string GetBaseDir()
        {
            string b = Path.GetDirectoryName(file);
            if (content.BaseDir != null)
                b = Path.Combine(b, content.BaseDir);
            return b;
        }
        public override void Init(ModContext context, AddressableMgr addressableMgr, BundleScan bundleScan)
        {
            if (content != null && content.Patch != null)
            {
                foreach (var item in content.Patch)
                {
                    context.Add(new CommonPatchItem(priority, Path.Combine(GetBaseDir(), item)));
                }
            }
        }
        override public void Apply(ModContext context)
        {
            if (content == null) return;
            string b = GetBaseDir();
            if (content.Redirect != null)
                foreach (var redirect in content.Redirect)
                {
                    string file = Path.Combine(b, redirect.File);
                    string? container = redirect.Container;
                    if(redirect.WrapType != null)
                    {
                        (file,container) = AutoWrap(redirect.Name, file,redirect.WrapType);
                    }
                    if (file == "") continue;
                    if (container == null)
                        container = "";
                    context.Redirect(redirect.Name, file, container,"");
                }
            if (content.Add != null)
                foreach (var add in content.Add)
                {
                    string file = Path.Combine(b, add.File);
                    string? container = add.Container;
                    if(add.WrapType != null)
                        (file, container) = AutoWrap(add.Name, file, add.WrapType);
                    if (file == "") continue;
                    if (container == null)
                        container = "";
                    context.NewItem(add.Name, file, container, add.Reference);
                }
        }
        private Tuple<string,string> AutoWrap(string Name,string filePath,string WrapType)
        {
            string container="";
            string file="";
            if (WrapType != null)
            {
                if (WrapType == "text")
                {
                    file = AB.CreateTextAbSingle(filePath, Name);
                    container = "2";
                }
                else if (WrapType == "image")
                {
                    file = AB.CreateImageAbSingle(filePath, Name);
                    container = "d";
                }
                else
                {
                    Report.Warning(filePath, $"{WrapType}不是合法的包装类型(image,text)");
                    return new Tuple<string, string>("","");
                }
                file = Path.Combine(Path.GetDirectoryName(filePath), "_generated", file);
            }
            return new Tuple<string, string>(file, container);
        }
        override public List<string> GetToPatchBundles(string targetBundleName)
        {
            if (content == null || content.Bundle == null) return [];
            List<string> bundles = new List<string>();
            foreach (var patch in content.Bundle)
            {
                if(patch.Target == targetBundleName)
                    bundles.Add( Path.Combine(GetBaseDir(),patch.File));
            }
            return bundles;
        }
    }
}
