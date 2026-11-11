using System;
using wb.Core;

namespace wb.UI
{
    /// <summary>
    /// Persistent configuration model for experimental features under 'Tools > Experimental'.
    /// Serialized to %LocalAppData%\wb\experimental_config.json via StorageHelper.
    /// </summary>
    public class ExperimentalConfigModel
    {
        public bool EnableProgressBarStardust { get; set; }
        public bool EnableGoogleSecurityCodeBanner { get; set; }

        public ExperimentalConfigModel()
        {
            EnableProgressBarStardust = false;
            EnableGoogleSecurityCodeBanner = true;
        }
    }

    /// <summary>
    /// Central manager for experimental browser flags and features.
    /// </summary>
    public static class ExperimentalManager
    {
        private const string ConfigFileName = "experimental_config.json";
        private static ExperimentalConfigModel _config;
        private static readonly object _syncLock = new object();
        private static bool _isInitialized = false;

        public static event EventHandler ConfigChanged;

        static ExperimentalManager()
        {
            Initialize();
        }

        public static void Initialize()
        {
            lock (_syncLock)
            {
                if (_isInitialized) return;
                _isInitialized = true;

                try
                {
                    _config = StorageHelper.LoadObject<ExperimentalConfigModel>(ConfigFileName);
                    if (_config == null)
                    {
                        _config = new ExperimentalConfigModel();
                    }
                }
                catch
                {
                    _config = new ExperimentalConfigModel();
                }
            }
        }

        public static bool EnableProgressBarStardust
        {
            get
            {
                Initialize();
                lock (_syncLock)
                {
                    return _config != null && _config.EnableProgressBarStardust;
                }
            }
            set
            {
                Initialize();
                bool changed = false;
                lock (_syncLock)
                {
                    if (_config == null) _config = new ExperimentalConfigModel();
                    if (_config.EnableProgressBarStardust != value)
                    {
                        _config.EnableProgressBarStardust = value;
                        changed = true;
                        SaveInternal();
                    }
                }

                if (changed)
                {
                    try
                    {
                        EventHandler handler = ConfigChanged;
                        if (handler != null)
                        {
                            handler(null, EventArgs.Empty);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        public static bool EnableGoogleSecurityCodeBanner
        {
            get
            {
                Initialize();
                lock (_syncLock)
                {
                    return _config == null || _config.EnableGoogleSecurityCodeBanner;
                }
            }
            set
            {
                Initialize();
                bool changed = false;
                lock (_syncLock)
                {
                    if (_config == null) _config = new ExperimentalConfigModel();
                    if (_config.EnableGoogleSecurityCodeBanner != value)
                    {
                        _config.EnableGoogleSecurityCodeBanner = value;
                        changed = true;
                        SaveInternal();
                    }
                }

                if (changed)
                {
                    try
                    {
                        EventHandler handler = ConfigChanged;
                        if (handler != null)
                        {
                            handler(null, EventArgs.Empty);
                        }
                    }
                    catch
                    {
                    }
                }
            }
        }

        private static void SaveInternal()
        {
            try
            {
                if (_config != null)
                {
                    StorageHelper.SaveObject(ConfigFileName, _config);
                }
            }
            catch
            {
            }
        }
    }
}
