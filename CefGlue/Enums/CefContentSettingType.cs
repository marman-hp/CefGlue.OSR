using System;
using System.Collections.Generic;
using System.Text;

namespace Xilium.CefGlue
{
    /// <summary>
    /// Supported content setting types. Some types are platform-specific or only
    /// supported with the Chrome runtime. Should be kept in sync with Chromium's
    /// ContentSettingsType type.
    /// </summary>
    public enum CefContentSettingType
    {
        Cookies,
        Images,
        Javascript,

        Popups,

        Geolocation,
        Notifications,
        AutoSelectCertificate,
        MixedScript,
        MediaStreamMic,
        MediaStreamCamera,
        ProtocolHandlers,
        DeprecatedPpapiBroker,
        AutomaticDownloads,

        MidiSysex,

        SslCertDecisions,
        ProtectedMediaIdentifier,
        AppBanner,
        SiteEngagement,
        DurableStorage,
        UsbChooserData,
        BluetoothGuard,
        BackgroundSync,
        Autoplay,
        ImportantSiteInfo,
        PermissionAutoBlockerData,
        Ads,

        AdsData,

        Midi,

        PasswordProtection,

        MediaEngagement,

        Sound,

        ClientHints,

        Sensors,

        DeprecatedAccessibilityEvents,

        PaymentHandler,

        UsbGuard,

        BackgroundFetch,

        IntentPickerDisplay,

        IdleDetection,

        SerialGuard,
        SerialChooserData,

        PeriodicBackgroundSync,

        BluetoothScanning,

        HidGuard,
        HidChooserData,

        WakeLockScreen,
        WakeLockSystem,

        LegacyCookieAccess,

        FileSystemWriteGuard,

        Nfc,

        BluetoothChooserData,

        ClipboardReadWrite,

        ClipboardSanitizedWrite,

        SafeBrowsingUrlCheckData,

        Vr,
        Ar,

        FileSystemReadGuard,

        StorageAccess,

        CameraPanTiltZoom,

        WindowManagement,

        InsecurePrivateNetwork,

        LocalFonts,

        PermissionAutoRevocationData,

        FileSystemLastPickedDirectory,

        DisplayCapture,

        FileSystemAccessChooserData,

        FederatedIdentitySharing,

        JavascriptJit,

        HttpAllowed,

        FormFillMetadata,

        DeprecatedFederatedIdentityActiveSession,

        AutoDarkWebContent,

        RequestDesktopSite,

        FederatedIdentityApi,

        NotificationInteractions,

        ReducedAcceptLanguage,

        NotificationPermissionReview,

        PrivateNetworkGuard,
        PrivateNetworkChooserData,

        FederatedIdentityIdentityProviderSignInStatus,

        RevokedUnusedSitePermissions,

        TopLevelStorageAccess,

        FederatedIdentityAutoReAuthnPermission,

        FederatedIdentityIdentityProviderRegistration,

        AntiAbuse,

        ThirdPartyStoragePartitioning,

        HttpsEnforced,

        AllScreenCapture,

        CookieControlsMetadata,

        TpcdHeuristicsGrants,

        TpcdMetadataGrants,

        TpcdTrial,

        TopLevelTpcdTrial,

        TopLevelTpcdOriginTrial,

        AutoPictureInPicture,

        FileSystemAccessExtendedPermission,

        FileSystemAccessRestorePermission,

        CapturedSurfaceControl,

        SmartCardGuard,
        SmartCardData,

        WebPrinting,

        AutomaticFullscreen,

        SubAppInstallationPrompts,

        SpeakerSelection,

        DirectSockets,

        KeyboardLock,

        PointerLock,

        RevokedAbusiveNotificationPermissions,

        TrackingProtection,

        DisplayMediaSystemAudio,

        JavascriptOptimizer,

        StorageAccessHeaderOriginTrial,

        HandTracking,

        WebAppInstallation,

        DirectSocketsPrivateNetworkAccess,

        LegacyCookieScope,

        AreSuspiciousNotificationsAllowlistedByUser,

        ControlledFrame,

        NumValues,
    }
}
