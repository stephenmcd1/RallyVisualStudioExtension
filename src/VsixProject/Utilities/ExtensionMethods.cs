using RallyExtension.Extension.Annotations;

namespace RallyExtension.Extension.Utilities
{
    public static class ExtensionMethods
    {
        /// <summary>
        /// Makes the string the specified length inclusive of the the trailing string.
        /// </summary>
        /// <param name="str">The string.</param>
        /// <param name="totalLength">The total width.</param>
        /// <param name="trailingString">The trailing string.</param>
        /// <returns></returns>
        [ContractAnnotation("str:null => null; str:notnull => notnull")]
        public static string TrimTo(this string str, int totalLength, string trailingString)
        {
            return string.IsNullOrEmpty(str) || str.Length <= totalLength
                ? str
                : str.Substring(0, totalLength - trailingString.Length) + trailingString;
        }
    }
}