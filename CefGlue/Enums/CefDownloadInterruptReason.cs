using System;
using System.Collections.Generic;
using System.Text;

namespace Xilium.CefGlue
{
    /// <summary>
    /// Download interrupt reasons. Should be kept in sync with
    /// Chromium's download::DownloadInterruptReason type.
    /// </summary>
    public enum CefDownloadInterruptReason
    {
        None = 0,

        FileFailed = 1,

        FileAccessDenied = 2,

        FileNoSpace = 3,

        FileNameTooLong = 5,

        FileTooLarge = 6,

        FileVirusInfected = 7,

        FileTransientError = 10,

        FileBlocked = 11,

        FileSecurityCheckFailed = 12,

        FileTooShort = 13,

        FileHashMismatch = 14,

        FileSameAsSource = 15,

        NetworkFailed = 20,

        NetworkTimeout = 21,

        NetworkDisconnected = 22,

        NetworkServerDown = 23,

        NetworkInvalidRequest = 24,

        ServerFailed = 30,

        ServerNoRange = 31,

        ServerBadContent = 33,

        ServerUnauthorized = 34,

        ServerCertProblem = 35,

        ServerForbidden = 36,

        ServerUnreachable = 37,

        ServerContentLengthMismatch = 38,

        ServerCrossOriginRedirect = 39,

        UserCanceled = 40,

        UserShutdown = 41,

        Crash = 50,
    }
}
