using System;
using System.Collections.Generic;

namespace LoL_Companion
{
    public static class StringSpliter
    {
        public static IEnumerable<String> SplitString(this String clipboard, Int32 partLength)
        {
            if (clipboard == null)
                throw new ArgumentNullException("s");
            if (partLength <= 0)
                throw new ArgumentException("The size of the parts must be positive.", "partLength");

            for (var i = 0; i < clipboard.Length; i += partLength)
                yield return clipboard.Substring(i, Math.Min(partLength, clipboard.Length - i));
        }
    }
}