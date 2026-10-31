namespace Xilium.CefGlue.Interop
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    internal unsafe struct cef_string_userfree
    {
        public static string ToString(cef_string_userfree* str)
        {
            if (str != null)
            {
                var result = cef_string_t.ToString((cef_string_t*)str);
                libcef.string_userfree_free(str);
                return result;
            }

            return null;
        }

    }
}
