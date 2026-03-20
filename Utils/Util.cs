using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace ResourceModLoader.util
{
    internal class Util
    {
        public static int TailEqualLen(string s1, string s2)
        {
            for (int i = 0; i < s1.Length && i < s2.Length; i++)
            {
                if (s1.ElementAt(s1.Length - i - 1) != s2.ElementAt(s2.Length - i - 1))
                    return i;
            }
            if (s1.Length > s2.Length)
                return s2.Length;
            return s1.Length;
        }
    }
}
