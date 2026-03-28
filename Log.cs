using AssetsTools.NET.Texture.TextureDecoders.CrnUnity;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResourceModLoader
{
    class Log
    {
        private static bool disablePrefix = false;
        private static bool inProgress = false;
        private static bool padNext = false;
        private static int progressCount = 0;
        private static int progressTotal = 0;
        private static void WriteLine(string message)
        {
            if (inProgress || padNext)
            {
                message = "\r\x1b[K" + message;
            }
            Console.WriteLine(message);
            padNext = false;
        }
        public static void SetupProgress(int total)
        {
            inProgress = true;
            progressTotal = total;
            progressCount = 0;
        }
        public static void StepProgress(string desc,int step = 1)
        {
            progressCount+=step;
            string message = $"[{progressCount.ToString().PadLeft(3, ' ')}/{progressTotal.ToString().PadLeft(3, ' ')}]";
            if (progressTotal <= 0)
                message = "";
            message += desc ;
            if(message.Length> Console.WindowWidth - 25)
            {
                message = message.Substring(0,30) + "..." + message.Substring(message.Length - (Console.WindowWidth - 65), (Console.WindowWidth - 65));
            }
            message = "\r\x1b[K" + message;
            Console.Write(message);
        }
        public static void FinalizeProgress(string? desc=null) {
            if (desc != null)
                WriteLine(desc);
            else
                padNext = true;
            inProgress = false;
            progressCount = 0;
            progressTotal = 0;
        }
        public static void Wait()
        {
            WriteLine("按任意键继续");
            Console.ReadKey();
        }
        public static void SetPrefixEnable(bool enable)
        {
            disablePrefix = !enable;
        }
        public static void PrefixWriteLine(string prefix,string content)
        {
            if(disablePrefix)
                Console.WriteLine(content);
            else
                Console.WriteLine($"[{prefix}] {content}");
        }
        public static void Info(string t)
        {
            Console.ResetColor();
            PrefixWriteLine("I", t);
        }

        public static void Error(string t)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            PrefixWriteLine("E", t);
            Console.ResetColor();
        }
        public static void Warn(string t)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            PrefixWriteLine("W", t);
            Console.ResetColor();
        }

        public static void Debug(string t)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            PrefixWriteLine("D", t);
            Console.ResetColor();
        }

        public static void SuccessAll(string t)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            PrefixWriteLine("SA", t);
            Console.ResetColor();
        }

        public static void SuccessPartial(string t)
        {
            Console.ForegroundColor = ConsoleColor.Blue;
            PrefixWriteLine("SP", t);
            Console.ResetColor();
        }
    }
}