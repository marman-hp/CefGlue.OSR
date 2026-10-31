using System;
using System.Linq;

namespace Xilium.CefGlue.Common.Shared
{
    public class CustomScheme
    {
        private const string CommandLineSchemesSeparator = ";";
        private const string CommandLinePropertiesSeparator = "|";

        public string SchemeName { get; set; }

        public string DomainName { get; set; }

        public bool IsStandard { get; set; }

        public bool IsLocal { get; set; }

        public bool IsDisplayIsolated { get; set; }

        public bool IsSecure { get; set; }

        public bool IsCorsEnabled { get; set; }

        public bool IsCSPBypassing { get; set; }

        public bool IsFetchEnabled { get; set; }

        public CefSchemeHandlerFactory SchemeHandlerFactory { get; set; }

        public CustomScheme()
        {
            IsStandard = true;
            IsLocal = false;
            IsDisplayIsolated = false;
            IsSecure = true;
            IsCorsEnabled = true;
            IsCSPBypassing = false;
            IsFetchEnabled = true;
        }

        public CefSchemeOptions Options
        {
            get
            {
                var options = CefSchemeOptions.None;
                if (IsStandard)
                {
                    options |= CefSchemeOptions.Standard;
                }
                if (IsLocal)
                {
                    options |= CefSchemeOptions.Local;
                }
                if (IsSecure)
                {
                    options |= CefSchemeOptions.Secure;
                }
                if (IsDisplayIsolated)
                {
                    options |= CefSchemeOptions.DisplayIsolated;
                }
                if (IsCorsEnabled)
                {
                    options |= CefSchemeOptions.CorsEnabled;
                }
                if (IsCSPBypassing)
                {
                    options |= CefSchemeOptions.CspBypassing;
                }
                if (IsFetchEnabled)
                {
                    options |= CefSchemeOptions.FetchEnabled;
                }
                return options;
            }
        }

        private string SerializeToCommandLineValue()
        {
            return SchemeName + CommandLinePropertiesSeparator + DomainName + CommandLinePropertiesSeparator + ((int)Options).ToString();
        }

        private static CustomScheme DeserializeFromCommandLineValue(string value)
        {
            var tokens = value.Split(new string[] { CommandLinePropertiesSeparator }, StringSplitOptions.None);
            if (tokens.Length < 3)
            {
                return null;
            }

            Enum.TryParse<CefSchemeOptions>(tokens[2], out var properties);

            return new CustomScheme()
            {
                SchemeName = tokens[0],
                DomainName = tokens[1],
                IsStandard = properties.HasFlag(CefSchemeOptions.Standard),
                IsLocal = properties.HasFlag(CefSchemeOptions.Local),
                IsDisplayIsolated = properties.HasFlag(CefSchemeOptions.DisplayIsolated),
                IsSecure = properties.HasFlag(CefSchemeOptions.Secure),
                IsCorsEnabled = properties.HasFlag(CefSchemeOptions.CorsEnabled),
                IsCSPBypassing = properties.HasFlag(CefSchemeOptions.CspBypassing),
                IsFetchEnabled = properties.HasFlag(CefSchemeOptions.FetchEnabled)
            };
        }

        internal static string ToCommandLineValue(CustomScheme[] schemes)
        {
            return string.Join(CommandLineSchemesSeparator, schemes.Select(s => s.SerializeToCommandLineValue()));
        }

        internal static CustomScheme[] FromCommandLineValue(string value)
        {
            var schemes = value?.Split(new string[] { CommandLineSchemesSeparator }, StringSplitOptions.RemoveEmptyEntries);
            if (schemes == null)
            {
                return new CustomScheme[0];
            }
            return schemes.Select(DeserializeFromCommandLineValue).Where(s => s != null).ToArray();
        }
    }
}
