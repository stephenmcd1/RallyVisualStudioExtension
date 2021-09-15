using System;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.Win32;

namespace RallyExtension.Extension.Utilities
{
    /// <summary>
    /// Helper class for getting and setting saved options.
    /// </summary>
    public class OptionsHelper
    {
        private readonly IServiceProvider _serviceProvider;

        public OptionsHelper(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public string GetStringOption(string name)
        {
            return (string) GetOption(name);
        }

        public string GetEncryptedOption(string name)
        {
            var raw = (byte[]) GetOption(name);
            if (raw == null)
            {
                return null;
            }

            return Encoding.Unicode.GetString(ProtectedData.Unprotect(raw, null,
                DataProtectionScope.CurrentUser));
        }

        public void SetEncryptedOption(string name, string value)
        {
            var encryptedData = ProtectedData.Protect(Encoding.Unicode.GetBytes(value), null, DataProtectionScope.CurrentUser);
            SetOption(name, encryptedData, RegistryValueKind.Binary);
        }

        public void SetStringOption(string name, string value)
        {
            SetOption(name, value, RegistryValueKind.String);
        }

        private void SetOption(string name, object value, RegistryValueKind valueKind)
        {
            using (var root = VSRegistry.RegistryRoot(_serviceProvider, __VsLocalRegistryType.RegType_UserSettings, true))
            using (var key = root.CreateSubKey("RallyVSExtension"))
            {
                key?.SetValue(name, value, valueKind);
            }
        }

        private object GetOption(string name)
        {
            using (var root = VSRegistry.RegistryRoot(_serviceProvider, __VsLocalRegistryType.RegType_UserSettings, true))
            using (var key = root.CreateSubKey("RallyVSExtension"))
            {
                return key?.GetValue(name);
            }
        }
    }
}