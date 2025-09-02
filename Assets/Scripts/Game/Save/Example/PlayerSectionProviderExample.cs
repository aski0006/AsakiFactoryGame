using Game.Save.Core;
using System;
using UnityEngine;

namespace Game.Save.Example
{
    public class PlayerSectionProviderExample : MonoBehaviour,
                                         ISaveSectionProvider,
                                         ICustomSaveSectionKey,
                                         IExposeSectionType
    {
        [Serializable]
        public class PlayerSection : ISaveSection
        {
            public string playerId;
            public int level;
            public float exp;
            public float hp;
            public float maxHp;
            public float posX;
            public float posY;
            public float posZ;
        }

        // 状态
        public string PlayerId => _playerId;
        public int Level => _level;
        public float Exp => _exp;
        public float Hp => _hp;
        public float MaxHp => _maxHp;

        [SerializeField] private string _playerId = "player-001";
        [SerializeField] private int _level = 1;
        [SerializeField] private float _exp = 0f;
        [SerializeField] private float _hp = 100f;
        [SerializeField] private float _maxHp = 100f;

        public string Key => "Player";                 // 自定义稳定 Key
        public Type SectionType => typeof(PlayerSection);
        public bool Ready => true;                     // 可在依赖初始化完再返回 true

        public ISaveSection Capture()
        {
            return new PlayerSection
            {
                playerId = _playerId,
                level = _level,
                exp = _exp,
                hp = _hp,
                maxHp = _maxHp,
                posX = transform.position.x,
                posY = transform.position.y,
                posZ = transform.position.z
            };
        }

        public void Restore(ISaveSection section)
        {
            var s = section as PlayerSection;
            if (s == null) return; // 初次没有
            _playerId = s.playerId;
            _level = s.level;
            _exp = s.exp;
            _hp = Mathf.Min(s.hp, s.maxHp);
            _maxHp = s.maxHp <= 0 ? 100 : s.maxHp;
            transform.position = new Vector3(s.posX, s.posY, s.posZ);
        }

        public void AddExp(float amount)
        {
            _exp += amount;
            while (_exp >= NeedExpFor(_level))
            {
                _exp -= NeedExpFor(_level);
                _level++;
                _maxHp += 10;
                _hp = _maxHp;
            }
        }

        private float NeedExpFor(int lvl) => 10 + (lvl - 1) * 5;

        public void TakeDamage(float dmg)
        {
            _hp = Mathf.Max(0, _hp - dmg);
        }

        public void Heal(float v)
        {
            _hp = Mathf.Min(_maxHp, _hp + v);
        }

        public void ResetToDefault()
        {
            _playerId = "player-001";
            _level = 1;
            _exp = 0;
            _maxHp = 100;
            _hp = _maxHp;
            transform.position = Vector3.zero;
        }

        private void OnEnable() => SaveManager.Instance?.RegisterProvider(this);
        private void OnDisable() => SaveManager.Instance?.UnregisterProvider(this);
    }
}
