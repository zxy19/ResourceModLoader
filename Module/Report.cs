
namespace ResourceModLoader.Module
{
    enum FileStatus
    {
        OK,
        NO_EFFECT,
        OK_PATCH,
        WARNING,
        ERROR
    }
    class Report
    {
        static List<string> files = new List<string>();
        static List<List<string>> warnings = new List<List<string>>();

        static List<FileStatus> statuses = new List<FileStatus>();
        static List<List<string>> effectTo = new List<List<string>>();

        static Dictionary<string, List<string>> taintBy = new Dictionary<string, List<string>>();

        static public void AddModFile(string name)
        {
            if (files.Contains(name)) return;
            files.Add(name);
            warnings.Add(new List<string>());
            statuses.Add(FileStatus.NO_EFFECT);
            effectTo.Add(new List<string>());
        }

        static public int GetModFileId(string name)
        {
            if (!files.Contains(name))
                AddModFile(name);
            return files.IndexOf(name);
        }
        static public void AddTaintFile(string modFile, string targetFile)
        {
            var modFileId = GetModFileId(modFile);
            if (!taintBy.ContainsKey(targetFile))
                taintBy[targetFile] = new List<string>();

            taintBy[targetFile].Add(modFile);
            effectTo[modFileId].Add(targetFile);
            if (statuses[modFileId] == FileStatus.NO_EFFECT)
            {
                if (targetFile.EndsWith(".bundle"))
                    statuses[modFileId] = FileStatus.OK_PATCH;
                else
                    statuses[modFileId] = FileStatus.OK;
            }
        }

        static public void Warning(string modFile, string message)
        {
            warnings[GetModFileId(modFile)].Add(message);
            statuses[GetModFileId(modFile)] = FileStatus.WARNING;
        }
        static public void Error(string modFile, string message)
        {
            statuses[GetModFileId(modFile)] = FileStatus.ERROR;
            warnings[GetModFileId(modFile)].Add(message);
        }

        static public void Print(string modBase)
        {
            if (files.Count == 0)
            {
                Log.Info("没有mod文件被加载");
                return;
            }
            Log.Info("======================================================");
            Log.Info("污染状况");
            Log.Info("======================================================");
            foreach (var tainted in taintBy)
            {
                for (int i = 0; i < tainted.Value.Count; i++)
                {
                    var name = tainted.Value[i].Replace(modBase, "").Replace("\\", " / ");
                    var k = i == 0 ? tainted.Key : "".PadRight(tainted.Key.Length, ' ');
                    string str = $"{k} <-- {name}";

                    if (tainted.Key.EndsWith(".bundle"))
                    {
                        Log.Warn(str);
                    }
                    else
                    {
                        Log.SuccessPartial(str);
                    }
                }
            }
            Log.Info("======================================================");
            Log.Info("加载文件");
            Log.Info("======================================================");
            bool hasWarning = false;
            for (int i = 0; i < files.Count; i++)
            {
                if (statuses[i] == FileStatus.NO_EFFECT) continue;
                var name = files[i].Replace(modBase, "").Replace("\\", " >");
                var effect = effectTo[i].Count;
                if (statuses[i] == FileStatus.OK)
                    Log.SuccessAll($"{name}({effect})");
                else if (statuses[i] == FileStatus.OK_PATCH)
                    Log.SuccessPartial($"{name}({effect})");
                if (statuses[i] == FileStatus.WARNING)
                {
                    hasWarning = true;
                    Log.Warn($"{name}({effect})");
                }
                if (statuses[i] == FileStatus.ERROR)
                {
                    hasWarning = true;
                    Log.Error($"{name}({effect})");
                }
            }

            if (hasWarning)
            {
                Log.Info("======================================================");
                Log.Info("警告和错误");
                Log.Info("======================================================");
                for (int i = 0; i < files.Count; i++)
                {
                    if (statuses[i] == FileStatus.NO_EFFECT) continue;
                    var name = files[i].Replace(modBase, "").Replace("\\", " >");
                    if (statuses[i] == FileStatus.WARNING)
                    {
                        Log.Warn($"{name}");
                        int c = 0;
                        foreach (var w in warnings[i])
                        {
                            if (c++ > 6)
                            {
                                Log.Warn($"...等总计{warnings[i].Count}个警告");
                                break;
                            }
                            Log.Warn("\t" + w);
                        }
                    }
                    if (statuses[i] == FileStatus.ERROR)
                    {
                        Log.Error($"{name}");
                        foreach (var w in warnings[i])
                        {
                            Log.Error("\t" + w);
                        }
                    }
                }
            }
        }

        internal static void Reset()
        {
            files.Clear();
            warnings.Clear();
            statuses.Clear();
            effectTo.Clear();
            taintBy.Clear();
        }
    }
}
