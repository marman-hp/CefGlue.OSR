namespace Xilium.CefGlue.Wrapper
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    internal static class CefMessageRouter
    {
        public const int ReservedId = 0;

        public const string MessageSuffix = "Msg";

        public const string MemberRequest = "request";
        public const string MemberOnSuccess = "onSuccess";
        public const string MemberOnFailure = "onFailure";
        public const string MemberPersistent = "persistent";

        public const int CanceledErrorCode = -1;
        public const string CanceledErrorMessage = "The query has been canceled";

        public const int ResponseSizeThreshold = 16384;

        public sealed class IdGeneratorInt32
        {
            private int _next_id;

            public int GetNextId()
            {
                var id = ++_next_id;
                if (id == CefMessageRouter.ReservedId)
                    id = ++_next_id;
                return id;
            }
        }

        public sealed class IdGeneratorInt64
        {
            private long _next_id;

            public long GetNextId()
            {
                var id = ++_next_id;
                if (id == CefMessageRouter.ReservedId)
                    id = ++_next_id;
                return id;
            }
        }
    }
}
