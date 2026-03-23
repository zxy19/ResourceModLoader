using AssetsTools.NET;
using AssetsTools.NET.Extra;
using ProtoBuf;
using ProtoUntyped;
using ResourceModLoader.Module;
using ResourceModLoader.Utils;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static System.Runtime.InteropServices.JavaScript.JSType;

namespace ResourceModLoader.Mod.Patch
{
    class ProtobufPatch : IPatch
    {
        AssetTypeValueField field = null;
        private ReaderMessage protoObject;

        public void Init(AssetsManager manager, AssetsFileInstance assets, AssetFileInfo file)
        {
            field = manager.GetBaseField(assets, file);
            var data = field["m_Script"].AsByteArray;
            protoObject = new ReaderMessage(data);
        }

        public bool PerformPatch(string source)
        {
            var patches = File.ReadAllText(source).Split("\n");
            var applyList = new List<Tuple<int, int, byte[]>>();
            bool hasPat = false;
            foreach (var pat in patches)
            {
                if (!pat.Contains("="))
                    continue;
                var p = pat.Trim().Split("=", 2);
                var p1 = p[0].Trim();
                var target = p[1].Trim();

                try
                {
                    var result = protoObject.Query([p1]);
                    if (result.Length != 1)
                    {
                        Report.Warning(source, $"文本修补 {p1} 对应 {result.Length} 个候选，已跳过");
                        continue;
                    }
                    var r = (ReaderMessage)result[0];
                    byte[] bytes;
                    if (target.StartsWith("##"))
                        bytes = Convert.FromBase64String(target.Substring(2));
                    else
                        bytes = Encoding.UTF8.GetBytes(target);

                    r.Replace(new ReaderMessage(bytes, r.Path, r.Parent));

                    hasPat = true;
                }
                catch (Exception e)
                {
                    Report.Warning(source, $"文本修补 {p1} 读取失败 {e}");
                }
            }
            return hasPat;
        }

        public void Finalize(AssetsManager manager, AssetsFileInstance assets, AssetFileInfo file)
        {
            field["m_Script"].AsByteArray = protoObject.GetBytes() ;
            file.SetNewData(field);
        }
    }
}
