using System;
using System.Collections.Generic;
using UnityEngine;

namespace Project.Character
{
    [CreateAssetMenu(menuName = "Project/Character/Item Definition", fileName = "ItemDefinition")]
    public sealed class ItemDefinition : ScriptableObject
    {
        [Header("Identity")]
        [SerializeField] private string itemId = "item_001";
        [SerializeField] private string displayName = "Предмет";

        [Header("Visual")]
        [SerializeField] private Sprite icon;

        [Header("Equip")]
        [SerializeField] private bool equippable = true;
        [SerializeField] private EquipmentSlotId slot = EquipmentSlotId.Helmet;

        [Header("Stats (additive)")]
        [SerializeField] private List<StatModifier> modifiers = new();

        [Header("Stats (simple fields, used when modifiers empty)")]
        [SerializeField] private int hp;
        [SerializeField] private int damage;
        [SerializeField] private int armor;
        [SerializeField] private int healing;
        [SerializeField] private float critChance;

        public string ItemId => itemId;
        public string DisplayName => displayName;
        public Sprite Icon => icon;
        public bool Equippable => equippable;
        public EquipmentSlotId Slot => slot;
        public IReadOnlyList<StatModifier> Modifiers => modifiers;

        public float GetStatValue(string statId)
        {
            if (!string.IsNullOrEmpty(statId) && modifiers != null && modifiers.Count > 0)
            {
                var sum = 0f;
                for (var i = 0; i < modifiers.Count; i++)
                {
                    var m = modifiers[i];
                    if (m == null || string.IsNullOrEmpty(m.statId)) continue;
                    if (m.statId == statId) sum += m.add;
                }
                return sum;
            }

            return statId switch
            {
                StatId.Hp => hp,
                StatId.Damage => damage,
                StatId.Armor => armor,
                StatId.Healing => healing,
                StatId.CritChance => critChance,
                _ => 0f,
            };
        }

        [Serializable]
        public sealed class StatModifier
        {
            public string statId = StatId.Hp;
            public float add = 0;
        }
    }
}

