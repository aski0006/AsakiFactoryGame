using Game.Save.Core;
using System;
using UnityEngine;

namespace Game.Save.Example
{
    public class SettingsSectionProviderExample : MonoBehaviour,
                                           ISaveSectionProvider,
                                           ICustomSaveSectionKey,
                                           IExposeSectionType
    {
        [Serializable]
        public class SettingsSection : ISaveSection
        {
            public float master = 1f;
            public float bgm = 0.8f;
            public float sfx = 0.9f;
            public bool vibration = true;
        }

        public string Key => "Settings";
        public Type SectionType => typeof(SettingsSection);
        public bool Ready => true;

        [SerializeField] private float _master = 1f;
        [SerializeField] private float _bgm = 0.8f;
        [SerializeField] private float _sfx = 0.9f;
        [SerializeField] private bool _vibration = true;

        public float Master => _master;
        public float Bgm => _bgm;
        public float Sfx => _sfx;

        public ISaveSection Capture()
        {
            return new SettingsSection
            {
                master = _master,
                bgm = _bgm,
                sfx = _sfx,
                vibration = _vibration
            };
        }

        public void Restore(ISaveSection section)
        {
            var s = section as SettingsSection;
            if (s == null) return;
            _master = s.master;
            _bgm = s.bgm;
            _sfx = s.sfx;
            _vibration = s.vibration;
            // 可在此处调用实际音频系统更新混音器音量
        }

        public void SetBgm(float v) => _bgm = Mathf.Clamp01(v);
        public void ResetSettings()
        {
            _master = 1f; _bgm = 0.8f; _sfx = 0.9f; _vibration = true;
        }

        private void OnEnable() => SaveManager.Instance?.RegisterProvider(this);
        private void OnDisable() => SaveManager.Instance?.UnregisterProvider(this);
    }
}


